using UnityEngine;

public class GroundSnap : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private Transform userCamera;           // XR Origin Main Camera
    [SerializeField] private AvatarEngine avatarEngine;       // Main pacing spatial engine

    [Header("Smoothing Settings")]
    [SerializeField] private float smoothTime = 0.3f;        // Exactly 0.3 seconds easing rule (Requirement 4.2)
    [SerializeField] private float stepThreshold = 0.15f;    // Trigger smoothing if vertical delta > 15cm (Requirement 4.2)

    [Header("LiDAR & Physics Settings")]
    [SerializeField] private LayerMask environmentLayerMask = ~0; // For terrain raycasting
    [SerializeField] private LayerMask obstacleLayerMask = ~0;    // For cliff/wall detection
    [SerializeField] private float obstacleDetectionDistance = 3.0f; // Requirement 4.2: Within 3.0 meters
    [SerializeField] private float minObstacleHeight = 1.5f;       // Requirement 4.2: Obstruction >= 1.5m

    private float _targetY;
    private float _currentYVelocity;
    private bool _simulateObstacleActive = false;
    private bool _wasHaltedLastFrame = false;

    private void Start()
    {
        _targetY = transform.position.y;
        
        if (avatarEngine == null)
        {
            avatarEngine = GetComponent<AvatarEngine>();
        }
    }

    private void Update()
    {
        // 1. Simulator Input for Testing Cliff Exception
        if (Input.GetKeyDown(KeyCode.C))
        {
            _simulateObstacleActive = !_simulateObstacleActive;
            Debug.Log($"[SIMULATOR] Obstacle/Cliff Simulation toggled to: {_simulateObstacleActive}");
        }

        // 2. Obstacle / Cliff Proximity Auditing
        bool obstacleDetected = IsObstacleDetected();

        if (avatarEngine != null)
        {
            avatarEngine.IsHalted = obstacleDetected;
        }

        // Trigger visual states and animations when halting states shift
        if (obstacleDetected != _wasHaltedLastFrame)
        {
            _wasHaltedLastFrame = obstacleDetected;
            UpdateAnimatorState(obstacleDetected);
            
            if (obstacleDetected)
            {
                Debug.LogWarning("[CLIFF EXCEPTION] Solid wall/cliff detected ahead! Avatar halting and entering In-Place Jog.");
            }
            else
            {
                Debug.Log("[CLIFF EXCEPTION] Path is now clear. Avatar resuming forward pace.");
            }
        }

        // 3. Ground Level Raycast Checking (LiDAR Snapping)
        float currentDetectedGroundHeight = GetCurrentGroundLevel(); 

        // If the vertical delta exceeds 15cm, lock in target and transition smoothly
        if (Mathf.Abs(transform.position.y - currentDetectedGroundHeight) > stepThreshold)
        {
            _targetY = currentDetectedGroundHeight;
            float smoothedY = Mathf.SmoothDamp(transform.position.y, _targetY, ref _currentYVelocity, smoothTime);
            transform.position = new Vector3(transform.position.x, smoothedY, transform.position.z);
        }
        else
        {
            // Small steps (<= 15cm): snap instantly to ground level
            _targetY = currentDetectedGroundHeight;
            _currentYVelocity = 0.0f;
            transform.position = new Vector3(transform.position.x, currentDetectedGroundHeight, transform.position.z);
        }
    }

    private float GetCurrentGroundLevel()
    {
        // Perform standard vertical down-cast from above the avatar coordinates to snap precisely to colliders
        RaycastHit hit;
        Vector3 rayOrigin = new Vector3(transform.position.x, transform.position.y + 10.0f, transform.position.z);
        if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 20.0f, environmentLayerMask))
        {
            return hit.point.y;
        }

        // Fallback baseline zero-plane
        return 0f; 
    }

    private bool IsObstacleDetected()
    {
        // 1. Handle Simulator Flag
        if (_simulateObstacleActive)
        {
            return true;
        }

        if (userCamera == null) return false;

        // 2. Continuous LiDAR-like spatial scanning
        // Perform a horizontal spherecast/raycast from the user camera forward vector
        RaycastHit hit;
        Vector3 rayOrigin = userCamera.position;
        Vector3 rayDirection = userCamera.forward;
        rayDirection.y = 0; // Lock to horizontal tracking plane
        rayDirection.Normalize();

        // Cast a sphere forward up to 3.0 meters (Requirement 4.2)
        if (Physics.SphereCast(rayOrigin, 0.4f, rayDirection, out hit, obstacleDetectionDistance, obstacleLayerMask))
        {
            // Verify if the height of the obstruction qualifies as a solid cliff or wall (>= 1.5m)
            // By checking the hit collider bounds size or the absolute distance to the hit normal surface
            if (hit.collider != null)
            {
                float boundsHeight = hit.collider.bounds.size.y;
                if (boundsHeight >= minObstacleHeight)
                {
                    return true;
                }
            }
        }

        // 3. Under-foot Cliff Drop checking
        // Perform a vertical raycast down exactly 3.0 meters ahead along user path of progression.
        // If the ground drops dramatically (cliff edge) or is missing, halt progression.
        Vector3 checkAheadPoint = userCamera.position + (rayDirection * obstacleDetectionDistance);
        RaycastHit cliffHit;
        if (Physics.Raycast(checkAheadPoint + (Vector3.up * 2.0f), Vector3.down, out cliffHit, 10.0f, environmentLayerMask))
        {
            float groundLevelAhead = cliffHit.point.y;
            float currentGroundLevel = transform.position.y;
            
            // If the ground drop ahead is greater than or equal to 1.5m, qualify it as a cliff
            if (currentGroundLevel - groundLevelAhead >= minObstacleHeight)
            {
                return true;
            }
        }
        else
        {
            // If we cast down 10 meters and find no ground, it's definitely a cliff or void!
            return true;
        }

        return false;
    }

    private void UpdateAnimatorState(bool isHalted)
    {
        // Recursively locate an Animator on this pacing companion model
        Animator animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.SetBool("IsInPlaceJog", isHalted);
        }
    }
}
