using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// F-11 リアルタイムデータ保存 (基本設計書 §5.2):
/// 実証実験(PoC)の技術限界を定量分析するため、走行中のセンサー生データと
/// 描画遅延を 100Hz でローカルCSVへ出力する。ソラド社への技術資産譲渡の基盤。
///
/// ファイル: <persistentDataPath>/RunLogs/Log_YYYYMMDD_HHMMSS.csv
/// 列(§5.2): timestamp, gps_latitude, gps_longitude, imu_accel_x/y/z,
///           avatar_pos_x, avatar_pos_z, latency_m2p
///
/// GPS緯度経度・IMU加速度は実機ではSwift(CoreLocation/CoreMotion)から
/// SetGpsCoordinates/SetImuAcceleration で供給する。エディタではIMUを
/// カメラ速度差分で近似し、GPSは0とする。
/// AvatarEngineと同じGameObjectに置く(Bootstrapが自動装着)。
/// </summary>
public class RunTelemetryLogger : MonoBehaviour
{
    private const string Header =
        "timestamp,gps_latitude,gps_longitude,imu_accel_x,imu_accel_y,imu_accel_z," +
        "avatar_pos_x,avatar_pos_z,latency_m2p";
    private const float SampleIntervalSeconds = 0.01f; // 100Hz
    private const int FlushEveryRows = 200;            // ~2秒毎にディスクフラッシュ

    [Header("References (auto-found if empty)")]
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private Transform userCamera;
    [SerializeField] private LatencyBenchmarkRunner latencyRunner;

    private bool _logging;
    private string _filePath;
    private readonly StringBuilder _buffer = new StringBuilder();
    private int _bufferedRows;
    private float _sampleAccumulator;

    // タイムスタンプはサンプル時刻(開始epoch + 連番×10ms)で採番する。
    // 書き込み時刻を使うと1フレームで複数行を書いた際に同一msが重複し、
    // 100Hzサンプルとして解析(§11.2 CSV解析による遅延評価)できなくなる
    private long _logStartEpochMs;
    private long _sampleIndex;

    // 実機供給値(未供給時は下記のエディタ近似/0)
    private double _gpsLat, _gpsLon;
    private bool _gpsExternal;
    private Vector3 _imuAccel;
    private bool _imuExternal;

    // 実機IMU(CoreMotion)。Unityの Input.gyro は iOS では CoreMotion が実体なので
    // Swiftブリッジを介さずに100Hzの実測加速度が取れる — UnitySendMessageで
    // 毎秒100件のJSONを流すより遥かに安く、配線も要らない
    private bool _deviceImuActive;
    private const float StandardGravity = 9.80665f; // Input.gyro は g 単位で返る

    // エディタ近似用: カメラ速度の差分で加速度を出す
    private Vector3 _lastCamPos;
    private Vector3 _lastCamVel;
    private bool _camInit;

    public bool IsLogging => _logging;
    public string CurrentFilePath => _filePath;

    void Awake()
    {
        if (avatarEngine == null)
            avatarEngine = GetComponent<AvatarEngine>() ?? FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (userCamera == null && Camera.main != null)
            userCamera = Camera.main.transform;
        if (latencyRunner == null)
            latencyRunner = FindFirstObjectByType<LatencyBenchmarkRunner>(FindObjectsInactive.Include);
    }

    // ── 実機(Swift)からの供給API ─────────────────────────────────────────
    public void SetGpsCoordinates(double latitude, double longitude)
    {
        _gpsLat = latitude;
        _gpsLon = longitude;
        _gpsExternal = true;
    }

    /// <summary>
    /// 外部(Swift/CoreMotion)からIMU加速度を供給する。呼ばれた時点で
    /// 端末IMU・エディタ近似の両方より優先される。
    /// </summary>
    public void SetImuAcceleration(Vector3 accelMetersPerSec2)
    {
        _imuAccel = accelMetersPerSec2;
        _imuExternal = true;
    }

    /// <summary>IMUの取得元。CSVの信頼性判断とE2E検証に使う。</summary>
    public string ImuSource =>
        _imuExternal ? "external" : (_deviceImuActive ? "device" : "approximated");

    /// <summary>
    /// 端末のIMU(iOSではCoreMotion)を100Hzで起動する。F-11のCSVの
    /// imu_accel_x/y/z 列を実測で埋めるための唯一の供給源(H5)。
    /// </summary>
    private void EnableDeviceImu()
    {
        if (_deviceImuActive || !SystemInfo.supportsGyroscope) return;

        Input.gyro.enabled = true;
        Input.gyro.updateInterval = SampleIntervalSeconds; // 0.01s = 100Hz
        _deviceImuActive = true;
        Debug.Log("[TELEMETRY] 端末IMU(CoreMotion)を100Hzで起動 — CSVのimu_accel列は実測値になります");
    }

