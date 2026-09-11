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
    [SerializeField] private SensorTimingBridge sensorTiming;

    private bool _logging;
    private string _filePath;
    private readonly StringBuilder _buffer = new StringBuilder();
    private int _bufferedRows;
    private float _sampleAccumulator;

    // 書き込みハンドルは走行中つけっぱなしにする。File.AppendAllText は
    // フラッシュのたびに open→write→close を行い、さらに _buffer.ToString() で
    // 毎回24KB前後の一時文字列を確保していた。60分走(§10)では約1,800回の
    // 開閉と数十MBのGCになり、M2P 20ms(§10)のフレーム予算を脅かす
    private StreamWriter _writer;

    /// <summary>書き出せなかった行数。0以外ならこのCSVは不完全。</summary>
    public int DroppedRowCount { get; private set; }

    // タイムスタンプはサンプル時刻(開始epoch + 連番×10ms)で採番する。
    // 書き込み時刻を使うと1フレームで複数行を書いた際に同一msが重複し、
    // 100Hzサンプルとして解析(§11.2 CSV解析による遅延評価)できなくなる
    private long _logStartEpochMs;
    private long _sampleIndex;

    // ネイティブ100Hzサンプルの受け皿(毎フレーム使い回す)と、時刻の基準点
    private readonly double[] _imuSampleTimes = new double[SensorTimingBridge.MaxDrainPerFrame];
    private readonly float[]  _imuSampleXyz   = new float[SensorTimingBridge.MaxDrainPerFrame * 3];
    private double _mediaTimeAtLogStart;

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
        if (sensorTiming == null)
            sensorTiming = FindFirstObjectByType<SensorTimingBridge>(FindObjectsInactive.Include);
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
        _imuExternal ? "external"
        : (sensorTiming != null && sensorTiming.IsImuStreaming) ? "native100hz"
        : (_deviceImuActive ? "device" : "approximated");

    /// <summary>ネイティブ100Hzサンプルから書いた行数(0ならフレーム同期の擬似100Hz)。</summary>
    public long NativeImuRowCount { get; private set; }

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

        bool shouldLog = avatarEngine.IsRunMotionActive;

        if (shouldLog && !_logging) StartLogging();
        else if (!shouldLog && _logging) StopLogging();

        if (!_logging) return;

        // ① ネイティブの100Hz IMU があれば **1サンプル=1行**で書く。
        //    行数だけでなく中身も本物の100Hzになり、タイムスタンプもサンプル自身の
        //    観測時刻になる(下の擬似100Hzは同じ値を複数行に複製してしまう)
        if (!_imuExternal && TryAppendNativeImuRows()) return;

        // ② ネイティブが無い場合(エディタ・Swift供給時)の擬似100Hz。
        //    優先順位: Swift供給 > 端末IMU(CoreMotion) > カメラ速度差分の近似
        if (!_imuExternal && !TryReadDeviceImu())
            UpdateImuApproximation();

        // 経過時間分の行をまとめて書き出す。**同一フレーム内の行は同じ値の複製**で、
        // 独立した100Hzサンプルではない(解析時はこの前提で読むこと)
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

    /// <summary>
    /// ネイティブ(CoreMotion)に貯まった100Hzサンプルを引き取り、1件1行で書く。
    /// 実機でのみ成立する。書けたら true。
    /// </summary>
    private bool TryAppendNativeImuRows()
    {
        if (sensorTiming == null || !sensorTiming.IsImuStreaming) return false;
        if (_mediaTimeAtLogStart <= 0.0) return false; // 時刻の基準が取れていない

        int count = sensorTiming.DrainImuSamples(_imuSampleTimes, _imuSampleXyz);
        if (count <= 0) return false;

        for (int i = 0; i < count; i++)
        {
            _imuAccel = new Vector3(_imuSampleXyz[i * 3],
                                    _imuSampleXyz[i * 3 + 1],
                                    _imuSampleXyz[i * 3 + 2]);

            // サンプル自身の観測時刻(CACurrentMediaTime軸)を、開始時に取った
            // 基準点でepoch msへ写す
            double offsetSeconds = _imuSampleTimes[i] - _mediaTimeAtLogStart;
            if (offsetSeconds < 0.0) continue; // ログ開始前に貯まっていた古いサンプル

            AppendRowAt(_logStartEpochMs + (long)(offsetSeconds * 1000.0));
            NativeImuRowCount++;
        }

        return true;
    }

    private void AppendRow()
    {
        // 100Hz固定間隔のサンプル時刻(単調増加・10ms刻み)
        long tsMs = _logStartEpochMs + (long)(_sampleIndex * (SampleIntervalSeconds * 1000f));
        _sampleIndex++;
        AppendRowAt(tsMs);
    }

    private void AppendRowAt(long tsMs)
    {
        var ci = CultureInfo.InvariantCulture;

        Vector3 avatarPos = avatarEngine != null ? avatarEngine.transform.position : Vector3.zero;

        // latency_m2p は「実測できたときだけ」書く。-1 = 未計測。
        //
        // 実測の中身は SensorTimingBridge が持つ:
        //   ARKitフレームのタイムスタンプ(センサー時刻) → CADisplayLink.targetTimestamp
        //   (提示予定時刻)。どちらも CACurrentMediaTime 軸なので直接引ける。
        // 合成値(実質フレーム時間)を書いていた頃は、60fpsなら約16msが全行に並び、
        // §11.2 の「CSV解析で20msを評価」が測定ではなく構造によって合格していた。
        double latency = MotionToPhotonMath.Unmeasured;
        if (sensorTiming != null && sensorTiming.TryGetLatencyMs(out double measuredMs))
            latency = measuredMs;

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
        // 走行開始と同時にネイティブ計測(M2P + 100Hz IMU)を起動する。
        // 常時走らせないのは§10の60分稼働・バッテリー要件のため
        if (sensorTiming != null) sensorTiming.StartMeasuring();

        // ネイティブが無い環境のための端末IMU(Input.gyro)も従来どおり起動する
        EnableDeviceImu();

        string dir = Path.Combine(Application.persistentDataPath, "RunLogs");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, $"Log_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");

        try
        {
            _writer = new StreamWriter(_filePath, append: false, encoding: new UTF8Encoding(false));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[TELEMETRY] CSVを開けませんでした ({_filePath}): {e.Message}");
            _writer = null;
        }

        _buffer.Clear();
        _buffer.Append(Header).Append('\n');
        _bufferedRows = 0;
        DroppedRowCount = 0;
        _sampleAccumulator = 0f;
        _logStartEpochMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _sampleIndex = 0;
        NativeImuRowCount = 0;

        // ネイティブのサンプル時刻(端末起動からの秒)をepochへ写すための基準点。
        // 実機以外では0が返り、ネイティブ経路は使われない
        _mediaTimeAtLogStart = sensorTiming != null ? sensorTiming.CurrentMediaTime : 0.0;
        if (_mediaTimeAtLogStart > 0.0)
            sensorTiming.DrainImuSamples(_imuSampleTimes, _imuSampleXyz); // 開始前の滞留を捨てる
        _camInit = false;
        _logging = true;

        Debug.Log($"[TELEMETRY] 100Hz CSVログ開始: {_filePath}");
    }

    private void StopLogging()
    {
        Flush();
        CloseWriter();
        _logging = false;
        if (sensorTiming != null) sensorTiming.StopMeasuring();
        if (DroppedRowCount > 0)
            Debug.LogError($"[TELEMETRY] CSVログ終了 — 不完全 ({DroppedRowCount} 行欠落): {_filePath}");
        else
            Debug.Log($"[TELEMETRY] CSVログ終了: {_filePath}");
    }

    private void Flush()
    {
        if (_buffer.Length == 0) return;

        if (_writer == null)
        {
            // 開けていない = このセッションのCSVは残らない。黙って捨てず件数を残す
            DroppedRowCount += _bufferedRows;
            _buffer.Clear();
            _bufferedRows = 0;
            return;
        }

        try
        {
            // StringBuilderを直接渡す(ToString()の大きな一時文字列を作らない)
            _writer.Write(_buffer);
            _writer.Flush(); // 従来と同じ耐久性: 200行毎にOSへ確実に渡す
        }
        catch (System.Exception e)
        {
            // PoCの成果物であるCSVが欠けたことを、件数として必ず残す
            DroppedRowCount += _bufferedRows;
            Debug.LogError($"[TELEMETRY] CSV書き出し失敗 (累計欠落 {DroppedRowCount} 行): {e.Message}");
        }
        _buffer.Clear();
        _bufferedRows = 0;
    }

    private void CloseWriter()
    {
        if (_writer == null) return;
        try { _writer.Dispose(); }
        catch (System.Exception e) { Debug.LogError($"[TELEMETRY] CSVクローズ失敗: {e.Message}"); }
        _writer = null;
    }

    /// <summary>再走行対応: ログ状態を破棄する(保存済みCSVはそのまま残る)。</summary>
    public void ResetSession()
    {
        if (_logging) StopLogging();
        CloseWriter();
        _filePath = null;
        _buffer.Clear();
        _bufferedRows = 0;
        _sampleAccumulator = 0f;
        _camInit = false;
    }

    // F-11のCSVは200行(≒2秒)毎にしかディスクへ渡していないため、走行中に
    // アプリをスワイプ終了されると末尾がまるごと消える。背面へ回る時点で必ず
    // 吐き出す(OnApplicationPause はスワイプ終了前に必ず呼ばれる)
    void OnApplicationPause(bool paused)
    {
        if (paused && _logging) Flush();
    }

    void OnApplicationQuit()
    {
        if (_logging) Flush();
    }

    void OnDestroy()
    {
        if (_logging) Flush();
        CloseWriter();
    }
}
