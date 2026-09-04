using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 画面には描画しない「実ランナー」の単一状態。
///
/// - ARKit/XR Camera: ユーザーのローカル位置・Visual-Inertial(カメラ+IMU)移動
/// - Swift/CoreLocation: 累積距離・速度・緯度経度・GPS精度
/// - Apple Watch/HealthKit: 心拍
///
/// GPS方位をARワールドへ較正し、直近1.5秒のAR移動と融合して安定した進行方向を
/// AvatarEngineへ供給する。ユーザーの3Dモデルは作らず、SceneビューのGizmoだけで
/// デバッグできるため、実機のPOV表示を遮らない。
/// </summary>
public sealed class RunnerTrackingState : MonoBehaviour
{
    public enum HeadingSource
    {
        InitialView,
        LocalArMotion,
        Gps,
        Fused
    }

    [Header("References (auto-found if empty)")]
    [SerializeField] private Transform userCamera;
    [SerializeField] private AvatarEngine avatarEngine;

    [Header("Fusion")]
    [Tooltip("要件§4.1: 進行方向へ使う移動履歴の長さ")]
    [SerializeField] private float headingWindowSeconds = 1.5f;
    [Range(0f, 1f)]
    [Tooltip("GPSとAR移動が両方ある時のGPS比率")]
    [SerializeField] private float gpsHeadingWeight = 0.65f;
    [SerializeField] private float gpsFreshSeconds = 5f;
    [SerializeField] private float maximumGpsAccuracyMeters = 20f;
    [SerializeField] private float minimumGpsSegmentMeters = 0.75f;
    [SerializeField] private float maximumGpsSegmentMeters = 100f;
    [SerializeField] private float minimumArAlignmentMeters = 0.15f;

    [Header("Debug (Scene view only)")]
    [SerializeField] private bool showUserGizmo = true;
    [SerializeField] private Color userGizmoColor = new Color(0.05f, 0.85f, 1f, 1f);

    private struct MotionSample
    {
        public Vector3 delta;
        public float timestamp;
        public MotionSample(Vector3 delta, float timestamp)
        {
            this.delta = delta;
            this.timestamp = timestamp;
        }
    }

    private struct HeadingSample
    {
        public Vector3 direction;
        public float timestamp;
        public HeadingSample(Vector3 direction, float timestamp)
        {
            this.direction = direction;
            this.timestamp = timestamp;
        }
    }

    private readonly Queue<MotionSample> _arMotion = new Queue<MotionSample>();
    private readonly Queue<HeadingSample> _headingHistory = new Queue<HeadingSample>();

    private Vector3 _lastCameraPosition;
    private Vector3 _cameraAtGpsReference;
    private Vector3 _currentHeading = Vector3.forward;
    private Vector3 _gpsHeadingWorld = Vector3.forward;
    private float _smoothedLocalSpeedMps;
    private float _worldFromGpsYawDegrees;
    private float _lastExternalMetricsTime = -999f;
    private float _lastSpeedSampleTime = -999f;
    private float _lastGpsFixTime = -999f;
    private double _gpsReferenceLatitude;
    private double _gpsReferenceLongitude;
    private bool _hasCameraPosition;
    private bool _hasGpsReference;
    private bool _hasGpsHeading;
    private bool _hasWorldAlignment;
    private bool _hasMovementHeading;
    private bool _sessionActive;
    private bool _hasExternalSpeedSample;