    /// <summary>
    /// 端末IMUから重力成分を除いた加速度を取り込む(m/s²へ換算)。
    /// Input.gyro.userAcceleration は g 単位・重力除去済み。
    /// </summary>
    private bool TryReadDeviceImu()
    {
        if (!_deviceImuActive) return false;

        Vector3 g = Input.gyro.userAcceleration;
        _imuAccel = new Vector3(g.x, g.y, g.z) * StandardGravity;
        return true;
    }

    void Update()
    {
        if (avatarEngine == null) return;

        bool shouldLog = avatarEngine.HasStarted && !avatarEngine.IsSessionEnded;

        if (shouldLog && !_logging) StartLogging();
        else if (!shouldLog && _logging) StopLogging();

        if (!_logging) return;

        // 優先順位: Swift供給 > 端末IMU(CoreMotion) > カメラ速度差分の近似
        if (!_imuExternal && !TryReadDeviceImu())
            UpdateImuApproximation();

        // 100Hz サンプリング: 経過時間分の行をまとめて書き出す
        _sampleAccumulator += Time.deltaTime;
        int guard = 0; // 1フレームで書きすぎない安全弁(低fps時)
        while (_sampleAccumulator >= SampleIntervalSeconds && guard++ < 50)
        {
            _sampleAccumulator -= SampleIntervalSeconds;
            AppendRow();
        }
    }

    private void UpdateImuApproximation()
    {
        if (userCamera == null) return;

        if (!_camInit)
        {
            _lastCamPos = userCamera.position;
            _lastCamVel = Vector3.zero;
            _camInit = true;
            return;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 vel = (userCamera.position - _lastCamPos) / dt;
        _imuAccel = (vel - _lastCamVel) / dt;
        _lastCamPos = userCamera.position;
        _lastCamVel = vel;
    }

    private void AppendRow()
    {
        var ci = CultureInfo.InvariantCulture;
        // 100Hz固定間隔のサンプル時刻(単調増加・10ms刻み)
        long tsMs = _logStartEpochMs + (long)(_sampleIndex * (SampleIntervalSeconds * 1000f));
        _sampleIndex++;

        Vector3 avatarPos = avatarEngine != null ? avatarEngine.transform.position : Vector3.zero;

        double latency = latencyRunner != null ? latencyRunner.AverageTotalMs : -1.0;
        if (latency <= 0) latency = Time.deltaTime * 1000.0; // フォールバック

        _buffer.Append(tsMs).Append(',')
            .Append(_gpsLat.ToString("F7", ci)).Append(',')
            .Append(_gpsLon.ToString("F7", ci)).Append(',')
            .Append(_imuAccel.x.ToString("F4", ci)).Append(',')
            .Append(_imuAccel.y.ToString("F4", ci)).Append(',')
            .Append(_imuAccel.z.ToString("F4", ci)).Append(',')
            .Append(avatarPos.x.ToString("F4", ci)).Append(',')
            .Append(avatarPos.z.ToString("F4", ci)).Append(',')
            .Append(latency.ToString("F2", ci))
            .Append('\n');

        if (++_bufferedRows >= FlushEveryRows)
            Flush();
    }

    private void StartLogging()
    {
        // 走行開始と同時に端末IMUを起動する(F-11のimu_accel列を実測で埋める)
        EnableDeviceImu();

        string dir = Path.Combine(Application.persistentDataPath, "RunLogs");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, $"Log_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");

        _buffer.Clear();
        _buffer.Append(Header).Append('\n');
        _bufferedRows = 0;
        _sampleAccumulator = 0f;
        _logStartEpochMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _sampleIndex = 0;
        _camInit = false;
        _logging = true;

        Debug.Log($"[TELEMETRY] 100Hz CSVログ開始: {_filePath}");
    }

    private void StopLogging()
    {
        Flush();
        _logging = false;
        Debug.Log($"[TELEMETRY] CSVログ終了: {_filePath}");
    }

    private void Flush()
    {
        if (_buffer.Length == 0) return;
        try
        {
            File.AppendAllText(_filePath, _buffer.ToString());
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[TELEMETRY] CSV書き出し失敗: {e.Message}");
        }
        _buffer.Clear();
        _bufferedRows = 0;
    }

    /// <summary>再走行対応: ログ状態を破棄する(保存済みCSVはそのまま残る)。</summary>
    public void ResetSession()
    {
        if (_logging) StopLogging();
        _filePath = null;
        _buffer.Clear();
        _bufferedRows = 0;
        _sampleAccumulator = 0f;
        _camInit = false;
    }

    void OnDestroy()
    {
        if (_logging) Flush();
    }
}
