using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// グラス(XREAL One・光学シースルー)向けの HUD の見た目。PeripheralHUDManager が F-07 のレイアウトに使う。
///
/// <para><b>加算表示の制約</b>: グラスは光を足して見せるので、黒は透けて見えない。暗いパネル・影・黒い縁取りは
/// グラスでは消える。だから面を塗らず、細い線・小さなラベル・数値の淡い発光(TMP の Glow)で組む —
/// 面を明るく塗ると路面が見えにくくなり、電池も食う。</para>
///
/// <para>構成(F-07 の4ゾーンはそのまま): 左上 = TIME / DISTANCE・右上 = PACE と状態(ON PACE / BEHIND)・
/// 中央 = 空・下部 = F-10 の警告(赤い細いカプセルで点滅)。シーンは書き換えず、実行時に既存の
/// テキスト(時間・距離・ペース)をコーナーの枠へ移して装飾を足す。</para>
///
/// <para>数値は等幅(&lt;mspace&gt;)で、桁が変わっても左右に揺れない。フォントは Inter
/// (Resources/Fonts/HUD-Inter_SDF・OFL)。日本語(F-10 の警告)は端末のフォント(iOS はヒラギノ)を使う —
/// 同梱フォントには日本語が無く、以前は警告が描かれていなかった。</para>
/// </summary>
public sealed class PeripheralHudSkin
{
    // ── 色(加算表示で読める明るさ。ライムはアプリのアクセント arYellow と同じ)───────────
    public static readonly Color Ink = new Color(0.93f, 0.98f, 1.00f);          // 数値
    public static readonly Color Accent = new Color(0.82f, 0.95f, 0.10f);       // 枠線・ラベル
    public static readonly Color Neutral = new Color(0.62f, 0.68f, 0.72f);      // ペース不明
    public static readonly Color Alert = new Color(1.00f, 0.34f, 0.28f);        // F-10 警告

    private const float ValueSize = 76f, PaceSize = 88f, LabelSize = 22f, LabelSpacing = 24f;
    private const float Margin = 64f;                // 画面端(セーフエリアの内側)からの余白
    private const string Mono = "<mspace=0.6em>";    // 等幅数字(桁が変わっても揺れない)

    public RectTransform TopLeft { get; private set; }
    public RectTransform TopRight { get; private set; }
    public RectTransform Warning { get; private set; }

    private const float GaugeWidth = 260f;

    private CanvasGroup _topLeftGroup, _topRightGroup;
    private TextMeshProUGUI _paceStatus;
    private Image _paceDot;
    private RectTransform _gauge;
    private Image _gaugeMarker;
    private float _gaugeX;
    private Image _warningFrame;
    private TextMeshProUGUI _warningText;
    private TMP_FontAsset _font;
    private PaceHudDisplay.PaceState _shownState = (PaceHudDisplay.PaceState)(-1);

    /// <summary>F-10 の警告に使っている日本語フォント(開発者モードの診断用)。無ければ null</summary>
    public static string JapaneseFontName { get; private set; }

