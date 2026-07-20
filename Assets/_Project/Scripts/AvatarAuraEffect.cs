using UnityEngine;

/// <summary>
/// ペーシング・オーラエフェクト (基本設計書 §7.2):
/// 目標より5.0m以上遅れると、アバターの足元からランナー側へ向けて
/// 光のラインを地面に放射する。ランナーは前方を向いたまま、周辺視野に入る
/// 光の「流れの速さ・密度」だけで遅れ具合を把握できる。
///
/// ラインは実行時生成のLineRenderer(ワールド空間・アバター非親)で、
/// アセット不要。AvatarEngineと同じGameObjectに置く(Bootstrapが自動装着)。
/// </summary>
public class AvatarAuraEffect : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private Transform userCamera;

    [Header("Activation (基本設計書 §7.2)")]
    [Tooltip("目標リードからこの遅延(m)を超えるとオーラを放射")]
    [SerializeField] private float activationDelayMeters = 5.0f;
    [Tooltip("この遅延(m)で強度(密度・流速)が最大になる")]
    [SerializeField] private float fullIntensityDelayMeters = 12.0f;

    [Header("Visuals")]
    [SerializeField] private int maxStreaks = 7;
    [SerializeField] private int minStreaks = 3;
    [SerializeField] private float streakLengthMeters = 0.9f;
    [SerializeField] private float minFlowSpeed = 3.0f;   // m/s
    [SerializeField] private float maxFlowSpeed = 9.0f;   // m/s
    [SerializeField] private float groundOffset = 0.03f;

    private static readonly Color AuraOrange = new Color(1.0f, 0.55f, 0.0f);
    private static readonly Color AuraRed = new Color(1.0f, 0.15f, 0.08f);

    private LineRenderer[] _streaks;
    private Material _material;
    private float _phase;
    private bool _auraActive;
    private float _auraIntensity;

    /// <summary>オーラ放射中か(E2E検証用)。</summary>
    public bool IsAuraActive => _auraActive;
    /// <summary>現在の強度[0,1]。非発動時は0。</summary>
    public float AuraIntensity => _auraIntensity;

    void Start()
    {
        if (avatarEngine == null)
            avatarEngine = GetComponent<AvatarEngine>() ?? FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (userCamera == null && Camera.main != null)
            userCamera = Camera.main.transform;

        BuildStreaks();
    }

    private void BuildStreaks()
    {
        Shader shader = Shader.Find("Sprites/Default"); // 全ビルド互換
        _material = shader != null ? new Material(shader) : null;

        _streaks = new LineRenderer[maxStreaks];
        for (int i = 0; i < maxStreaks; i++)
        {
            var go = new GameObject($"AuraStreak_{i}");
            go.transform.SetParent(null); // ワールド固定(アバターの回転/スケールを継がない)

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.startWidth = 0.07f;
            lr.endWidth = 0.02f;
            lr.numCapVertices = 2;
            if (_material != null) lr.material = _material;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            go.SetActive(false);
            _streaks[i] = lr;
        }
    }

    void Update()
    {
        if (_streaks == null || avatarEngine == null || userCamera == null) return;

        // 走行中のみ(準備画面・終了後は消灯)
        bool running = avatarEngine.HasStarted && !avatarEngine.IsSessionEnded;
        if (!running)
        {
            SetActiveStreaks(0);
            _auraActive = false;
            _auraIntensity = 0f;
            return;
        }

        // 進行方向へのアバター符号付きリード距離 → 目標からの超過遅延
        Vector3 heading = avatarEngine.CurrentHeading;
        heading.y = 0f;
        if (heading.sqrMagnitude < 0.0001f) heading = transform.forward;
        heading.Normalize();

        Vector3 toAvatar = transform.position - userCamera.position;
        toAvatar.y = 0f;
        float signedLead = Vector3.Dot(toAvatar, heading);
        float deviation = signedLead - avatarEngine.LeadDistanceMeters;

        _auraActive = AuraFeedback.TryEvaluate(deviation, activationDelayMeters,
            fullIntensityDelayMeters, out _auraIntensity);

        if (!_auraActive)
        {
            SetActiveStreaks(0);
            return;
        }

        DrawStreaks(signedLead, heading);
    }

    private void DrawStreaks(float signedLead, Vector3 heading)
    {
        // 遅れが大きいほど密度と流速が上がる(§7.2: 流れの速さ・密度で遅れを伝える)
        int count = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(minStreaks, maxStreaks, _auraIntensity)), 1, maxStreaks);
        float flowSpeed = Mathf.Lerp(minFlowSpeed, maxFlowSpeed, _auraIntensity);

        // アバター足元 → ランナー側 の地面ライン
        float groundY = transform.position.y + groundOffset;
        Vector3 avatarGround = new Vector3(transform.position.x, groundY, transform.position.z);
        Vector3 userGround = new Vector3(userCamera.position.x, groundY, userCamera.position.z);

        float span = Mathf.Max(Vector3.Distance(avatarGround, userGround), 0.5f);
        _phase += (flowSpeed / span) * Time.deltaTime;
        _phase -= Mathf.Floor(_phase); // 0..1 で循環

        Color auraColor = Color.Lerp(AuraOrange, AuraRed, _auraIntensity);
        float lengthFraction = Mathf.Clamp01(streakLengthMeters / span);

        SetActiveStreaks(count);

        for (int i = 0; i < count; i++)
        {
            float u = _phase + (float)i / count;
            u -= Mathf.Floor(u);

            // 先頭はランナー側へ流れ、尾はアバター側へ伸びる
            Vector3 head = Vector3.Lerp(avatarGround, userGround, u);
            Vector3 tail = Vector3.Lerp(avatarGround, userGround, Mathf.Max(0f, u - lengthFraction));

            var lr = _streaks[i];
            lr.SetPosition(0, tail);
            lr.SetPosition(1, head);

            // 端でフェードして唐突な出現・消滅を避ける
            float edgeFade = Mathf.Clamp01(Mathf.Min(u, 1f - u) * 6f);
            Color c = auraColor;
            c.a = 0.85f * edgeFade;
            lr.startColor = new Color(c.r, c.g, c.b, c.a * 0.25f); // 尾は薄く
            lr.endColor = c;
        }
    }

    private void SetActiveStreaks(int count)
    {
        if (_streaks == null) return;
        for (int i = 0; i < _streaks.Length; i++)
        {
            if (_streaks[i] == null) continue;
            bool shouldBeActive = i < count;
            if (_streaks[i].gameObject.activeSelf != shouldBeActive)
                _streaks[i].gameObject.SetActive(shouldBeActive);
        }
    }

    void OnDestroy()
    {
        if (_streaks == null) return;
        foreach (var lr in _streaks)
            if (lr != null) Destroy(lr.gameObject);
    }
}
