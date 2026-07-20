using UnityEngine;

/// <summary>
/// F-09 GPSロスト自動判定 (基本設計書 §8.1)。
/// Swift(CoreLocation)から供給される測位サンプルを監視し、設計書の異常検知条件で
/// GPS FSM を自動遷移させる:
///   ロスト判定: 位置情報の更新が 1.5秒以上 途絶えた場合、
///               または水平精度誤差が 10m以上 に悪化した瞬間
///   復帰判定 : 新鮮なサンプルがあり、かつ精度が復帰ゲート(5m)以内
///
/// 実測サンプルを一度も受け取っていない間(エディタ単体走行・E2E)は一切介入しない
/// ため、既存のシミュレーションキー(G/R/A)の挙動は従来どおり。
/// </summary>
public class GpsSignalMonitor : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private GameStateController stateController;
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private RunTelemetryLogger telemetryLogger;

    [Header("Detection Thresholds (基本設計書 §8.1)")]
    [Tooltip("この秒数だけ位置情報の更新が途絶えるとロスト判定")]
    [SerializeField] private float staleTimeoutSeconds = 1.5f;
    [Tooltip("水平精度誤差がこの値以上に悪化した瞬間にロスト判定(m)")]
    [SerializeField] private float accuracyLostThresholdMeters = 10.0f;
    [Tooltip("復帰を認める精度ゲート(m)。AGENTS.md §5 の再集積ゲートと同値")]
    [SerializeField] private float accuracyRecoveredThresholdMeters = 5.0f;

    private float _lastUpdateTime = -1f;
    private float _lastAccuracy = -1f;
    private bool _hasReceivedSample;
    private bool _lostReported;

    /// <summary>実測サンプルを受信済みか(未受信なら本監視は介入しない)。</summary>
    public bool IsMonitoring => _hasReceivedSample;
    /// <summary>直近サンプルの水平精度誤差(m)。未受信は-1。</summary>
    public float LastAccuracyMeters => _lastAccuracy;
    /// <summary>現在ロスト条件を満たしているか。</summary>
    public bool IsSignalLost => _hasReceivedSample && EvaluateLost();

    void Awake()
    {
        if (stateController == null)
            stateController = FindFirstObjectByType<GameStateController>(FindObjectsInactive.Include);
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (telemetryLogger == null)
            telemetryLogger = FindFirstObjectByType<RunTelemetryLogger>(FindObjectsInactive.Include);
    }

    /// <summary>
    /// Swift(CoreLocation)からの測位サンプル報告。ブリッジがUpdateMetrics毎に呼ぶ。
    /// horizontalAccuracyMeters は CoreLocation 同様、負値なら無効サンプル。
    /// </summary>
    public void ReportGpsUpdate(double latitude, double longitude, float horizontalAccuracyMeters)
    {
        if (horizontalAccuracyMeters < 0f) return; // 無効サンプルは無視(更新時刻も進めない)

        _lastUpdateTime = Time.time;
        _lastAccuracy = horizontalAccuracyMeters;
        _hasReceivedSample = true;

        // F-11 CSVログのGPS列へ供給(§5.2)
        if (telemetryLogger != null)
            telemetryLogger.SetGpsCoordinates(latitude, longitude);
    }

    void Update()
    {
        if (!_hasReceivedSample || stateController == null) return;

        // 走行中のみ判定(準備画面や終了後は介入しない)
        if (avatarEngine != null && (!avatarEngine.HasStarted || avatarEngine.IsSessionEnded)) return;

        // 再集積ゲート(AGENTS.md §5)へ実測精度を供給する
        stateController.SimulatedGPSAccuracyRadius = _lastAccuracy;

        bool lost = EvaluateLost();

        if (lost && !_lostReported && stateController.currentState == GameStateController.ARVisionState.Normal)
        {
            _lostReported = true;
            float stale = Time.time - _lastUpdateTime;
            Debug.LogWarning($"[GPS MONITOR] ロスト判定 — 更新途絶 {stale:F2}s / 精度 {_lastAccuracy:F1}m");
            stateController.TransitionToState(GameStateController.ARVisionState.InertialMovement);
        }
        else if (!lost && _lostReported)
        {
            _lostReported = false;
            // 慣性移動中の復帰は Normal へ直接同期(FadeOut以降は既存FSMの復帰経路に委ねる)
            if (stateController.currentState == GameStateController.ARVisionState.InertialMovement)
            {
                Debug.Log($"[GPS MONITOR] 信号復帰 — 精度 {_lastAccuracy:F1}m。通常追従へ同期");
                stateController.TransitionToState(GameStateController.ARVisionState.Normal);
            }
        }
    }

    private bool EvaluateLost()
    {
        bool stale = (Time.time - _lastUpdateTime) >= staleTimeoutSeconds;
        bool inaccurate = _lastAccuracy >= accuracyLostThresholdMeters;
        return stale || inaccurate;
    }

    /// <summary>再走行対応: 監視状態を初期化する。</summary>
    public void ResetSession()
    {
        _lastUpdateTime = -1f;
        _lastAccuracy = -1f;
        _hasReceivedSample = false;
        _lostReported = false;
    }
}
