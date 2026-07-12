using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

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

    /// <summary>E2E/エディタ検証用: 障害物検知の強制ON/OFF(Cキーと同じ)。</summary>
    public bool SimulateObstacle
    {
        get => _simulateObstacleActive;
        set => _simulateObstacleActive = value;
    }
    private bool _wasHaltedLastFrame = false;
    private bool _isEasing = false;
    private float _lerpTimer = 0f;
    private float _startY = 0f;
    
    private static RaycastHit[] s_RaycastHits = new RaycastHit[32];
    private static RaycastHit[] s_SphereCastHits = new RaycastHit[32];

    [Header("Terrain Alignment")]
    [SerializeField] private bool alignWithTerrainNormal = true;
    [SerializeField] private float alignmentSpeed = 5.0f;
    [SerializeField] private float maxTiltAngle = 20.0f;

    private Vector3 _currentNormal = Vector3.up;
    private ARRaycastManager _arRaycastManager;
    private static List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

    private void Start()
    {
#if UNITY_2023_1_OR_NEWER
        _arRaycastManager = Object.FindFirstObjectByType<ARRaycastManager>();
#else
        _arRaycastManager = Object.FindObjectOfType<ARRaycastManager>();
#endif
        if (_arRaycastManager == null && userCamera != null)
        {
            _arRaycastManager = userCamera.transform.root.gameObject.AddComponent<ARRaycastManager>();
            Debug.Log("[GroundSnap] Dynamically added ARRaycastManager to XR Origin root.");
        }
        // Initial snap
        _targetY = GetCurrentGroundLevel(out _currentNormal);
        transform.position = new Vector3(transform.position.x, _targetY, transform.position.z);

        // Always prefer the engine on this same GameObject: a stale reference to a
        // different (orphaned) AvatarEngine would send IsHalted to the wrong object
        // and the visible avatar would ignore cliffs/obstacles entirely.
        AvatarEngine localEngine = GetComponent<AvatarEngine>();
        if (localEngine != null && avatarEngine != localEngine)
        {
            if (avatarEngine != null)
                Debug.LogWarning("[GroundSnap] avatarEngine pointed at a different object — rebound to the engine on this avatar.");
            avatarEngine = localEngine;
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
    }

    private void LateUpdate()
    {
        // 3. Ground Level Raycast Checking (LiDAR Snapping)
        // We move this to LateUpdate to ensure we are the final authority on Y height
        // after AvatarEngine has calculated the horizontal movement.
        Vector3 groundNormal;
        float currentDetectedGroundHeight = GetCurrentGroundLevel(out groundNormal); 

        // Exactly 0.3 seconds easing rule for changes > 15cm
        if (Mathf.Abs(currentDetectedGroundHeight - _targetY) > stepThreshold)
        {
            _targetY = currentDetectedGroundHeight;
            _isEasing = true;
            _lerpTimer = 0f;
            _startY = transform.position.y;
        }
        else if (!_isEasing)
        {
            _targetY = currentDetectedGroundHeight;
        }

        float smoothedY = transform.position.y;
        
        if (_isEasing)
        {
            _lerpTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_lerpTimer / smoothTime);
            // Smooth cubic ease-in-out
            float easeT = t * t * (3f - 2f * t);
            smoothedY = Mathf.Lerp(_startY, _targetY, easeT);
            
            if (t >= 1.0f)
            {
                _isEasing = false;
                smoothedY = _targetY;
            }
        }
        else
        {
            // For <= 15cm bumps, use faster 0.1s smoothdamp to absorb LiDAR noise
            smoothedY = Mathf.SmoothDamp(transform.position.y, _targetY, ref _currentYVelocity, 0.1f);
            if (Mathf.Abs(smoothedY - _targetY) < 0.001f)
            {
                smoothedY = _targetY;
                _currentYVelocity = 0.0f;
            }
        }

        transform.position = new Vector3(transform.position.x, smoothedY, transform.position.z);

        // 4. Terrain Normal Alignment
        if (alignWithTerrainNormal)
        {
            _currentNormal = Vector3.Slerp(_currentNormal, groundNormal, Time.deltaTime * alignmentSpeed);
            
            // Limit tilt angle
            float tilt = Vector3.Angle(Vector3.up, _currentNormal);
            if (tilt > maxTiltAngle)
            {
                _currentNormal = Vector3.RotateTowards(Vector3.up, _currentNormal, maxTiltAngle * Mathf.Deg2Rad, 0f);
            }

            Quaternion targetRot = Quaternion.FromToRotation(transform.up, _currentNormal) * transform.rotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * alignmentSpeed);
        }
    }

    private float GetCurrentGroundLevel(out Vector3 normal)
    {
        normal = Vector3.up;
        // Perform standard vertical down-cast to snap precisely to colliders
        // Use a safe height (at least camera height) to prevent falling through the floor forever
        float safeY = Mathf.Max(transform.position.y, userCamera != null ? userCamera.position.y : 0f) + 10.0f;
        Vector3 rayOrigin = new Vector3(transform.position.x, safeY, transform.position.z);
        
        int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, s_RaycastHits, 20.0f, environmentLayerMask, QueryTriggerInteraction.Ignore);
        float highestGround = -1000f;
        bool found = false;
        
        for (int i = 0; i < hitCount; i++)
        {
            var h = s_RaycastHits[i];
            // Ignore the avatar's own colliders
            if (h.transform.root == transform.root) continue;
            
            // Fix: Ignore the user camera's root as well to prevent snapping to the player's head/body
            if (userCamera != null && h.transform.root == userCamera.root) continue;
            
            if (h.point.y > highestGround)
            {
                highestGround = h.point.y;
                normal = h.normal;
                found = true;
            }
        }
        
        if (found) return highestGround;

        // Fallback 1: AR Raycast against detected AR Planes
        if (_arRaycastManager != null)
        {
            // ARRaycastManager requires screen point or Ray. We construct a ray from safeY downwards.
            Ray ray = new Ray(rayOrigin, Vector3.down);
            if (_arRaycastManager.Raycast(ray, s_Hits, TrackableType.PlaneWithinPolygon))
            {
                // Find the highest point
                highestGround = -1000f;
                foreach (var hit in s_Hits)
                {
                    if (hit.pose.position.y > highestGround)
                    {
                        highestGround = hit.pose.position.y;
                        // For planes, we could use hit.pose.up but Vector3.up is safe
                        normal = Vector3.up; 
                        found = true;
                    }
                }
                if (found) return highestGround;
            }
        }

        // Fallback 2: Heuristic floor level based on user height
        if (userCamera != null)
        {
            return userCamera.position.y - 1.5f; // Assumes phone is held at 1.5m height
        }

        // Fallback 3: baseline zero-plane
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
        Vector3 rayOrigin = userCamera.position;
        Vector3 rayDirection = userCamera.forward;
        rayDirection.y = 0; // Lock to horizontal tracking plane
        rayDirection.Normalize();

        // Cast a sphere forward up to 3.0 meters (Requirement 4.2)
        int sphereHitCount = Physics.SphereCastNonAlloc(rayOrigin, 0.4f, rayDirection, s_SphereCastHits, obstacleDetectionDistance, obstacleLayerMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < sphereHitCount; i++)
        {
            var h = s_SphereCastHits[i];
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
        int cliffHitCount = Physics.RaycastNonAlloc(checkAheadPoint + (Vector3.up * 2.0f), Vector3.down, s_RaycastHits, 10.0f, environmentLayerMask, QueryTriggerInteraction.Ignore);
        
        bool foundGroundAhead = false;
        float groundLevelAhead = -1000f;
        for (int i = 0; i < cliffHitCount; i++)
        {
            var h = s_RaycastHits[i];
            if (h.transform.root == transform.root) continue;
            if (h.point.y > groundLevelAhead)
            {
                groundLevelAhead = h.point.y;
                foundGroundAhead = true;
            }
        }

        float userGroundLevel = transform.position.y;
        RaycastHit userGroundHit;
        bool groundUnderUser = Physics.Raycast(userCamera.position + Vector3.up * 2.0f, Vector3.down, out userGroundHit, 20.0f, environmentLayerMask, QueryTriggerInteraction.Ignore);
        if (groundUnderUser)
        {
            userGroundLevel = userGroundHit.point.y;
        }
        
        if (foundGroundAhead)
        {
            // Compare ground level ahead with the user's ground level to prevent snapping feedback loop issues
            if (userGroundLevel - groundLevelAhead >= minObstacleHeight)
            {
                return true;
            }
        }
        else
        {
            // If we cast down 10 meters and find no ground, check if there is ground under the user.
            // If there is ground under the user, then missing ground ahead is a real cliff/void.
            // If there is no ground under the user either, we are in a colliderless scene, so do not halt.
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
        Animator animator = (overtake != null && overtake.ActiveAnimator != null) ? overtake.ActiveAnimator : AvatarRigLocator.FindBestAnimator(transform);
        if (animator != null)
        {
            animator.SetBool("IsHalted", isHalted);
            animator.SetBool("IsInPlaceJog", isHalted);
        }
    }
}
