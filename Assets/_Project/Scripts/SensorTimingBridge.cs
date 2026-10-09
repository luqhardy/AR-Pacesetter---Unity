using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// ネイティブ計測基盤への唯一の窓口 (第1期PoCの核)。
/// <c>Assets/Plugins/iOS/ARVisionSensorTiming.mm</c> と1対1で対応する。
///
/// <para>担当は2つ:</para>
/// <list type="number">
/// <item><b>Motion-to-Photon の実測</b> (§10: 20ms以内・最大許容30ms)。
///   ARKitフレームのタイムスタンプ(センサー時刻)と <c>CADisplayLink.targetTimestamp</c>
///   (提示予定時刻)の差を取る。どちらも <c>CACurrentMediaTime()</c> と同じ時間軸。
///   <b>合成値は一切使わない</b> — 測れないときは「未計測(-1)」を返す。</item>
/// <item><b>100Hz IMU の供給</b> (§5.2)。CoreMotion のコールバックでリングバッファへ
///   貯めたサンプルを毎フレームまとめて引き取る。Unityの <c>Input.gyro</c> を
///   Update で読む方式では、CoreMotion を100Hzにしても実際に取れるのは
///   フレームレート(60Hz)ぶんだけになる。</item>
/// </list>
///
/// <para>iOS実機以外(エディタ・E2E)ではネイティブが存在しないため、すべて
/// 「未計測 / サンプル0件」を返す。呼び出し側はこの状態で正しく動くこと。</para>
/// </summary>
public class SensorTimingBridge : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════════════
    // ネイティブ境界 — 呼び出し側が #if を書かなくて済むよう、ここで吸収する
    // ════════════════════════════════════════════════════════════════════════
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void   ARV_StartSensorTiming();
    [DllImport("__Internal")] private static extern void   ARV_StopSensorTiming();
    [DllImport("__Internal")] private static extern double ARV_CurrentMediaTime();
    [DllImport("__Internal")] private static extern double ARV_DisplayTargetTimestamp();
    [DllImport("__Internal")] private static extern int    ARV_IsImuStreaming();
    [DllImport("__Internal")] private static extern int    ARV_DrainImuSamples(
        double[] outTimestamps, float[] outXyz, int maxSamples);

    private static void   NativeStart()            => ARV_StartSensorTiming();
    private static void   NativeStop()             => ARV_StopSensorTiming();
    private static double NativeMediaTime()        => ARV_CurrentMediaTime();
    private static double NativeTargetTimestamp()  => ARV_DisplayTargetTimestamp();
    private static bool   NativeImuStreaming()     => ARV_IsImuStreaming() != 0;
    private static int    NativeDrainImu(double[] ts, float[] xyz, int max)
        => ARV_DrainImuSamples(ts, xyz, max);

    /// <summary>ネイティブ計測が使える環境か(iOS実機のみ true)。</summary>
    public const bool NativeAvailable = true;
#else
    private static void   NativeStart()           { }
    private static void   NativeStop()            { }
    private static double NativeMediaTime()       => 0.0;
    private static double NativeTargetTimestamp() => 0.0;
    private static bool   NativeImuStreaming()    => false;
    private static int    NativeDrainImu(double[] ts, float[] xyz, int max) => 0;

    /// <summary>ネイティブ計測が使える環境か(iOS実機のみ true)。</summary>
    public const bool NativeAvailable = false;
