using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ARグラスへ出している走行の終わり方(タスク表 ARグラス「ランニング終了」2026-10-19)。
/// <list type="number">
/// <item>アバターがランナーへ挨拶して粒子になって消える(<see cref="AvatarVFXController"/> の終了演出)</item>
/// <item>視界中央にシンプルな結果カード(獲得ランク・シンクロ率・距離・タイム)を数秒</item>
/// <item>カードと HUD を消してグラスの表示を OFF にする(加算表示なので黒 = 何も見えない)。
///   詳しい結果は iPhone の結果画面(S7)で見る</item>
/// </list>
/// iPhone 画面で走っているときは出さない(Swift の結果画面が覆う)。展示デモでも出さない(結果が無い)。
/// Swift は終了からこの合計秒数だけ待ってから Unity を止める(UnityBridge 側の GlassFarewell)。
/// Bootstrap が自動生成する。
/// </summary>
public sealed class GlassRunSummaryCard : MonoBehaviour
{
    /// <summary>挨拶(1.5秒)と消滅(0.8秒)を待つ時間。カードはアバターが消えてから出す</summary>
    public const float FarewellSeconds = 2.4f;

    /// <summary>カードを出しておく時間</summary>
    public const float CardSeconds = 4.0f;

    private const float FadeSeconds = 0.35f;

    /// <summary>終了からグラスの表示を OFF にするまでの合計秒数(Swift はこれより少し長く待って Unity を止める)</summary>
    public const float TotalSeconds = FarewellSeconds + CardSeconds + FadeSeconds;

    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private RunSessionController sessionController;
    [SerializeField] private GlassViewRig glassRig;
    [SerializeField] private PeripheralHUDManager hud;

    private bool _prevEnded;
    private Coroutine _sequence;
    private Canvas _canvas;
    private CanvasGroup _group;
    private TextMeshProUGUI _rankText;
    private TextMeshProUGUI _bodyText;
    private bool _hudTurnedOff;

    // ── 検証用の公開状態 ─────────────────────────────────────────────
    public bool IsCardVisible => _group != null && _group.alpha > 0.01f;
    /// <summary>カードを出し終えてグラスの表示を OFF にしたか(次の走行の開始で戻る)</summary>
    public bool IsGlassDisplayOff => _hudTurnedOff;
    public string LastRankText { get; private set; } = "";
    public string LastBodyText { get; private set; } = "";

    void Start()
    {
        if (avatarEngine == null) avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (sessionController == null) sessionController = FindFirstObjectByType<RunSessionController>(FindObjectsInactive.Include);
        if (glassRig == null) glassRig = FindFirstObjectByType<GlassViewRig>(FindObjectsInactive.Include);
        if (hud == null) hud = FindFirstObjectByType<PeripheralHUDManager>(FindObjectsInactive.Include);
    }

    void Update()
    {
        if (avatarEngine == null) return;

        bool ended = avatarEngine.IsSessionEnded;
        if (ended && !_prevEnded) OnRunEnded();
        // 次の走行が始まった(ResetSession で IsSessionEnded が戻った)らカードを片付け、HUD を戻す
        if (!ended && _prevEnded) ResetSession();
        _prevEnded = ended;
    }

    private void OnRunEnded()
    {
        if (glassRig == null || !glassRig.IsGlassOutputActive) return;     // iPhone 画面の走行
        if (sessionController == null || sessionController.SuppressPersistence) return; // 展示デモ
        RunSessionRecord record = sessionController.LastRecord;
        if (record == null) return;

        if (_sequence != null) StopCoroutine(_sequence);
        _sequence = StartCoroutine(PlaySequence(record));
    }

    private IEnumerator PlaySequence(RunSessionRecord record)
    {
        EnsureCard();
        bool japanese = PeripheralHudSkin.JapaneseFontName != null;
        LastRankText = RunSummaryText.Rank(record.rankLabel);
        LastBodyText = RunSummaryText.Body(record.averageSyncRate, record.distanceMeters, record.elapsedSeconds, japanese);
        _rankText.text = LastRankText;
        _bodyText.text = LastBodyText;

        // アバターの挨拶と消滅を見届けてから
        yield return new WaitForSeconds(FarewellSeconds);

        yield return Fade(0f, 1f);
        yield return new WaitForSeconds(CardSeconds - FadeSeconds);
        yield return Fade(1f, 0f);

        // グラスの表示を OFF(HUD も消す)。走行の詳しい結果は iPhone の結果画面で
        if (hud != null && hud.IsHudVisible) hud.SetHudVisible(false);
        _hudTurnedOff = true;
        _sequence = null;
        Debug.Log("[GLASS] 走行の結果カードを出し終え、グラスの表示を消しました(詳しい結果は iPhone で)。");
    }

    private IEnumerator Fade(float from, float to)
    {
        float t = 0f;
        while (t < FadeSeconds)
        {
            t += Time.deltaTime;
            if (_group != null) _group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / FadeSeconds));
            yield return null;
        }
        if (_group != null) _group.alpha = to;
    }

    /// <summary>次の走行の開始で片付ける。HUD の表示は StartSession が決め直す</summary>
    public void ResetSession()
    {
        if (_sequence != null) StopCoroutine(_sequence);
        _sequence = null;
        if (_group != null) _group.alpha = 0f;
        _hudTurnedOff = false;
    }

    // ── カード(実行時生成) ─────────────────────────────────────────────

    private void EnsureCard()
    {
        if (_canvas != null) return;

        var canvasGo = new GameObject("GlassRunSummaryCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 500; // HUD より手前
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _group = canvasGo.GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        // 背景は暗い色 = グラスでは透ける。文字だけが浮かぶ控えめなカードにする(誇張しない)
        var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(canvasGo.transform, false);
        var rt = card.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(760f, 440f);
        var image = card.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.55f);

        var outline = card.AddComponent<Outline>();
        outline.effectColor = PeripheralHudSkin.Accent * new Color(1f, 1f, 1f, 0.6f);
        outline.effectDistance = new Vector2(2f, -2f);

        TMP_FontAsset japaneseFont = PeripheralHudSkin.JapaneseFontName != null ? PeripheralHudSkin.LoadJapaneseFont() : null;
        _rankText = CreateText(card.transform, "Rank", new Vector2(0f, 120f), 84f, PeripheralHudSkin.Accent, japaneseFont);
        _bodyText = CreateText(card.transform, "Body", new Vector2(0f, -60f), 46f, PeripheralHudSkin.Ink, japaneseFont);
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, Vector2 position, float size,
                                             Color color, TMP_FontAsset font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(700f, size * 4f);
        var text = go.GetComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }
}
