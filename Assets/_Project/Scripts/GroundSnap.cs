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

    [Header("Floor Acquisition")]
    [Tooltip("実測フロアが未取得の間だけ使う想定端末保持高(m)。1回だけ採用して固定される。" +
             "第1期は胸マウント運用のため既定1.2m(手持ち・目線高なら1.5m前後)。" +
             "ARプレーンを掴めば実測値が優先されるので、効くのは走行開始直後だけ")]
    [SerializeField] private float assumedCameraHeightMeters = 1.2f;
    [Tooltip("実測フロア(コライダー/ARプレーン)を掴むまでアバターの描画を抑止する。" +
             "エディタ/E2Eのシーンには実測フロアが存在しないため既定OFF。実機で有効化を検討")]
    [SerializeField] private bool hideUntilMeasuredFloor = false;

    [Tooltip("検出済み平面を無限に延長して拾うとき、確定済みの床からこの高さ以内なら" +
             "「同じ床の続き」として採用する(m)。机など別の高さの面を誤って床にしないための上限")]
    [SerializeField] private float extendedFloorToleranceMeters = 0.5f;

    [Tooltip("前方の壁・断崖でアバターを足踏み停止させる(基本設計書 §4.2)。" +
             "陸上トラックのように壁が単なる背景の環境ではOFFにすると素直に走り続ける")]
    [SerializeField] private bool haltOnObstacles = true;

    // 床面高さの確定・保持(純ロジック)。実測が途切れてもカメラに追従させないための要
    private readonly GroundFloorTracker _floor = new GroundFloorTracker();
    private bool _renderersSuppressed = false;

    private float _targetY;
    private float _currentYVelocity;
    private bool _simulateObstacleActive = false;

    /// <summary>実測フロア(コライダー/ARプレーン)を掴んでいるか。暫定推定中は false。</summary>
    public bool HasMeasuredFloor => _floor.HasMeasuredFloor;

    /// <summary>現在確定している床面高さ(ワールドY)。</summary>
    public float ResolvedFloorY => _floor.FloorY;

    /// <summary>再走行時などに床の確定をやり直す。</summary>
    public void ResetFloor() => _floor.Reset();

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
    
    /// <summary>これ以上「上向き」の面のみ地面として採用する(cos45°≒0.7)。
    /// 壁・天井を床と誤認するとアバターが壁の高さへ跳ね上がり視界から消える。</summary>
    private const float GroundNormalMinDot = 0.7f;

    /// <summary>これ以下の「上向き成分」なら壁とみなす。床や緩斜面を障害物にしない。</summary>
    private const float WallNormalMaxDot = 0.5f;

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

                // 停止中はペーシングのアンカーが更新されないため、解除時に現在位置で
                // 取り直す。これが無いと停止中に置き去りにされたアバターが古い
                // アンカーから復帰し、視界に戻るまでが遅い(あるいはワープする)
                if (avatarEngine != null && userCamera != null)
                    avatarEngine.ResyncPacingAnchor(transform.position, userCamera.position);
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

        ApplyFloorVisibilityGate();
    }

    /// <summary>
    /// 実測フロアを掴むまでアバターの描画を抑止する(既定OFF)。
    /// GameObject自体は無効化しない — 無効化するとこの GroundSnap も止まり、
    /// 復帰判定が二度と走らなくなるため、Renderer の enable のみを切り替える。
    /// </summary>
    private void ApplyFloorVisibilityGate()
    {
        if (!hideUntilMeasuredFloor)
        {
            if (_renderersSuppressed) SetAvatarRenderersEnabled(true);
            return;
        }

        bool shouldHide = !_floor.HasMeasuredFloor;
        if (shouldHide == _renderersSuppressed) return; // 変化時のみ適用
        SetAvatarRenderersEnabled(!shouldHide);
    }

    private void SetAvatarRenderersEnabled(bool visible)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = visible;

        _renderersSuppressed = !visible;
        Debug.Log($"[GroundSnap] 実測フロア未取得によるアバター描画の{(visible ? "再開" : "抑止")} " +
                  $"(Renderer {renderers.Length}件)");
    }

    /// <summary>
    /// 床面高さを解決する。実測(コライダー/ARプレーン)があればそれを採用し、
    /// 無ければ <see cref="GroundFloorTracker"/> が確定済みの床を保持する。
    /// **カメラ高からの推定は最初の1回のみ**(毎フレーム再計算するとアバターが
    /// 頭の上下動に追従して浮き上がるため)。
    /// </summary>
    private float GetCurrentGroundLevel(out Vector3 normal)
    {
        bool measured = TryMeasureGroundLevel(out float measuredY, out normal);

        // 実測を一度も得ていない時だけ使う暫定値(1回だけ採用され固定される)
        float provisional = userCamera != null
            ? userCamera.position.y - assumedCameraHeightMeters
            : 0f;

        if (_floor.Resolve(measured, measuredY, provisional, out float floorY))
        {
            Debug.Log($"[GroundSnap] 床面の由来: {_floor.Source} (Y={floorY:F3})" +
                      (_floor.HasMeasuredFloor
                          ? ""
                          : " — 実測フロア未取得のためカメラ高からの暫定値を固定。" +
                            "シーンにコライダーが無い/ARプレーン未検出の可能性"));
        }

        return floorY;
    }

    /// <summary>実測の床面(コライダー → ARプレーン)を探す。見つからなければ false。</summary>
    private bool TryMeasureGroundLevel(out float groundY, out Vector3 normal)
    {
        normal = Vector3.up;
        groundY = 0f;
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

            // 壁・天井を床と誤認しない。ARKitは垂直平面もコライダー付きで生成するため、
            // 面の向きを見ないと壁の上端を「最も高い地面」として拾ってしまい、
            // アバターが壁の高さへ跳ね上がって視界から消える
            if (Vector3.Dot(h.normal, Vector3.up) < GroundNormalMinDot) continue;

            if (h.point.y > highestGround)
            {
                highestGround = h.point.y;
                normal = h.normal;
                found = true;
            }
        }
        
        if (found)
        {
            groundY = highestGround;
            return true;
        }

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
                    // 垂直平面(壁)は地面にしない
                    if (Vector3.Dot(hit.pose.up, Vector3.up) < GroundNormalMinDot) continue;

                    if (hit.pose.position.y > highestGround)
                    {
                        highestGround = hit.pose.position.y;
                        // For planes, we could use hit.pose.up but Vector3.up is safe
                        normal = Vector3.up; 
                        found = true;
                    }
                }
                if (found)
                {
                    groundY = highestGround;
                    return true;
                }
            }

            // Fallback 2: 検出済み平面を「無限に延長」して拾う。
            //
            // LiDAR/平面検出は歩いた範囲しか地面を作らないため、検出済み領域から
            // 出た瞬間に実測が途切れる。PlaneWithinInfinity は検出済み平面を
            // 境界の外まで延長して判定してくれる = ソフト的に床を伸ばす。
            //
            // 注意: ここで「最も高い面」を採ると、机などの平面が無限に延長されて
            // フロア全体が机の高さになってしまう。**確定済みの床に最も近い候補**を選び、
            // 許容差を超える面は「床の続き」ではないとみなして採用しない。
            if (_arRaycastManager.Raycast(ray, s_Hits, TrackableType.PlaneWithinInfinity))
            {
                bool got = false;
                float bestY = 0f;
                float bestDelta = float.MaxValue;

                foreach (var hit in s_Hits)
                {
                    if (Vector3.Dot(hit.pose.up, Vector3.up) < GroundNormalMinDot) continue;

                    float y = hit.pose.position.y;
                    // 床が確定していれば「それに近い面」、未確定なら「低い面」を優先する
                    float delta = _floor.HasFloor ? Mathf.Abs(y - _floor.FloorY) : -y;

                    if (!got || delta < bestDelta)
                    {
                        bestDelta = delta;
                        bestY = y;
                        got = true;
                    }
                }

                if (got && (!_floor.HasFloor || bestDelta <= extendedFloorToleranceMeters))
                {
                    groundY = bestY;
                    normal = Vector3.up;
                    return true;
                }
            }
        }

        // 実測なし — 確定済みの床の保持は GroundFloorTracker が担当する
        return false;
    }

    private bool IsObstacleDetected()
    {
        // 1. Handle Simulator Flag
        if (_simulateObstacleActive)
        {
            return true;
        }

        // トラック走行では周囲の壁は単なる背景で、そこで伴走者が止まる必要はない。
        // OFFにすると前方の壁・断崖による足踏み停止を行わない(接地判定には影響しない)
        if (!haltOnObstacles) return false;

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

            // 開始位置がコライダー内部だと normal が零ベクトルで返り、壁と誤判定される
            if (h.distance <= 0.0001f) continue;

            // ほぼ垂直な面(=進路を塞ぐ壁)のみを障害物とみなす。
            // 床や緩斜面のコライダーを「高さ1.5m以上」だけで障害物にしない
            if (Mathf.Abs(Vector3.Dot(h.normal, Vector3.up)) > WallNormalMaxDot) continue;

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
            // 前方に地面が「見つからない」ことは断崖の証拠にならない。
            // ARKitの平面検出はまばらで、平坦な床でも3m先が未検出のことが普通にある。
            // ここで停止させていたため屋内では未検出域のたびにアバターが足踏みを始め、
            // ユーザーが追い越して視界から消えていた(=「壁でアバターが消える」の実体)。
            // 断崖は**実測された落差**でのみ判定する(上の foundGroundAhead 分岐)。
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
