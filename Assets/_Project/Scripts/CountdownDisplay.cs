using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 走行開始カウントダウンのAR表示。
///
/// 音のカウントダウン(<see cref="RunAudioEngine"/> の PlayStartSignal — 1秒間隔の
/// ビープ3回 + START)と同期する。ここでは HUD のオーバーレイでは
/// なく、**ワールド空間に置いた実体のテキスト**として前方に出す。グラス越しに
/// 「その場に浮かんでいる」ように見え、アバターと同じ空間にあるものとして読める。
///
/// 実行時生成のTextMeshPro(3D)なのでアセット・シーン配線とも不要。
/// 走り出す前の4秒間だけユーザー正面へ追従し、見回しても数字を見失わない。
/// </summary>
public class CountdownDisplay : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private Transform userCamera;
    [SerializeField] private AvatarEngine avatarEngine;

    [Header("Placement")]
    [Tooltip("ユーザー前方どれだけの距離に出すか(m)。アバターの3.0mより手前へ置く")]
    [SerializeField] private float distanceMeters = 2.0f;
    [Tooltip("目線からの高さオフセット(m)")]
    [SerializeField] private float heightOffsetMeters = 0.1f;
    [Tooltip("文字の物理的な高さ(m)。2.2m先で読める大きさ")]
    [SerializeField] private float characterHeightMeters = 0.62f;
    [Tooltip("START!を含む文字列の最大ワールド幅(m)")]
    [SerializeField] private float maximumTextWidthMeters = 2.8f;

    [Header("Timing")]
    [Tooltip("1カウントの間隔(秒)。RunAudioEngineのビープ間隔と一致させること")]
    [SerializeField] private float stepSeconds = 1.0f;
    [Tooltip("カウント開始値。3 なら 3→2→1→START!")]
    [SerializeField] private int countFrom = 3;
    [Tooltip("START!表示を残す時間(秒)")]
    [SerializeField] private float goHoldSeconds = 1.0f;

    [Header("Appearance")]
    [SerializeField] private Color countColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color goColor = new Color(0.30f, 0.92f, 0.45f);
    [SerializeField] private Color accentColor = new Color(0.05f, 0.78f, 1f, 1f);

    private const string GoText = "START!";

    private Transform _visualRoot;
    private TextMeshPro _label;
    private TextMeshPro _shadowLabel;
    private LineRenderer _accentRing;
    private Material _ringMaterial;
    private Vector3 _labelBaseScale = Vector3.one;
    private bool _sequencePlayed;
    private bool _sequenceCompleted;
    private Coroutine _routine;

    /// <summary>E2E/検証用: カウントダウンが表示中か。</summary>
    public bool IsShowing => _visualRoot != null && _visualRoot.gameObject.activeSelf;

    /// <summary>E2E/検証用: 現在の表示文字列("3"/"2"/"1"/"START!"、非表示時は空)。</summary>
    public string CurrentText => IsShowing ? _label.text : string.Empty;

    /// <summary>
    /// True only after the complete 3-2-1-START presentation has finished.
    /// AvatarEngine uses this as the authoritative transition from an armed
    /// session to actual running motion.
    /// </summary>
    public bool HasCompleted => _sequenceCompleted;

    void Awake()
    {
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (userCamera == null && Camera.main != null)
            userCamera = Camera.main.transform;
    }

    void Update()
    {
        if (avatarEngine == null || userCamera == null) return;

        // 走行開始と同時に1回だけ走らせる(音のカウントダウンと同じトリガ)
        if (avatarEngine.HasStarted && !_sequencePlayed)
        {
            _sequencePlayed = true;
            _sequenceCompleted = false;
            _routine = StartCoroutine(RunSequence());
        }

        // スタート前の短い案内は常に視界中央へ置く。走行中のアバター方位とは独立し、
        // カウント終了後すぐ消えるため、頭を動かしても数字を見失わない。
        if (IsShowing)
            PlaceInFrontOfUser();
    }

    /// <summary>再走行対応: もう一度カウントダウンできるようにする。</summary>
    public void ResetSession()
    {
        _sequencePlayed = false;
        _sequenceCompleted = false;
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
        if (_visualRoot != null)
            _visualRoot.gameObject.SetActive(false);
    }

    private IEnumerator RunSequence()
    {
        EnsureVisual();
        if (_label == null)
        {
            // Do not leave the pacing engine permanently armed if TextMeshPro
            // cannot be created in a damaged or stripped build.
            _sequenceCompleted = true;
            yield break;
        }

        PlaceInFrontOfUser();
        _visualRoot.gameObject.SetActive(true);

        for (int n = countFrom; n >= 1; n--)
            yield return AnimateStep(n.ToString(), countColor, stepSeconds);

        yield return AnimateStep(GoText, goColor, goHoldSeconds);

        _visualRoot.gameObject.SetActive(false);
        _sequenceCompleted = true;
        _routine = null;
    }

    /// <summary>カウント開始時のユーザー前方(水平方向)へ配置する。</summary>
    private void PlaceInFrontOfUser()
    {
        Vector3 forward = userCamera.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        _visualRoot.position = userCamera.position
                             + forward * distanceMeters
                             + Vector3.up * heightOffsetMeters;
        FaceUser();
    }

    private void FaceUser()
    {
        Vector3 toUser = _visualRoot.position - userCamera.position;
        toUser.y = 0f;
        if (toUser.sqrMagnitude < 0.0001f) return;
        _visualRoot.rotation = Quaternion.LookRotation(toUser.normalized, Vector3.up);
    }

    private IEnumerator AnimateStep(string text, Color color, float duration)
    {
        SetStepText(text, color);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(duration, 0.01f));

            // ゲームらしい軽いポップ。巨大化させず、最初の20%だけオーバーシュート。
            float scale = t < 0.2f
                ? Mathf.Lerp(0.62f, 1.12f, Mathf.SmoothStep(0f, 1f, t / 0.2f))
                : Mathf.Lerp(1.12f, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.2f) / 0.8f));
            float alpha = t < 0.82f ? 1f : 1f - Mathf.InverseLerp(0.82f, 1f, t);

            _label.transform.localScale = _labelBaseScale * scale;
            _shadowLabel.transform.localScale = _labelBaseScale * scale;
            _label.color = WithAlpha(color, alpha);
            _shadowLabel.color = new Color(0f, 0f, 0f, 0.78f * alpha);

            if (_accentRing != null)
            {
                Color ringColor = WithAlpha(text == GoText ? goColor : accentColor,
                                            (0.35f + 0.65f * (1f - t)) * alpha);
                _accentRing.startColor = ringColor;
                _accentRing.endColor = ringColor;
                _accentRing.transform.localScale = Vector3.one * Mathf.Lerp(0.78f, 1.08f, t);
            }

            yield return null;
        }
    }

    private void ConfigureText(TextMeshPro text, Color color)
    {
        if (text.font == null && TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 5f;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.color = color;
        text.renderer.sortingOrder = 2;
    }

    private void SetStepText(string text, Color color)
    {
        _label.text = text;
        _shadowLabel.text = text;
        _label.color = color;

        Vector2 preferred = _label.font != null
            ? _label.GetPreferredValues(text, Mathf.Infinity, Mathf.Infinity)
            : new Vector2(Mathf.Max(6f, text.Length * 3f), 6f);
        preferred.x = Mathf.Max(preferred.x, 0.01f);
        preferred.y = Mathf.Max(preferred.y, 0.01f);
        float fittedScale = Mathf.Min(maximumTextWidthMeters / preferred.x,
                                      characterHeightMeters / preferred.y);

        _label.rectTransform.sizeDelta = preferred;
        _shadowLabel.rectTransform.sizeDelta = preferred;
        _labelBaseScale = Vector3.one * fittedScale;
        _label.transform.localScale = _labelBaseScale;
        _shadowLabel.transform.localScale = _labelBaseScale;
    }

    private void BuildAccentRing()
    {
        var ringGo = new GameObject("CountdownAccentRing");
        ringGo.transform.SetParent(_visualRoot, false);
        ringGo.transform.localPosition = new Vector3(0f, 0f, 0.04f);

        _accentRing = ringGo.AddComponent<LineRenderer>();
        _accentRing.useWorldSpace = false;
        _accentRing.loop = true;
        _accentRing.positionCount = 64;
        _accentRing.startWidth = 0.025f;
        _accentRing.endWidth = 0.025f;
        _accentRing.numCornerVertices = 3;
        _accentRing.sortingOrder = 1;

        Shader shader = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (shader != null)
        {
            _ringMaterial = new Material(shader);
            _accentRing.sharedMaterial = _ringMaterial;
        }

        for (int i = 0; i < _accentRing.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / _accentRing.positionCount;
            _accentRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * 1.7f,
                                                    Mathf.Sin(angle) * 0.62f,
                                                    0f));
        }
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    void OnDestroy()
    {
        if (_ringMaterial != null)
            Destroy(_ringMaterial);
    }

    private void EnsureVisual()
    {
        if (_label != null) return;

        var rootGo = new GameObject("CountdownVisual");
        rootGo.transform.SetParent(null);
        _visualRoot = rootGo.transform;

        var shadowGo = new GameObject("CountdownShadow");
        shadowGo.transform.SetParent(_visualRoot, false);
        shadowGo.transform.localPosition = new Vector3(0.045f, -0.045f, 0.025f);
        _shadowLabel = shadowGo.AddComponent<TextMeshPro>();

        var go = new GameObject("CountdownLabel");
        go.transform.SetParent(_visualRoot, false);

        _label = go.AddComponent<TextMeshPro>();
        ConfigureText(_label, countColor);
        ConfigureText(_shadowLabel, new Color(0f, 0f, 0f, 0.8f));

        BuildAccentRing();
        SetStepText(countFrom.ToString(), countColor);

        // 明るい屋外でも視認できるよう輪郭を付ける。
        // フォント未解決のまま fontMaterial / outline 系へ触ると例外になるため先に確認する
        if (_label.font != null && _label.fontSharedMaterial != null)
        {
            _label.fontMaterial.EnableKeyword("OUTLINE_ON");
            _label.outlineWidth = 0.2f;
            _label.outlineColor = new Color32(0, 0, 0, 255);
        }

        rootGo.SetActive(false);
    }
}
