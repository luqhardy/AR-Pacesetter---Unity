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
    private bool _isEasing = false;

    private void Start()
    {
        _targetY = GetCurrentGroundLevel();
        transform.position = new Vector3(transform.position.x, _targetY, transform.position.z);
        
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

        // If the target ground level changed by more than 15cm, trigger/update easing
        if (Mathf.Abs(currentDetectedGroundHeight - _targetY) > stepThreshold)
        {
            _targetY = currentDetectedGroundHeight;
            _isEasing = true;
        }

        if (_isEasing)
        {
            float smoothedY = Mathf.SmoothDamp(transform.position.y, _targetY, ref _currentYVelocity, smoothTime);
            transform.position = new Vector3(transform.position.x, smoothedY, transform.position.z);

            // Once we are extremely close to the target, end easing to prevent floating precision jitter
            if (Mathf.Abs(transform.position.y - _targetY) < 0.001f)
            {
                transform.position = new Vector3(transform.position.x, _targetY, transform.position.z);
                _currentYVelocity = 0.0f;
                _isEasing = false;
            }
        }
        else
        {
            // Small steps (<= 15cm) and no active transition: snap instantly to ground level
            _targetY = currentDetectedGroundHeight;
            _currentYVelocity = 0.0f;
            transform.position = new Vector3(transform.position.x, currentDetectedGroundHeight, transform.position.z);
        }
    }

    private float GetCurrentGroundLevel()
    {
        // Perform standard vertical down-cast to snap precisely to colliders
        // Use a safe height (at least camera height) to prevent falling through the floor forever
        float safeY = Mathf.Max(transform.position.y, userCamera != null ? userCamera.position.y : 0f) + 10.0f;
        Vector3 rayOrigin = new Vector3(transform.position.x, safeY, transform.position.z);
        
        RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, 20.0f, environmentLayerMask);
        float highestGround = -1000f;
        bool found = false;
        
        foreach (var h in hits)
        {
            // Ignore the avatar's own colliders
            if (h.transform.root == transform.root) continue;
            
            // Fix: Ignore the user camera's root as well to prevent snapping to the player's head/body
            if (userCamera != null && h.transform.root == userCamera.root) continue;
            
            if (h.point.y > highestGround)
            {
                highestGround = h.point.y;
                found = true;
            }
        }
        
        if (found) return highestGround;

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
        RaycastHit[] hits = Physics.SphereCastAll(rayOrigin, 0.4f, rayDirection, obstacleDetectionDistance, obstacleLayerMask);
        foreach (var h in hits)
        {
            if (h.transform.root == transform.root) continue;
            
            // Verify if the height of the obstruction qualifies as a solid cliff or wall (>= 1.5m)
            if (h.collider != null)
            {
                float boundsHeight = h.collider.bounds.size.y;
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
        RaycastHit[] cliffHits = Physics.RaycastAll(checkAheadPoint + (Vector3.up * 2.0f), Vector3.down, 10.0f, environmentLayerMask);
        
        bool foundGroundAhead = false;
        float groundLevelAhead = -1000f;
        foreach (var h in cliffHits)
        {
            if (h.transform.root == transform.root) continue;
            if (h.point.y > groundLevelAhead)
            {
                groundLevelAhead = h.point.y;
                foundGroundAhead = true;
            }
        }
        
        if (foundGroundAhead)
        {
            float currentGroundLevel = transform.position.y;
            
            // If the ground drop ahead is greater than or equal to 1.5m, qualify it as a cliff
            if (currentGroundLevel - groundLevelAhead >= minObstacleHeight)
            {
                return true;
            }
        }
        else
        {
            // If we cast down 10 meters and find no ground, check if there is ground under the user.
            // If there is ground under the user, then missing ground ahead is a real cliff/void.
            // If there is no ground under the user either, we are in a colliderless scene, so do not halt.
            RaycastHit userGroundHit;
            bool groundUnderUser = Physics.Raycast(userCamera.position + Vector3.up * 2.0f, Vector3.down, out userGroundHit, 20.0f, environmentLayerMask);
            if (groundUnderUser)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateAnimatorState(bool isHalted)
    {
        OvertakeBehaviourController overtake = GetComponent<OvertakeBehaviourController>();
        Animator animator = (overtake != null && overtake.ActiveAnimator != null) ? overtake.ActiveAnimator : GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.SetBool("IsInPlaceJog", isHalted);
        }
    }
}
