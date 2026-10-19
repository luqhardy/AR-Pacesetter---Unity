using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// サイレントルート復帰(企画書 4.3): ルートを外れたら、アバターが走者の後ろへ回ってルートの方を向いて案内する。
///
/// <para>ルートは2通り:</para>
/// <list type="bullet">
/// <item><b>選んだルート(GPS)</b> — Swift の S8 で「ルートを外れたら案内する」を ON にし、過去の走行の経路を選んだとき
///   StartSession の <c>routeLatLon</c> で届く(<see cref="SetGpsRoute"/>)。逸脱は走者の最新の測位と経路を
///   緯度経度のまま比べ(<see cref="RouteDeviation"/>)、トラックの GPS 誤差では反応しないしきい値・持続時間で決める
///   (<see cref="RouteDeviationGate"/>)。案内の向きだけを AR の世界へ回す(RunnerTrackingState)。</item>
/// <item><b>インスペクタの Transform</b> — エディタの検証用。無ければ D キー / E2E の <see cref="SimulateDeviation"/>。</item>
/// </list>
/// </summary>
public class SilentRouteRecoverer : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private Transform userCamera;
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private SafetyEventLogger safetyLogger;

    [Header("Route Settings")]
    [SerializeField] private float deviationThresholdMeters = 5.0f;
    [SerializeField] private float recoveryTrailingDistance = 2.5f;

    [Header("Route Path (assign in Inspector)")]
    [Tooltip("Ordered array of transforms defining the real route. Leave empty to use D-key simulation.")]
    [SerializeField] private Transform[] routeWaypoints;

    private bool _isRecoveringSilently = false;
    private int _nearestSegmentIndex = 0;

    /// <summary>E2E/エディタ検証用: ルート逸脱の強制シミュレート(Dキーと同じ)。</summary>
    public bool SimulateDeviation { get; set; }

    /// <summary>現在サイレント復帰(逸脱誘導)中かどうか。</summary>
    public bool IsRecovering => _isRecoveringSilently;

    // ── 選んだルート(GPS)───────────────────────────────────────────────
    private List<(double lat, double lon)> _gpsRoute = new List<(double lat, double lon)>();
    private readonly RouteDeviationGate _gpsGate = new RouteDeviationGate();
    private RunnerTrackingState _tracking;
    private float _lastJudgedFixTime = -1f;
    private Vector3 _gpsGuideWorldDirection;
    private bool _hasGpsGuideDirection;

    /// <summary>選んだルート(GPS)で判定しているか</summary>
    public bool HasGpsRoute => _gpsRoute.Count >= RouteDeviation.MinimumPoints;

    /// <summary>選んだルートの点の数(診断・E2E 用)</summary>
    public int GpsRoutePointCount => _gpsRoute.Count;

    /// <summary>選んだルートの判定の状態(診断・E2E 用)</summary>
    public RouteDeviationGate GpsGate => _gpsGate;

    /// <summary>
    /// 選んだルートを設定する(StartSession の routeLatLon — [緯度0, 経度0, 緯度1, 経度1, ...])。
    /// 空・null なら選んだルートは無し(従来どおり)。走行ごとに呼ばれ、前の走行の判定は持ち越さない
    /// </summary>
    public void SetGpsRoute(double[] flatLatLon)
    {
        _gpsRoute = RouteDeviation.ParseFlatLatLon(flatLatLon);
        _gpsGate.Reset();
        _lastJudgedFixTime = -1f;
        _hasGpsGuideDirection = false;
        if (_isRecoveringSilently) CeaseSilentRouteRecovery();
        Debug.Log(HasGpsRoute
            ? $"[SILENT RECOVERY] 選んだルート {_gpsRoute.Count} 点で逸脱を判定します(GPS)"
            : "[SILENT RECOVERY] 選んだルートはありません");
    }

    void Start()
    {
        // Bootstrap経由でアバターへ自動装着された場合の参照解決
        if (avatarEngine == null)
            avatarEngine = GetComponent<AvatarEngine>();
        if (userCamera == null && Camera.main != null)
            userCamera = Camera.main.transform;
    }

    void Update()
    {
        if (userCamera == null || avatarEngine == null) return;

        if (HasGpsRoute)
        {
            UpdateGpsRoute();
            return;
        }

        float crossTrackError = CalculateRouteDeviation(userCamera.position);

        if (crossTrackError >= deviationThresholdMeters && !_isRecoveringSilently)
            InitiateSilentRouteRecovery();
        else if (crossTrackError < deviationThresholdMeters && _isRecoveringSilently)
            CeaseSilentRouteRecovery();

        if (_isRecoveringSilently)
            ExecuteTrailingAndGuidingLogic();
    }

    /// <summary>選んだルート(GPS)の判定。新しい測位が来たときだけ数え、走行中(START 後〜終了前)だけ案内する</summary>
    private void UpdateGpsRoute()
    {
        if (!avatarEngine.IsRunMotionActive) return;
        if (_tracking == null) _tracking = FindFirstObjectByType<RunnerTrackingState>(FindObjectsInactive.Include);
        if (_tracking == null) return;

        float fixTime = _tracking.LastAcceptedGpsFixTime;
        if (fixTime > _lastJudgedFixTime)
        {
            double sinceLast = _lastJudgedFixTime < 0f ? 0.0 : fixTime - _lastJudgedFixTime;
            _lastJudgedFixTime = fixTime;
            if (RouteDeviation.TryCrossTrack(_gpsRoute, _tracking.Latitude, _tracking.Longitude,
                                             out double distance, out double east, out double north, out _))
            {
                _gpsGate.Update(distance, _tracking.GpsAccuracyMeters, sinceLast);
                _hasGpsGuideDirection = _tracking.TryGeographicToWorldDirection(east, north, out _gpsGuideWorldDirection);
            }
        }

        if (_gpsGate.IsDeviated && !_isRecoveringSilently)
            InitiateSilentRouteRecovery();
        else if (!_gpsGate.IsDeviated && _isRecoveringSilently)
            CeaseSilentRouteRecovery();

        if (_isRecoveringSilently)
            ExecuteTrailingAndGuidingLogic();
    }

    private float CalculateRouteDeviation(Vector3 userPos)
    {
        // If waypoints are configured, compute real cross-track error
        if (routeWaypoints != null && routeWaypoints.Length >= 2)
            return ComputeCrossTrackError(userPos);

        // Fallback: D-key / E2E simulation for editor testing
        if (SimulateDeviation || Input.GetKey(KeyCode.D))
            return 6.0f;

        return 0.0f;
    }

    // Point-to-line-segment perpendicular distance across all route segments
    private float ComputeCrossTrackError(Vector3 userPos)
    {
        float minDistance = float.MaxValue;
        int bestIndex = 0;

        for (int i = 0; i < routeWaypoints.Length - 1; i++)
        {
            if (routeWaypoints[i] == null || routeWaypoints[i + 1] == null)
                continue;

            Vector3 a = routeWaypoints[i].position;
            Vector3 b = routeWaypoints[i + 1].position;

            float dist = PointToSegmentDistance(userPos, a, b);
            if (dist < minDistance)
            {
                minDistance = dist;
                bestIndex = i;
            }
        }

        _nearestSegmentIndex = bestIndex;
        return minDistance;
    }

    // Standard point-to-line-segment distance (clamped projection)
    private float PointToSegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        Vector3 ap = p - a;

        float sqrLenAB = ab.sqrMagnitude;
        if (sqrLenAB < 0.0001f) return Vector3.Distance(p, a);

        float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / sqrLenAB);
        Vector3 closest = a + t * ab;
        return Vector3.Distance(p, closest);
    }

    private void InitiateSilentRouteRecovery()
    {
        _isRecoveringSilently = true;
        Debug.Log("[SILENT RECOVERY] Runner deviated from route. Overriding pacer logic.");
        if (avatarEngine != null)
        {
            avatarEngine.IsOverriddenByRecovery = true;
        }

        // 企画書 §4: 逸脱地点をルートマップ上にログ記録
        if (safetyLogger == null)
            safetyLogger = FindFirstObjectByType<SafetyEventLogger>(FindObjectsInactive.Include);
        if (safetyLogger != null && userCamera != null)
            safetyLogger.LogRouteDeviation(userCamera.position);
    }

    private void CeaseSilentRouteRecovery()
    {
        _isRecoveringSilently = false;
        Debug.Log("[SILENT RECOVERY] Runner returned to route. Restoring standard pacer logic.");
        
        // Sync AvatarEngine's internal state before resuming movement to prevent jumps.
        // 型安全な公開APIで再同期する(旧実装はprivateフィールドをリフレクションで
        // 書き換えており、フィールド名変更時に無言で壊れる負債だった)
        if (avatarEngine != null)
        {
            avatarEngine.IsOverriddenByRecovery = false;
            avatarEngine.ResyncPacingAnchor(transform.position, userCamera.position);
        }
    }

    private void ExecuteTrailingAndGuidingLogic()
    {
        // Determine where to guide the runner
        Vector3 guideTarget = GetNearestRouteTarget();
        Vector3 dirToRoute = (guideTarget - userCamera.position).normalized;
        dirToRoute.y = 0;

        // Position avatar behind runner, facing true route
        Vector3 trailPos = userCamera.position - (userCamera.forward * recoveryTrailingDistance);
        
        // Fix: Use current Y to prevent floating to eye level
        trailPos.y = transform.position.y;
        
        // k=3.0 は333msで、k=5.0(回転)は200msで補間係数が1に飽和する。走行中の最大フレームは
        // 実測328〜333msなので、ここは現実的に飽和し「ユーザー背後へワープ」になっていた
        transform.position = Vector3.Lerp(transform.position, trailPos,
                                          FrameSmoothing.Factor(Time.deltaTime, 3.0f));

        if (dirToRoute != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dirToRoute);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot,
                                                  FrameSmoothing.Factor(Time.deltaTime, 5.0f));
        }
    }

    private Vector3 GetNearestRouteTarget()
    {
        // 選んだルート(GPS): 経路上の最寄り点の方へ(GPS と AR の向きの対応が決まっているときだけ)
        if (HasGpsRoute && _hasGpsGuideDirection && userCamera != null)
            return userCamera.position + _gpsGuideWorldDirection * 20.0f;

        if (routeWaypoints != null && routeWaypoints.Length >= 2)
        {
            int nextIdx = Mathf.Min(_nearestSegmentIndex + 1, routeWaypoints.Length - 1);
            if (routeWaypoints[nextIdx] != null)
                return routeWaypoints[nextIdx].position;
        }

        // Fallback mock target
        if (userCamera != null)
            return userCamera.position + (userCamera.forward * 20.0f);

        return transform.position;
    }
}