    public Vector3 UserWorldPosition => userCamera != null ? userCamera.position : Vector3.zero;
    public Quaternion UserWorldRotation => userCamera != null ? userCamera.rotation : Quaternion.identity;
    public Vector3 CurrentHeading => _currentHeading;
    public bool HasMovementHeading => _hasMovementHeading;
    public bool IsSessionActive => _sessionActive;
    public HeadingSource CurrentHeadingSource { get; private set; } = HeadingSource.InitialView;
    public float TargetPaceKmH { get; private set; }
    public double TargetDistanceMeters { get; private set; }
    public double DistanceMeters { get; private set; }
    public float ExternalSpeedKmH { get; private set; }
    public int HeartRateBpm { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public float GpsAccuracyMeters { get; private set; } = -1f;
    public bool HasFreshExternalMetrics => Time.time - _lastExternalMetricsTime <= gpsFreshSeconds;
    public bool HasValidSpeedMeasurement => _hasExternalSpeedSample
        && Time.time - _lastSpeedSampleTime <= gpsFreshSeconds;
    public bool HasFreshGpsHeading => _hasGpsHeading && Time.time - _lastGpsFixTime <= gpsFreshSeconds;
    public float CurrentSpeedKmH => HasValidSpeedMeasurement
        ? ExternalSpeedKmH
        : _smoothedLocalSpeedMps * 3.6f;
    public float ActualPaceMinutesPerKm => CurrentSpeedKmH > 0.1f
        ? 60f / CurrentSpeedKmH
        : 0f;

    void Awake()
    {
        ResolveReferences();
        ResetTrackingOrigin();
    }

    void Update()
    {
        ResolveReferences();
        if (userCamera == null)
            return;

        UpdateArMotion();

        // Unity単体スタートも取りこぼさない。Swift開始時はBeginSessionが先に呼ばれる。
        if (!_sessionActive && avatarEngine != null && avatarEngine.HasStarted
            && !avatarEngine.IsSessionEnded)
        {
            BeginSession(avatarEngine.TargetPaceMinutesPerKm > 0f
                    ? 60f / avatarEngine.TargetPaceMinutesPerKm : 0f,
                0.0);
        }

        if (_sessionActive)
            UpdateFusedHeading();

        if (_sessionActive && avatarEngine != null && avatarEngine.IsSessionEnded)
            EndSession();
    }

    public void BeginSession(float targetPaceKmH, double targetDistanceKm)
    {
        ResolveReferences();
        _sessionActive = true;
        TargetPaceKmH = Mathf.Max(0f, targetPaceKmH);
        TargetDistanceMeters = System.Math.Max(0.0, targetDistanceKm * 1000.0);
        DistanceMeters = 0.0;
        ExternalSpeedKmH = 0f;
        HeartRateBpm = 0;
        Latitude = 0.0;
        Longitude = 0.0;
        GpsAccuracyMeters = -1f;
        _lastExternalMetricsTime = -999f;
        _lastSpeedSampleTime = -999f;
        _lastGpsFixTime = -999f;
        _hasGpsReference = false;
        _hasGpsHeading = false;
        _hasWorldAlignment = false;
        _hasMovementHeading = false;
        _hasExternalSpeedSample = false;
        _smoothedLocalSpeedMps = 0f;
        _worldFromGpsYawDegrees = 0f;
        _arMotion.Clear();
        _headingHistory.Clear();
        ResetTrackingOrigin();
        _currentHeading = HorizontalCameraForward();
        CurrentHeadingSource = HeadingSource.InitialView;
        Debug.Log("[RUNNER TRACKING] Session started — invisible POV runner state active.");
    }

    public void EndSession()
    {
        _sessionActive = false;
    }

    /// <summary>
    /// Clears speed/distance gathered while GPS was warming up during the visual
    /// countdown, while preserving its useful heading calibration.
    /// </summary>
    public void MarkRunMotionStarted()
    {
        DistanceMeters = 0.0;
        ExternalSpeedKmH = 0f;
        _hasExternalSpeedSample = false;
        _lastExternalMetricsTime = -999f;
        _lastSpeedSampleTime = -999f;
        _smoothedLocalSpeedMps = 0f;
        if (userCamera != null)
        {
            _lastCameraPosition = userCamera.position;
            _hasCameraPosition = true;
        }
    }

    public void ResetSession()
    {
        _sessionActive = false;
        TargetPaceKmH = 0f;
        TargetDistanceMeters = 0.0;
        DistanceMeters = 0.0;
        ExternalSpeedKmH = 0f;
        HeartRateBpm = 0;
        Latitude = 0.0;
        Longitude = 0.0;
        GpsAccuracyMeters = -1f;
        _lastExternalMetricsTime = -999f;
        _lastSpeedSampleTime = -999f;
        _lastGpsFixTime = -999f;
        _hasGpsReference = false;
        _hasGpsHeading = false;
        _hasWorldAlignment = false;
        _hasMovementHeading = false;
        _hasExternalSpeedSample = false;
        _smoothedLocalSpeedMps = 0f;
        _arMotion.Clear();
        _headingHistory.Clear();
        ResetTrackingOrigin();
        CurrentHeadingSource = HeadingSource.InitialView;
    }

    /// <summary>Swift UpdateMetricsの生センサースナップショットを一か所へ集約する。</summary>
    public void ReportMetrics(float paceKmH, int heartRate, double distanceKm,
                              double latitude, double longitude, float gpsAccuracy,
                              bool locationSampleFresh, bool speedSampleValid,
                              bool includesNewGpsFix)
    {
        if (heartRate > 0)
            HeartRateBpm = heartRate;

        // タイマーによるキャッシュ再送では鮮度を更新しない。新しいCoreLocation fix
        // だけが速度・距離の5秒フォールバック時計を進める。
        if (locationSampleFresh)
        {
            if (distanceKm >= 0.0)
                DistanceMeters = distanceKm * 1000.0;
            _lastExternalMetricsTime = Time.time;
        }

        // CoreLocation speed=0 is a valid stationary measurement; unavailable speed is
        // represented separately by speedSampleValid=false and must not refresh freshness.
        if (locationSampleFresh && speedSampleValid)
        {
            ExternalSpeedKmH = Mathf.Max(0f, paceKmH);
            _hasExternalSpeedSample = true;
            _lastSpeedSampleTime = Time.time;
        }

        if (includesNewGpsFix)
            ReportGpsFix(latitude, longitude, gpsAccuracy);
    }

    public void ReportGpsFix(double latitude, double longitude, float accuracyMeters)
    {
        Latitude = latitude;
        Longitude = longitude;
        GpsAccuracyMeters = accuracyMeters;

        if (!RunnerTrackingMath.IsValidCoordinate(latitude, longitude)
            || accuracyMeters < 0f || accuracyMeters > maximumGpsAccuracyMeters)
            return;

        _lastGpsFixTime = Time.time;
        Vector3 cameraPosition = userCamera != null ? userCamera.position : Vector3.zero;

        if (!_hasGpsReference)
        {
            SetGpsReference(latitude, longitude, cameraPosition);
            return;
        }

        if (!RunnerTrackingMath.TryLocalOffsetMeters(
                _gpsReferenceLatitude, _gpsReferenceLongitude,
                latitude, longitude, out double eastMeters, out double northMeters))
            return;

        double segmentMeters = RunnerTrackingMath.DistanceMeters(eastMeters, northMeters);
        if (segmentMeters < minimumGpsSegmentMeters)
            return;

        if (segmentMeters > maximumGpsSegmentMeters)
        {
            // CoreLocationのテレポートを方位へ混ぜず、次サンプルから再開する。
            SetGpsReference(latitude, longitude, cameraPosition);
            return;
        }

        Vector3 geographicDirection = new Vector3((float)eastMeters, 0f, (float)northMeters).normalized;
        Vector3 arDelta = cameraPosition - _cameraAtGpsReference;
        arDelta.y = 0f;

        float geographicYaw = Mathf.Atan2(geographicDirection.x, geographicDirection.z)
                            * Mathf.Rad2Deg;
        if (!_hasWorldAlignment)
        {
            Vector3 alignmentDirection = arDelta.magnitude >= minimumArAlignmentMeters
                ? arDelta.normalized
                : HorizontalCameraForward();
            float arYaw = Mathf.Atan2(alignmentDirection.x, alignmentDirection.z) * Mathf.Rad2Deg;
            _worldFromGpsYawDegrees = Mathf.DeltaAngle(geographicYaw, arYaw);
            _hasWorldAlignment = true;
        }
        else if (arDelta.magnitude >= minimumArAlignmentMeters)
        {
            // 長距離でARワールドが少しドリフトしてもGPSとの対応を緩やかに再較正。
            float arYaw = Mathf.Atan2(arDelta.x, arDelta.z) * Mathf.Rad2Deg;
            float observedOffset = Mathf.DeltaAngle(geographicYaw, arYaw);
            _worldFromGpsYawDegrees = Mathf.LerpAngle(
                _worldFromGpsYawDegrees, observedOffset, 0.12f);
        }

        _gpsHeadingWorld = Quaternion.Euler(0f, _worldFromGpsYawDegrees, 0f)
                         * geographicDirection;
        _gpsHeadingWorld.y = 0f;
        _gpsHeadingWorld.Normalize();
        _hasGpsHeading = true;
        SetGpsReference(latitude, longitude, cameraPosition);
    }

    private void ResolveReferences()
    {
        if (userCamera == null && Camera.main != null)
            userCamera = Camera.main.transform;
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
    }

    private void ResetTrackingOrigin()
    {
        if (userCamera == null)
            return;
        _lastCameraPosition = userCamera.position;
        _cameraAtGpsReference = userCamera.position;
        _hasCameraPosition = true;
        _currentHeading = HorizontalCameraForward();
    }

    private void UpdateArMotion()
    {
        Vector3 currentPosition = userCamera.position;
        if (!_hasCameraPosition)
        {
            _lastCameraPosition = currentPosition;
            _hasCameraPosition = true;
            return;
        }

        Vector3 delta = currentPosition - _lastCameraPosition;
        delta.y = 0f;
        _lastCameraPosition = currentPosition;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float instantSpeed = delta.magnitude / dt;
        // AR relocalization/teleportを人間の移動として扱わない。
        bool plausible = instantSpeed <= 15f;
        float acceptedSpeed = plausible ? instantSpeed : 0f;
        float speedK = 1f - Mathf.Exp(-dt / 2f);
        _smoothedLocalSpeedMps += (acceptedSpeed - _smoothedLocalSpeedMps) * speedK;

        if (_sessionActive && plausible && delta.sqrMagnitude > 0.00000025f)
            _arMotion.Enqueue(new MotionSample(delta, Time.time));

        PruneMotionHistory();
    }

    private void UpdateFusedHeading()
    {
        PruneMotionHistory();
        Vector3 integratedArMotion = Vector3.zero;
        foreach (MotionSample sample in _arMotion)
            integratedArMotion += sample.delta;

        bool hasArHeading = integratedArMotion.magnitude > 0.02f;
        bool hasGpsHeading = HasFreshGpsHeading;
        Vector3 candidate;

        if (hasGpsHeading && hasArHeading)
        {
            candidate = (_gpsHeadingWorld.normalized * gpsHeadingWeight
                       + integratedArMotion.normalized * (1f - gpsHeadingWeight)).normalized;
            CurrentHeadingSource = HeadingSource.Fused;
        }
        else if (hasGpsHeading)
        {
            candidate = _gpsHeadingWorld;
            CurrentHeadingSource = HeadingSource.Gps;
        }
        else if (hasArHeading)
        {
            candidate = integratedArMotion.normalized;
            CurrentHeadingSource = HeadingSource.LocalArMotion;
        }
        else
        {
            return;
        }

        _hasMovementHeading = true;
        _headingHistory.Enqueue(new HeadingSample(candidate, Time.time));
        while (_headingHistory.Count > 0
               && Time.time - _headingHistory.Peek().timestamp > headingWindowSeconds)
            _headingHistory.Dequeue();

        Vector3 weighted = Vector3.zero;
        float totalWeight = 0f;
        foreach (HeadingSample sample in _headingHistory)
        {
            float age = Time.time - sample.timestamp;
            float weight = Mathf.Exp(-age * 2.5f);
            weighted += sample.direction * weight;
            totalWeight += weight;
        }

        if (totalWeight > 0f && weighted.sqrMagnitude > 0.0001f)
        {
            Vector3 averaged = (weighted / totalWeight).normalized;
            float smoothing = 1f - Mathf.Exp(-Time.deltaTime / 0.25f);
            _currentHeading = Vector3.Slerp(_currentHeading, averaged, smoothing).normalized;
        }
    }

    private void PruneMotionHistory()
    {
        while (_arMotion.Count > 0
               && Time.time - _arMotion.Peek().timestamp > headingWindowSeconds)
            _arMotion.Dequeue();
    }

    private void SetGpsReference(double latitude, double longitude, Vector3 cameraPosition)
    {
        _gpsReferenceLatitude = latitude;
        _gpsReferenceLongitude = longitude;
        _cameraAtGpsReference = cameraPosition;
        _hasGpsReference = true;
    }

    private Vector3 HorizontalCameraForward()
    {
        Vector3 forward = userCamera != null ? userCamera.forward : Vector3.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    void OnDrawGizmosSelected()
    {
        if (!showUserGizmo)
            return;

        Transform cameraTransform = userCamera != null
            ? userCamera
            : (Camera.main != null ? Camera.main.transform : null);
        if (cameraTransform == null)
            return;

        Vector3 position = cameraTransform.position;
        Gizmos.color = userGizmoColor;
        Gizmos.DrawWireSphere(position, 0.18f);
        Gizmos.DrawLine(position, position + CurrentHeading * 1.5f);
    }
}
