using UnityEngine;

public class AvatarVisualsAndActions : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform userCamera;       // XR Origin Main Camera
    [SerializeField] private MeshRenderer avatarRenderer; // Capsule Mesh Renderer
    [SerializeField] private AvatarEngine avatarEngine;   // 進行方向・目標リード取得用

    private SkinnedMeshRenderer _avatarSkinnedRenderer;   // Added for VRChat model compatibility

    [Header("Bio-Luminescence Pulse")]
    [SerializeField] private float baseIntensity = 1.0f;
    [SerializeField] private float pulseAmplitude = 1.5f;

    [Header("Pace-Sync Colors (基本設計書 §7.1)")]
    [Tooltip("ジャスト判定の許容(目標リード±m)")]
    [SerializeField] private float justToleranceMeters = 1.5f;
    [Tooltip("超過グラデ幅(m)。ユーザーがこの分だけ詰めると完全に青")]
    [SerializeField] private float overPaceSpanMeters = 1.5f;
    [Tooltip("遅延グラデ幅(m)。ジャスト帯からこの分だけ離れると完全に赤")]
    [SerializeField] private float behindSpanMeters = 3.0f;

    [Header("Vital Warning (企画書 4.1 — 心拍過負荷 / 第1期スコープ外)")]
    [Tooltip("BPM at or above which the avatar turns deep blue and performs the calm-down hand sign.")]
    [SerializeField] private int vitalWarningBpmThreshold = 185;
    [Tooltip("Optional avatar Animator. Trigger 'CalmDownSign' fires once per overload episode.")]
    [SerializeField] private Animator avatarAnimator;

    private Material _glowMaterial;
    private int _currentHeartRate = 60; // Baseline default
    private bool _vitalWarningActive = false;
    private string _paceColorState = "Just";

    // ペースシンクロ色 (基本設計書 §7.1)
    private static readonly Color PaceGreen = new Color(0.15f, 1.0f, 0.35f);  // ジャスト(安定)
    private static readonly Color PaceOrange = new Color(1.0f, 0.55f, 0.0f);  // 遅延開始(警告)
    private static readonly Color PaceRed = new Color(1.0f, 0.12f, 0.10f);    // 遅延大(危険)
    private static readonly Color PaceBlue = new Color(0.10f, 0.45f, 1.0f);   // 超過(過速)
    private static readonly Color DeepBlueVital = new Color(0.05f, 0.15f, 0.9f); // HR過負荷

    public bool IsVitalWarningActive => _vitalWarningActive;
    /// <summary>現在のペースシンクロ状態("Just"/"Behind"/"OverPace"/"Vital")。E2E検証用。</summary>
    public string PaceColorState => _paceColorState;

    void Start()
    {
        if (avatarEngine == null)
            avatarEngine = GetComponent<AvatarEngine>() ?? FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        RefreshMaterialReference();
    }

    void Update()
    {
        if (userCamera == null || _glowMaterial == null) return;

        // 1. ペースシンクロ・カラー (§7.1): 進行方向へのアバター符号付きリード距離を算出
        Color targetBaseColor = ComputePaceSyncColor();

        // 2. バイタル警告(企画書4.1・第1期スコープ外)は優先オーバーライド:
        //    心拍過負荷で深青 + 落ち着けサイン
        if (_currentHeartRate >= vitalWarningBpmThreshold)
        {
            targetBaseColor = DeepBlueVital;
            _paceColorState = "Vital";

            if (!_vitalWarningActive)
            {
                _vitalWarningActive = true;
                Debug.LogWarning($"[VITAL WARNING] HR {_currentHeartRate} BPM >= {vitalWarningBpmThreshold}. Deep-blue state + calm-down sign.");
                if (avatarAnimator != null)
                    avatarAnimator.SetTrigger("CalmDownSign");
            }
        }
        else if (_vitalWarningActive && _currentHeartRate < vitalWarningBpmThreshold - 5)
        {
            // 5 BPM hysteresis so the color does not flicker at the threshold
            _vitalWarningActive = false;
        }

        // 3. Compute Bio-Luminescence Pulse Frequency using Heart Rate
        float pulseFrequency = (_currentHeartRate / 60.0f) * Mathf.PI * 2.0f;

        // Use a sine wave to create a smooth, continuous glowing oscillation
        float sineWave = Mathf.Sin(Time.time * (pulseFrequency / 2.0f));
        float currentIntensity = baseIntensity + (sineWave * pulseAmplitude);

        // 4. Apply Final HDR Color and Light Intensity Matrix to the shader
        Color finalGlowColor = targetBaseColor * currentIntensity;
        _glowMaterial.SetColor("_EmissionColor", finalGlowColor);
    }

    private Color ComputePaceSyncColor()
    {
        // 進行方向(純化ヘディング)。無い場合はアバターの向きで代替
        Vector3 heading = avatarEngine != null ? avatarEngine.CurrentHeading : transform.forward;
        heading.y = 0f;
        if (heading.sqrMagnitude < 0.0001f) heading = transform.forward;
        heading.Normalize();

        Vector3 toAvatar = transform.position - userCamera.position;
        toAvatar.y = 0f;
        float signedLead = Vector3.Dot(toAvatar, heading);

        float targetLead = avatarEngine != null ? avatarEngine.LeadDistanceMeters : 3.0f;

        var state = AvatarPaceColor.Evaluate(signedLead, targetLead, justToleranceMeters,
            overPaceSpanMeters, behindSpanMeters, out float t);

        switch (state)
        {
            case AvatarPaceColor.PaceState.Behind:
                _paceColorState = "Behind";
                return Color.Lerp(PaceOrange, PaceRed, t);
            case AvatarPaceColor.PaceState.OverPace:
                _paceColorState = "OverPace";
                return Color.Lerp(PaceGreen, PaceBlue, t);
            default:
                _paceColorState = "Just";
                return PaceGreen;
        }
    }

    // --- THE MISSING LINK FOR MODEL SWITCHING ---
    public void UpdateActiveRenderer(MeshRenderer staticMesh, SkinnedMeshRenderer skinnedMesh)
    {
        avatarRenderer = staticMesh;
        _avatarSkinnedRenderer = skinnedMesh;

        // Reset the cached material reference so it grabs from the new renderer
        _glowMaterial = null; 
        RefreshMaterialReference();
    }

    private void RefreshMaterialReference()
    {
        if (_glowMaterial != null) return;

        if (avatarRenderer != null)
        {
            _glowMaterial = avatarRenderer.material;
        }
        else if (_avatarSkinnedRenderer != null)
        {
            _glowMaterial = _avatarSkinnedRenderer.material;
        }

        if (_glowMaterial != null)
        {
            _glowMaterial.EnableKeyword("_EMISSION");

            // 企画書 4.1: 起動時から透過率50%の半透明を適用
            // (マテリアルが透過モードでない場合は視覚上no-op)
            Color baseColor = _glowMaterial.color;
            _glowMaterial.color = new Color(
                baseColor.r, baseColor.g, baseColor.b,
                GameStateController.AvatarBaseAlpha);
        }
    }

    // Public gateway method to feed data directly from your Apple Watch BLE script loop
    public void UpdateHeartRate(int newBpm)
    {
        _currentHeartRate = newBpm;
    }
}