    // ── 組み立て ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 既存の3つの readout を枠へ移し、ラベル・線・状態表示・警告の器を足す。
    /// フォントが読めなければ null(従来の見た目のまま動く)
    /// </summary>
    public static PeripheralHudSkin Build(RectTransform canvas, TextMeshProUGUI time, TextMeshProUGUI distance,
                                          TextMeshProUGUI pace, TextMeshProUGUI splitAlert)
    {
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts/HUD-Inter_SDF");
        if (canvas == null || font == null || time == null || distance == null || pace == null) return null;

        var skin = new PeripheralHudSkin { _font = font };

        // 左上: TIME / DISTANCE
        skin.TopLeft = NewRect("HUD_TopLeft", canvas, new Vector2(0f, 1f), new Vector2(560f, 300f));
        skin._topLeftGroup = skin.TopLeft.gameObject.AddComponent<CanvasGroup>();
        skin._topLeftGroup.blocksRaycasts = false;
        Frame(skin.TopLeft, left: true, height: 250f);
        Label(skin.TopLeft, "TIME", left: true, y: -8f, font);
        Readout(time, skin.TopLeft, left: true, y: -34f, ValueSize, font);
        Label(skin.TopLeft, "DISTANCE", left: true, y: -136f, font);
        Readout(distance, skin.TopLeft, left: true, y: -162f, ValueSize, font);

        // 右上: PACE と状態
        skin.TopRight = NewRect("HUD_TopRight", canvas, new Vector2(1f, 1f), new Vector2(560f, 300f));
        skin._topRightGroup = skin.TopRight.gameObject.AddComponent<CanvasGroup>();
        skin._topRightGroup.blocksRaycasts = false;
        Frame(skin.TopRight, left: false, height: 246f);
        Label(skin.TopRight, "PACE  /KM", left: false, y: -8f, font);
        Readout(pace, skin.TopRight, left: false, y: -34f, PaceSize, font);
        skin._paceDot = Line(skin.TopRight, new Vector2(1f, 1f), new Vector2(-24f, -160f), new Vector2(11f, 11f), Neutral);
        skin._paceStatus = Text(skin.TopRight, "PaceStatus", font, LabelSize, Neutral, TextAlignmentOptions.TopRight,
                                new Vector2(1f, 1f), new Vector2(-46f, -154f), new Vector2(420f, 30f));
        skin._paceStatus.characterSpacing = LabelSpacing;
        skin._paceStatus.fontStyle = FontStyles.Bold;

        // ペースゲージ: 中央の目印 = 目標、右 = 速い・左 = 遅い(±30秒/km で端)。色だけでなく「どれだけ」ずれているか
        skin._gauge = NewRect("PaceGauge", skin.TopRight, new Vector2(1f, 1f), new Vector2(GaugeWidth, 22f));
        skin._gauge.anchoredPosition = new Vector2(-24f, -194f);
        Line(skin._gauge, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(GaugeWidth, 2f), new Color(Neutral.r, Neutral.g, Neutral.b, 0.55f));
        Line(skin._gauge, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(2f, 10f), new Color(Neutral.r, Neutral.g, Neutral.b, 0.7f));
        Line(skin._gauge, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(2f, 10f), new Color(Neutral.r, Neutral.g, Neutral.b, 0.7f));
        Line(skin._gauge, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2f, 18f), new Color(Ink.r, Ink.g, Ink.b, 0.9f));
        skin._gaugeMarker = Line(skin._gauge, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, 20f), Neutral);
        skin._gaugeMarker.enabled = false;
        GaugeLabel(skin._gauge, "SLOW", left: true, font);
        GaugeLabel(skin._gauge, "FAST", left: false, font);

        // 下部: F-10 の警告(平常時は空)
        skin.Warning = NewRect("HUD_Warning", canvas, new Vector2(0.5f, 0f), new Vector2(1120f, 88f));
        skin._warningFrame = skin.Warning.gameObject.AddComponent<Image>();
        skin._warningFrame.sprite = CapsuleOutlineSprite();
        skin._warningFrame.type = Image.Type.Sliced;
        skin._warningFrame.color = Alert;
        skin._warningFrame.raycastTarget = false;
        skin.Warning.gameObject.SetActive(false);

        // スプリット通知(上部中央)の書体
        if (splitAlert != null)
        {
            splitAlert.font = font;
            splitAlert.fontSize = 46f;
            splitAlert.fontStyle = FontStyles.Bold;
            splitAlert.richText = true;
            splitAlert.lineSpacing = -8f;
            splitAlert.rectTransform.sizeDelta = new Vector2(1000f, 140f);
        }

        skin.ApplyInset(Vector2.zero);
        return skin;
    }

    /// <summary>セーフエリアの余白(キャンバス単位)を反映する。グラス接続時だけ 0 以外</summary>
    public void ApplyInset(Vector2 inset)
    {
        TopLeft.anchoredPosition = new Vector2(Margin + inset.x, -(Margin * 0.8f + inset.y));
        TopRight.anchoredPosition = new Vector2(-(Margin + inset.x), -(Margin * 0.8f + inset.y));
        Warning.anchoredPosition = new Vector2(0f, 72f + inset.y);
    }

    /// <summary>
    /// 数値の淡い発光と書体の仕上げ。TMP のアウトライン設定(PeripheralHUDManager)の後に呼ぶ —
    /// どちらも同じマテリアルインスタンスを触る
    /// </summary>
    public void ApplyGlow(TextMeshProUGUI time, TextMeshProUGUI distance, TextMeshProUGUI pace, TextMeshProUGUI splitAlert)
    {
        Glow(time, Ink);
        Glow(distance, Ink);
        Glow(pace, Neutral);
        Glow(splitAlert, Accent);
    }

    // ── 毎フレームの更新 ───────────────────────────────────────────────────────

    /// <summary>"24:18"</summary>
    public static string FormatTime(int minutes, int seconds) => MonoDigits($"{minutes:00}:{seconds:00}");

    /// <summary>"2.35" + 小さい KM</summary>
    public static string FormatDistance(float km) =>
        MonoDigits(km.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)) +
        $"<size=42%><color=#{ColorUtility.ToHtmlStringRGB(Accent)}>  KM</color></size>";

    /// <summary>"4'37\""(単位はラベル側)。PaceHudDisplay.Format の "/km" を外す</summary>
    public static string FormatPace(string plainPace) =>
        MonoDigits(plainPace.EndsWith("/km") ? plainPace.Substring(0, plainPace.Length - 3) : plainPace);

    /// <summary>
    /// 数字の並びだけ等幅にする(桁が変わっても揺れない)。コロン・小数点・分秒の記号は本来の幅のまま —
    /// 全体を等幅にすると "00 : 01" のように記号の周りが空きすぎる
    /// </summary>
    public static string MonoDigits(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length + 32);
        bool inDigits = false;
        foreach (char ch in text)
        {
            bool digit = ch >= '0' && ch <= '9' || ch == '-';
            if (digit && !inDigits) { sb.Append(Mono); inDigits = true; }
            if (!digit && inDigits) { sb.Append("</mspace>"); inDigits = false; }
            sb.Append(ch);
        }
        if (inDigits) sb.Append("</mspace>");
        return sb.ToString();
    }

    /// <summary>ペースの状態表示(状態が変わったときだけマテリアルを触る)</summary>
    public void SetPaceState(PaceHudDisplay.PaceState state, Color color, TextMeshProUGUI pace)
    {
        if (state == _shownState) return;
        _shownState = state;
        _paceStatus.text = state == PaceHudDisplay.PaceState.Maintaining ? "ON PACE"
                         : state == PaceHudDisplay.PaceState.Behind ? "BEHIND"
                         : "NO PACE";
        _paceStatus.color = color;
        _paceDot.color = color;
        Glow(pace, color);
    }

    /// <summary>
    /// ペースゲージの針。<paramref name="position"/> は PaceHudDisplay.GaugePosition(-1〜+1、NaN = 不明で隠す)。
    /// 1秒ごとに跳ねる実測をそのまま追うと針が震えるので、フレーム時間に依存しない平滑化で寄せる
    /// </summary>
    public void SetGauge(float position, Color color, float deltaTime)
    {
        bool known = !float.IsNaN(position);
        if (_gaugeMarker.enabled != known) _gaugeMarker.enabled = known;
        if (!known) return;
        float target = position * (GaugeWidth * 0.5f - 4f);
        _gaugeX += (target - _gaugeX) * FrameSmoothing.Factor(deltaTime, 6f);
        _gaugeMarker.rectTransform.anchoredPosition = new Vector2(_gaugeX, 0f);
        _gaugeMarker.color = color;
    }

    /// <summary>首振り中の減光(F-07 の readout と一緒に枠・ラベルも薄くする)</summary>
    public void SetAlpha(float alpha)
    {
        _topLeftGroup.alpha = alpha;
        _topRightGroup.alpha = alpha;
    }

    /// <summary>Unity の HUD を出すか(iPhone 表示中は SwiftUI が持つので隠す)</summary>
    public void SetVisible(bool visible)
    {
        if (TopLeft.gameObject.activeSelf != visible) TopLeft.gameObject.SetActive(visible);
        if (TopRight.gameObject.activeSelf != visible) TopRight.gameObject.SetActive(visible);
        if (!visible && Warning.gameObject.activeSelf) Warning.gameObject.SetActive(false);
    }

    /// <summary>警告の文字を載せる器(PeripheralHUDManager が警告ラベルをここへ作る)</summary>
    public RectTransform WarningContainer => Warning;

    /// <summary>警告の表示と点滅(1Hz・明るさ 55〜100%)。文字自体は消さない</summary>
    public void SetWarning(bool show, float time, TextMeshProUGUI warningText)
    {
        if (Warning.gameObject.activeSelf != show) Warning.gameObject.SetActive(show);
        if (!show) return;
        float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Cos(time * Mathf.PI * 2f));
        _warningFrame.color = new Color(Alert.r, Alert.g, Alert.b, pulse);
        if (warningText != null && _warningText == null) _warningText = warningText;
    }

    /// <summary>
    /// F-10 の警告用の日本語フォント。同梱の Inter / LiberationSans には日本語が無い。
    /// 端末のフォントから作る(iOS はヒラギノ・Windows のエディタは游ゴシック/メイリオ)。無ければ null
    /// </summary>
    public static TMP_FontAsset LoadJapaneseFont()
    {
        string[][] candidates =
        {
            new[] { "Hiragino Sans", "W6" }, new[] { "Hiragino Sans", "W3" },
            new[] { "Hiragino Kaku Gothic ProN", "W6" }, new[] { "Hiragino Kaku Gothic ProN", "W3" },
            new[] { "Yu Gothic UI", "Bold" }, new[] { "Yu Gothic", "Bold" }, new[] { "Meiryo", "Bold" },
            new[] { "Meiryo", "Regular" }, new[] { "Noto Sans CJK JP", "Bold" }, new[] { "Noto Sans JP", "Bold" },
        };
        foreach (string[] c in candidates)
        {
            try
            {
                TMP_FontAsset f = TMP_FontAsset.CreateFontAsset(c[0], c[1]);
                if (f != null && f.HasCharacter('探', tryAddCharacter: true))
                {
                    JapaneseFontName = $"{c[0]} {c[1]}";
                    Debug.Log($"[HUD] 日本語フォント: {JapaneseFontName}");
                    return f;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[HUD] 日本語フォント {c[0]} {c[1]} を読めない: {e.GetType().Name}");
            }
        }
        JapaneseFontName = null;
        Debug.LogWarning("[HUD] 日本語フォントが見つからない — F-10 の警告は英語で出す");
        return null;
    }

    // ── 部品 ───────────────────────────────────────────────────────────────────

    private static RectTransform NewRect(string name, RectTransform parent, Vector2 anchor, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = size;
        return rt;
    }

    /// <summary>ブロックの縁: 縦の細線と、上下の短い横線(角の括弧)</summary>
    private static void Frame(RectTransform block, bool left, float height)
    {
        Vector2 a = left ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        float sx = left ? 1f : -1f;
        Line(block, a, Vector2.zero, new Vector2(3f, height), new Color(Accent.r, Accent.g, Accent.b, 0.85f));
        Line(block, a, Vector2.zero, new Vector2(34f, 3f), Accent);
        Line(block, a, new Vector2(0f, -height + 3f), new Vector2(14f, 3f), new Color(Accent.r, Accent.g, Accent.b, 0.85f));
        // 角の内側の小さな点(計器らしさ)
        Line(block, a, new Vector2(sx * 42f, 0f), new Vector2(5f, 3f), new Color(Accent.r, Accent.g, Accent.b, 0.6f));
    }

    private static Image Line(RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
    {
        RectTransform rt = NewRect("Line", parent, anchor, size);
        rt.anchoredPosition = pos;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static void Label(RectTransform block, string text, bool left, float y, TMP_FontAsset font)
    {
        TextMeshProUGUI t = Text(block, "Label_" + text, font, LabelSize, new Color(Accent.r, Accent.g, Accent.b, 0.9f),
                                 left ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight,
                                 left ? new Vector2(0f, 1f) : new Vector2(1f, 1f),
                                 new Vector2(left ? 22f : -22f, y), new Vector2(480f, 30f));
        t.text = text;
        t.characterSpacing = LabelSpacing;
        t.fontStyle = FontStyles.Bold;
    }

    private static void GaugeLabel(RectTransform gauge, string text, bool left, TMP_FontAsset font)
    {
        Vector2 a = left ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);
        TextMeshProUGUI t = Text(gauge, "Gauge_" + text, font, 15f, new Color(Neutral.r, Neutral.g, Neutral.b, 0.8f),
                                 left ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight,
                                 a, new Vector2(0f, -14f), new Vector2(120f, 22f));
        t.rectTransform.pivot = left ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        t.text = text;
        t.characterSpacing = LabelSpacing;
        t.fontStyle = FontStyles.Bold;
    }

    /// <summary>シーンの readout を枠へ移して書体を揃える(アンカーは枠の同じ隅 — E2E が確かめる)</summary>
    private static void Readout(TextMeshProUGUI text, RectTransform block, bool left, float y, float size, TMP_FontAsset font)
    {
        RectTransform rt = text.rectTransform;
        rt.SetParent(block, false);
        Vector2 a = left ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        rt.anchorMin = a;
        rt.anchorMax = a;
        rt.pivot = a;
        rt.anchoredPosition = new Vector2(left ? 18f : -18f, y);
        rt.sizeDelta = new Vector2(540f, size + 16f);
        rt.localScale = Vector3.one;
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.richText = true;
        text.enableWordWrapping = false;
        text.alignment = left ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight;
        text.color = Ink;
    }

    private static TextMeshProUGUI Text(RectTransform parent, string name, TMP_FontAsset font, float size, Color color,
                                        TextAlignmentOptions align, Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
    {
        RectTransform rt = NewRect(name, parent, anchor, sizeDelta);
        rt.anchoredPosition = pos;
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>
    /// 数値の淡い発光(TMP の Glow)。加算表示では光の滲みとして見え、明るい路面でも輪郭が立つ。
    /// Glow に対応しないシェーダ(モバイル版)のときは何もしない。装飾なので失敗しても止めない
    /// </summary>
    public static void Glow(TextMeshProUGUI text, Color color)
    {
        if (text == null) return;
        try
        {
            Material m = text.fontMaterial;
            if (m == null || !m.HasProperty(ShaderUtilities.ID_GlowColor)) return;
            m.EnableKeyword(ShaderUtilities.Keyword_Glow);
            m.SetColor(ShaderUtilities.ID_GlowColor, new Color(color.r, color.g, color.b, 0.6f));
            m.SetFloat(ShaderUtilities.ID_GlowOffset, 0f);
            m.SetFloat(ShaderUtilities.ID_GlowInner, 0.08f);
            m.SetFloat(ShaderUtilities.ID_GlowOuter, 0.42f);
            m.SetFloat(ShaderUtilities.ID_GlowPower, 0.45f);
            text.UpdateMeshPadding();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[HUD] 発光の設定をスキップ ({text.name}): {e.GetType().Name}");
        }
    }

    /// <summary>角の丸い細い枠(9スライス)。警告のカプセルに使う。外部の画像に頼らず実行時に描く</summary>
    private static Sprite CapsuleOutlineSprite()
    {
        const int size = 64, radius = 30;
        const float thickness = 3.2f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // 角丸四角の縁までの距離(内側が負)
            float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
            float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - (radius - 1f);
            float edge = 1f - Mathf.Clamp01(Mathf.Abs(d + thickness * 0.5f) - thickness * 0.5f + 0.5f);
            float fill = d < 0f ? 0.10f : 0f;     // ごく淡い内側(加算表示では赤い霞)
            byte a = (byte)(Mathf.Clamp01(Mathf.Max(edge, fill)) * 255f);
            px[y * size + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                             SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }
}
