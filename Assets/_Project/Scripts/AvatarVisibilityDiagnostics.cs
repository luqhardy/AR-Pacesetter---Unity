using UnityEngine;

/// <summary>
/// 「アバターが見えない理由」を毎フレーム判定し、**変化した時だけ**ログとSwiftへ報告する。
/// 判定そのものは <see cref="AvatarVisibilityReason"/>(純ロジック)。
///
/// <para>実機で「消えた」と言われたとき、Xcodeのログの <c>[VISIBILITY]</c> 行と
/// 端末画面のバナーを見れば、**どの経路で**消えたかが一発で分かる。
/// 症状の報告を経路の報告に変えるための装置。</para>
/// </summary>
public class AvatarVisibilityDiagnostics : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private GameStateController stateController;
    [SerializeField] private ARPlaneOcclusionController planeOcclusion;

    [Tooltip("視野判定の余白(ビューポート単位)。画面端ぎりぎりを『視野外』にしない")]
    [SerializeField] private float viewportMargin = 0.15f;

    /// <summary>直近の判定: 見えているか。</summary>
    public bool IsVisible { get; private set; } = true;

    /// <summary>直近の判定理由(見えていれば "visible" で始まる)。</summary>
    public string CurrentReason { get; private set; } = "visible";

    private Renderer[] _renderers = System.Array.Empty<Renderer>();
    private float _nextRendererRefresh;
    private string _lastReported;

    private void Awake()
    {
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (stateController == null)
            stateController = FindFirstObjectByType<GameStateController>(FindObjectsInactive.Include);
        if (planeOcclusion == null)
            planeOcclusion = FindFirstObjectByType<ARPlaneOcclusionController>(FindObjectsInactive.Include);
    }

    private void LateUpdate()
    {
        if (avatarEngine == null) return;

        // 走行前は「見えない」のが正常なので報告しない
        if (!avatarEngine.HasStarted || avatarEngine.IsSessionEnded) return;

        RefreshRenderersIfDue();

        var inputs = new AvatarVisibilityReason.Inputs
        {
            GameObjectActive     = avatarEngine.gameObject.activeInHierarchy,
            AnyRendererEnabled   = AnyRendererEnabled(),
            FsmState             = stateController != null ? stateController.currentState.ToString() : "Normal",
            Halted               = avatarEngine.IsHalted,
            WaitingForUser       = avatarEngine.IsWaitingForUser,
            OverriddenByRecovery = avatarEngine.IsOverriddenByRecovery,
            PlaneOcclusionEnabled = planeOcclusion != null && planeOcclusion.OccludeAvatarBehindPlanes,
        };
        FillCameraRelation(ref inputs);

        bool visible = AvatarVisibilityReason.Resolve(inputs, out string reason);
        IsVisible = visible;
        CurrentReason = reason;

        if (reason == _lastReported) return;
        _lastReported = reason;

        if (visible) Debug.Log($"[VISIBILITY] {reason}");
        else         Debug.LogWarning($"[VISIBILITY] アバター非表示 — {reason}");

        SwiftMessageSender.SendAvatarVisibility(visible, reason);
    }

    private void RefreshRenderersIfDue()
    {
        // モデル差し替え(AvatarModelSwitcher)に追従するため、たまに取り直す
        if (Time.time < _nextRendererRefresh && _renderers.Length > 0) return;
        _nextRendererRefresh = Time.time + 2f;
        _renderers = avatarEngine.GetComponentsInChildren<Renderer>(true);
    }

    private bool AnyRendererEnabled()
    {
        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            if (r != null && r.enabled && r.gameObject.activeInHierarchy) return true;
        }
        return _renderers.Length == 0; // Rendererが無いモデルは「描画抑止ではない」とみなす
    }

    private void FillCameraRelation(ref AvatarVisibilityReason.Inputs inputs)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            inputs.InCameraView = true; // カメラが無ければ視野判定はしない
            return;
        }

        Vector3 pos = avatarEngine.transform.position;
        Vector3 toAvatar = pos - cam.transform.position;
        Vector3 flat = new Vector3(toAvatar.x, 0f, toAvatar.z);
        Vector3 camFlat = new Vector3(cam.transform.forward.x, 0f, cam.transform.forward.z);

        inputs.DistanceMeters = flat.magnitude;
        inputs.HorizontalAngleDegrees = (flat.sqrMagnitude > 0.0001f && camFlat.sqrMagnitude > 0.0001f)
            ? Vector3.Angle(camFlat, flat)
            : 0f;

        // アバターの胸あたり(原点+1m)で判定。原点は足元なので下端で切れやすい
        Vector3 vp = cam.WorldToViewportPoint(pos + Vector3.up * 1.0f);
        float m = viewportMargin;
        inputs.InCameraView = vp.z > 0f
            && vp.x >= -m && vp.x <= 1f + m
            && vp.y >= -m && vp.y <= 1f + m;
    }
}