#endif

    /// <summary>1フレームで引き取るIMUサンプルの上限(100Hz×フレーム落ち耐性)。</summary>
    public const int MaxDrainPerFrame = 64;

    /// <summary>実測が途切れたとみなすまでの猶予(秒)。</summary>
    private const double MeasurementStaleSeconds = 1.0;

    private ARCameraManager _cameraManager;
    private bool _subscribed;
    private float _nextCameraLookupTime;

    private double _lastSensorSeconds;     // ARKitフレームのタイムスタンプ(秒)
    private double _latestLatencyMs = MotionToPhotonMath.Unmeasured;
    private float  _lastValidMeasurementTime = -999f;

    private bool _measuring;

    /// <summary>走行1本ぶんのM2P集計 (§11.2 の評価をアプリ内でも言えるように)。</summary>
    public MotionToPhotonStats Stats { get; } = new MotionToPhotonStats();

    /// <summary>計測を開始しているか。</summary>
    public bool IsMeasuring => _measuring;

    /// <summary>100Hz IMU がネイティブから流れているか。</summary>
    public bool IsImuStreaming => _measuring && NativeImuStreaming();

    /// <summary>
    /// 直近のM2P実測(ms)。<b>実測できていなければ false</b> を返し、
    /// <paramref name="latencyMs"/> には <see cref="MotionToPhotonMath.Unmeasured"/> が入る。
    /// </summary>
    public bool TryGetLatencyMs(out double latencyMs)
    {
        latencyMs = MotionToPhotonMath.Unmeasured;
        if (_latestLatencyMs < 0.0) return false;
        if (Time.realtimeSinceStartup - _lastValidMeasurementTime > MeasurementStaleSeconds)
            return false; // 途切れた値を最新として配らない

        latencyMs = _latestLatencyMs;
        return true;
    }

    /// <summary>ネイティブと同じ時間軸の現在時刻(秒)。使えない環境では0。</summary>
    public double CurrentMediaTime => NativeMediaTime();

    /// <summary>計測(表示リンク + 100Hz IMU)を開始する。走行開始時に呼ぶ。</summary>
    public void StartMeasuring()
    {
        if (_measuring) return;
        _measuring = true;
        Stats.Reset();
        _latestLatencyMs = MotionToPhotonMath.Unmeasured;
        NativeStart();
        EnsureCameraSubscription();

        Debug.Log(NativeAvailable
            ? "[M2P] ネイティブ計測を開始 — latency_m2p は実測値で埋まります"
            : "[M2P] ネイティブ計測は実機のみ — このビルドでは latency_m2p は -1(未計測)のままです");
    }

    /// <summary>計測を停止し、集計を1行で残す。</summary>
    public void StopMeasuring()
    {
        if (!_measuring) return;
        _measuring = false;
        NativeStop();
        _latestLatencyMs = MotionToPhotonMath.Unmeasured;

        Debug.Log($"[M2P] {Stats.Summarize()}");
    }

    /// <summary>
    /// 貯まった100Hz IMUサンプルを古い順に引き取る。戻り値 = 取得件数(0なら未供給)。
    /// </summary>
    public int DrainImuSamples(double[] timestampsSeconds, float[] xyz)
    {
        if (!_measuring || timestampsSeconds == null || xyz == null) return 0;

        int max = Mathf.Min(timestampsSeconds.Length, xyz.Length / 3);
        if (max <= 0) return 0;

        return NativeDrainImu(timestampsSeconds, xyz, max);
    }

    private void OnDisable()
    {
        UnsubscribeCamera();
        if (_measuring) StopMeasuring();
    }

    private void Update()
    {
        if (!_measuring) return;
        EnsureCameraSubscription();
    }

    private void LateUpdate()
    {
        if (!_measuring) return;

        // このフレームの提示予定時刻 − このフレームが使ったARKitフレームのセンサー時刻。
        // 片方でも欠けていれば「未計測」にする(合成値で埋めない)
        double present = NativeTargetTimestamp();
        if (MotionToPhotonMath.TryComputeLatencyMs(_lastSensorSeconds, present, out double ms))
        {
            _latestLatencyMs = ms;
            _lastValidMeasurementTime = Time.realtimeSinceStartup;
            Stats.Add(ms);
        }
    }

    // ── ARKitフレームのタイムスタンプ(センサー時刻)の購読 ──────────────────
    private void EnsureCameraSubscription()
    {
        if (_subscribed) return;
        if (Time.realtimeSinceStartup < _nextCameraLookupTime) return;
        _nextCameraLookupTime = Time.realtimeSinceStartup + 1.0f; // 毎フレーム探さない

        if (_cameraManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            _cameraManager = Object.FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
#else
            _cameraManager = Object.FindObjectOfType<ARCameraManager>(true);
#endif
        }

        if (_cameraManager == null) return;

        _cameraManager.frameReceived += OnCameraFrameReceived;
        _subscribed = true;
    }

    private void UnsubscribeCamera()
    {
        if (!_subscribed || _cameraManager == null) return;
        _cameraManager.frameReceived -= OnCameraFrameReceived;
        _subscribed = false;
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        // timestampNs は ARFrame.timestamp (CACurrentMediaTime軸) のナノ秒表現
        if (args.timestampNs.HasValue)
            _lastSensorSeconds = args.timestampNs.Value * 1e-9;
    }
}
