using UnityEngine;

/// <summary>
/// プロシージャルジェスチャー (企画書 4.1 自律アクションの可視化):
/// Mixamoの専用モーションが用意されるまでの間、ヒューマノイドボーンを
/// LateUpdate(Animator書き込み後)でワールド空間回転させ、
///  - Beckon: 離隔待機中の手招き(右腕を上げ、前腕を1.6Hzで振る)
///  - CalmDown: バイタル警告中の「落ち着け」(手のひらを前に、ゆっくり上下)
///  - Goodbye: 終了時のお辞儀(背骨+頭を前傾→戻す)
/// を実際に見える形にする。専用モーション導入後もフォールバックとして無害
/// (Animator側に専用ステートが入れば重ね掛けでも自然に馴染む角度に留めている)。
/// AvatarEngineと同じGameObjectに置く(Bootstrapが自動装着)。
/// </summary>
public class ProceduralGestureDriver : MonoBehaviour
{
    private const float FarewellGestureSeconds = 1.6f; // VFXの挨拶ウィンドウに一致
    private const float BlendSpeed = 4f;

    private AvatarEngine _engine;
    private AvatarVisualsAndActions _visuals;
    private Animator _animator;

    private Transform _rightUpperArm;
    private Transform _rightLowerArm;
    private Transform _spine;
    private Transform _head;

    private string _currentPose = "None";
    private float _weight = 0f;
    private bool _prevEnded = false;
    private float _farewellStartTime = -99f;

    /// <summary>現在再生中のジェスチャー("None"/"Beckon"/"CalmDown"/"Goodbye")。E2E検証用。</summary>
    public string ActiveGesture { get; private set; } = "None";

    void Start()
    {
        _engine = GetComponent<AvatarEngine>();
        _visuals = GetComponent<AvatarVisualsAndActions>();
        ResolveRig();
    }

    private void ResolveRig()
    {
        _animator = AvatarRigLocator.FindBestAnimator(transform);
        if (_animator == null || !_animator.isHuman) return;

        _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        _rightLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
        _head = _animator.GetBoneTransform(HumanBodyBones.Head);
    }

    void LateUpdate()
    {
        if (_engine == null) return;
        if (_animator == null || !_animator.isHuman || _rightUpperArm == null)
        {
            ResolveRig(); // モデル切替後の再解決
            if (_animator == null || !_animator.isHuman) return;
        }

        // 終了(挨拶)ウィンドウの立ち上がり検知
        bool ended = _engine.IsSessionEnded;
        if (ended && !_prevEnded) _farewellStartTime = Time.time;
        _prevEnded = ended;

        // 優先度: Goodbye > Beckon > CalmDown
        string target = "None";
        if (ended && Time.time - _farewellStartTime < FarewellGestureSeconds)
            target = "Goodbye";
        else if (!ended && _engine.IsWaitingForUser)
            target = "Beckon";
        else if (!ended && _visuals != null && _visuals.IsVitalWarningActive)
            target = "CalmDown";

        ActiveGesture = target;

        // ブレンド(0.25秒でイン/アウト)。フェードアウト中は直前のポーズを維持
        if (target != "None") _currentPose = target;
        _weight = Mathf.MoveTowards(_weight, target != "None" ? 1f : 0f, Time.deltaTime * BlendSpeed);
        if (_weight <= 0.001f) return;

        ApplyPose(_currentPose, _weight);
    }

    private void ApplyPose(string pose, float weight)
    {
        // ワールド空間の軸で合成するため、ボーンローカル軸の規約に依存しない
        Vector3 right = transform.right;

        switch (pose)
        {
            case "Beckon":
            {
                // 右腕を前上方へ上げ、前腕で「おいでおいで」(1.6Hz)
                RotateBone(_rightUpperArm, right, -70f * weight);
                float wave = Mathf.Sin(Time.time * 2f * Mathf.PI * 1.6f) * 25f;
                RotateBone(_rightLowerArm, right, (-30f + wave) * weight);
                break;
            }
            case "CalmDown":
            {
                // 手のひらを前に見せて、ゆっくり(0.6Hz)沈める「落ち着け」
                RotateBone(_rightUpperArm, right, -80f * weight);
                float bob = Mathf.Sin(Time.time * 2f * Mathf.PI * 0.6f) * 8f;
                RotateBone(_rightLowerArm, right, (-20f + bob) * weight);
                break;
            }
            case "Goodbye":
            {
                // お辞儀: 1.5秒かけて前傾→戻る(サインカーブ)
                float t = Mathf.Clamp01((Time.time - _farewellStartTime) / 1.5f);
                float bow = Mathf.Sin(t * Mathf.PI) * 28f;
                RotateBone(_spine, right, bow * weight);
                RotateBone(_head, right, bow * 0.5f * weight);
                break;
            }
        }
    }

    private static void RotateBone(Transform bone, Vector3 worldAxis, float degrees)
    {
        if (bone == null || Mathf.Approximately(degrees, 0f)) return;
        bone.rotation = Quaternion.AngleAxis(degrees, worldAxis) * bone.rotation;
    }
}
