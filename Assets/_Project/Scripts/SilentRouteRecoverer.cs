using UnityEngine;

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

    void Update()
    {
        if (userCamera == null || avatarEngine == null) return;

        float crossTrackError = CalculateRouteDeviation(userCamera.position);

        if (crossTrackError >= deviationThresholdMeters && !_isRecoveringSilently)
            InitiateSilentRouteRecovery();
        else if (crossTrackError < deviationThresholdMeters && _isRecoveringSilently)
            CeaseSilentRouteRecovery();

        if (_isRecoveringSilently)
            ExecuteTrailingAndGuidingLogic();
    }

    private float CalculateRouteDeviation(Vector3 userPos)
    {
        // If waypoints are configured, compute real cross-track error
        if (routeWaypoints != null && routeWaypoints.Length >= 2)
            return ComputeCrossTrackError(userPos);

        // Fallback: D-key simulation for editor testing
        if (Input.GetKey(KeyCode.D))
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
        
        // Sync AvatarEngine's internal state before resuming movement to prevent jumps
        if (avatarEngine != null)
        {
            avatarEngine.IsOverriddenByRecovery = false;
            
            // Ensure internal target tracking aligns with current transform
            var field = typeof(AvatarEngine).GetField("_targetPacingPosition", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(avatarEngine, transform.position);
            
            var fieldUser = typeof(AvatarEngine).GetField("_lastFrameUserPosition", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fieldUser?.SetValue(avatarEngine, userCamera.position);
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
        
        transform.position = Vector3.Lerp(transform.position, trailPos, Time.deltaTime * 3.0f);

        if (dirToRoute != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dirToRoute);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 5.0f);
        }
    }

    private Vector3 GetNearestRouteTarget()
    {
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
