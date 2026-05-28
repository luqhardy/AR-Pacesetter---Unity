using UnityEngine;

public class SilentRouteRecoverer : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private Transform userCamera;       // XR Origin Main Camera
    [SerializeField] private AvatarEngine avatarEngine;   // To toggle normal 3.0m forward logic

    [Header("Route Settings")]
    [SerializeField] private float deviationThresholdMeters = 5.0f; // Allowed route drift before recovery triggers
    [SerializeField] private float recoveryTrailingDistance = 2.5f; // Distance avatar stays behind user

    private bool _isRecoveringSilently = false;
    private Vector3 _mockTargetRouteWaypoint;

    void Start()
    {
        // Initialize a mock waypoint ahead of the user for testing purposes
        if (userCamera != null)
        {
            _mockTargetRouteWaypoint = userCamera.position + (userCamera.forward * 20.0f);
        }
    }

    void Update()
    {
        if (userCamera == null || avatarEngine == null) return;

        // 1. Calculate how far the runner has drifted from the true route path
        float crossTrackError = CalculateRouteDeviation(userCamera.position);

        // 2. Evaluate if a Silent Recovery needs to be initiated (Requirement 7)
        if (crossTrackError >= deviationThresholdMeters && !_isRecoveringSilently)
        {
            InitiateSilentRouteRecovery();
        }
        else if (crossTrackError < deviationThresholdMeters && _isRecoveringSilently)
        {
            CeaseSilentRouteRecovery();
        }

        // 3. Execute custom behavior matrix if we are in recovery mode
        if (_isRecoveringSilently)
        {
            ExecuteTrailingAndGuidingLogic();
        }
    }

    private float CalculateRouteDeviation(Vector3 userPos)
    {
        // For testing in the editor, we simulate deviation tracking.
        // Pressing the 'D' key simulates drifting 6 meters off-course.
        if (Input.GetKey(KeyCode.D))
        {
            return 6.0f;
        }

        // In your real-world GPS deployment, this calculates the perpendicular 
        // distance between userPos and the nearest node line on your map vector array.
        return 0.0f;
    }

    private void InitiateSilentRouteRecovery()
    {
        _isRecoveringSilently = true;
        Debug.Log("[SILENT RECOVERY] Runner deviated from route. Overriding pacer logic—no warnings triggered.");

        // Turn off the standard 3.0m forward moving average calculation loop
        avatarEngine.enabled = false;
    }

    private void CeaseSilentRouteRecovery()
    {
        _isRecoveringSilently = false;
        Debug.Log("[SILENT RECOVERY] Runner safely returned to route vector. Restoring standard pacer logic.");

        // Hand positioning control back over to the standard forward-facing engine
        avatarEngine.enabled = true;
    }

    private void ExecuteTrailingAndGuidingLogic()
    {
        // Requirement 7: The avatar must move behind the user and run along to guide them back
        // 1. Calculate the vector pointing from the user back toward the true route waypoint
        Vector3 directionToRoute = (_mockTargetRouteWaypoint - userCamera.position).normalized;
        directionToRoute.y = 0; // Keep movement completely horizontal

        // 2. Position the avatar slightly behind the runner, facing the direction of the real path
        Vector3 targetTrailingPosition = userCamera.position - (userCamera.forward * recoveryTrailingDistance);

        // Smoothly glide the avatar to this new tracking position
        transform.position = Vector3.Lerp(transform.position, targetTrailingPosition, Time.deltaTime * 3.0f);

        // Rotate the avatar so it looks toward the true path, guiding the runner's gaze via peripheral vision
        if (directionToRoute != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(directionToRoute);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5.0f);
        }
    }
}