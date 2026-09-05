using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// セーフティ・ロギング (企画書 §4 走行分析＆データマネジメント):
/// 急停止・速度超過・ルート逸脱の発生地点と時刻をセッション中に記録し、
/// 走行終了時にセッションレコードへ格納する。
/// </summary>
public class SafetyEventLogger : MonoBehaviour
{
    [Serializable]
    public class SafetyEvent
    {
        public string type;        // SuddenStop / Overspeed / RouteDeviation
        public float sessionTime;  // seconds since scene start
        public Vector3 position;   // world position on the route map
        public float speedMetersPerSecond;
    }

    [Header("References")]
    [SerializeField] private Transform userCamera;
    [SerializeField] private AvatarEngine avatarEngine;

    [Header("Detection Thresholds")]
    [Tooltip("Speed above this before the drop counts as a sudden stop.")]
    [SerializeField] private float suddenStopFromSpeed = 2.5f;
    [Tooltip("Speed below this after the drop counts as a sudden stop.")]
    [SerializeField] private float suddenStopToSpeed = 0.5f;
    [Tooltip("Window (s) within which the drop must occur.")]
    [SerializeField] private float suddenStopWindow = 0.7f;
    [Tooltip("Overspeed = max(this floor, target speed x1.6), sustained 1.5s.")]
    [SerializeField] private float overspeedFloor = 6.0f;

    private readonly List<SafetyEvent> _events = new List<SafetyEvent>();
    private readonly Queue<(float time, float speed)> _speedHistory = new Queue<(float, float)>();

    private Vector3 _lastPos;
    private bool _initialized = false;
    private float _overspeedSince = -1f;
    private float _suddenStopCooldownUntil = 0f;
    private float _overspeedCooldownUntil = 0f;
    private float _deviationCooldownUntil = 0f;

    public IReadOnlyList<SafetyEvent> Events => _events;

    /// <summary>再走行対応: 前セッションのイベントと検知状態を破棄する。</summary>
    public void ResetSession()
    {
        _events.Clear();
        _speedHistory.Clear();
        _overspeedSince = -1f;
        _suddenStopCooldownUntil = 0f;
        _overspeedCooldownUntil = 0f;
        _deviationCooldownUntil = 0f;
    }

    void Start()
    {
        if (userCamera == null && Camera.main != null)
            userCamera = Camera.main.transform;
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
    }

    void Update()
    {
        if (userCamera == null) return;

        if (!_initialized)
        {
            _lastPos = userCamera.position;
            _initialized = true;
            return;
        }

        Vector3 delta = userCamera.position - _lastPos;
        delta.y = 0f;
        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.001f);
        _lastPos = userCamera.position;

        // Only log while a run is in progress
        if (avatarEngine != null && !avatarEngine.IsRunMotionActive) return;

        TrackSpeedHistory(speed);
        DetectSuddenStop(speed);
        DetectOverspeed(speed);
    }

    private void TrackSpeedHistory(float speed)
    {
        _speedHistory.Enqueue((Time.time, speed));
        while (_speedHistory.Count > 0 && Time.time - _speedHistory.Peek().time > suddenStopWindow)
            _speedHistory.Dequeue();
    }

    private void DetectSuddenStop(float currentSpeed)
    {
        if (Time.time < _suddenStopCooldownUntil) return;
        if (currentSpeed >= suddenStopToSpeed) return;

        foreach (var (_, pastSpeed) in _speedHistory)
        {
            if (pastSpeed >= suddenStopFromSpeed)
            {
                Log("SuddenStop", currentSpeed);
                _suddenStopCooldownUntil = Time.time + 5f;
                return;
            }
        }
    }

    private void DetectOverspeed(float currentSpeed)
    {
        float targetSpeed = avatarEngine != null ? avatarEngine.GetBaseTargetSpeed() : 3.3f;
        float threshold = Mathf.Max(overspeedFloor, targetSpeed * 1.6f);

        if (currentSpeed > threshold)
        {
            if (_overspeedSince < 0f) _overspeedSince = Time.time;

            if (Time.time - _overspeedSince >= 1.5f && Time.time >= _overspeedCooldownUntil)
            {
                Log("Overspeed", currentSpeed);
                _overspeedCooldownUntil = Time.time + 10f;
            }
        }
        else
        {
            _overspeedSince = -1f;
        }
    }

    /// <summary>Called by SilentRouteRecoverer when the runner leaves the route.</summary>
    public void LogRouteDeviation(Vector3 position)
    {
        if (Time.time < _deviationCooldownUntil) return;
        _deviationCooldownUntil = Time.time + 5f;

        _events.Add(new SafetyEvent
        {
            type = "RouteDeviation",
            sessionTime = Time.time,
            position = position,
            speedMetersPerSecond = 0f
        });
        Debug.Log($"[SAFETY LOG] RouteDeviation at {position} (t={Time.time:F1}s)");
    }

    private void Log(string type, float speed)
    {
        _events.Add(new SafetyEvent
        {
            type = type,
            sessionTime = Time.time,
            position = userCamera.position,
            speedMetersPerSecond = speed
        });
        Debug.Log($"[SAFETY LOG] {type} at {userCamera.position} speed={speed:F1}m/s (t={Time.time:F1}s)");
    }
}
