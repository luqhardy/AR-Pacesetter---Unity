using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Pre-run setup UI: user enters target pace, then taps Start before pacing begins.
/// Builds a simple panel at runtime if one is not assigned in the Inspector.
/// </summary>
public class PaceCalibrationController : MonoBehaviour
{
    private const float MinPaceMinutesPerKm = 3.5f;
    private const float MaxPaceMinutesPerKm = 7.0f;
    private const string DefaultPaceText = "5:00";

    [Header("Engine Reference")]
    [SerializeField] private AvatarEngine avatarEngine;

    [Header("UI (optional — built at runtime if empty)")]
    [SerializeField] private GameObject setupPanel;
    [SerializeField] private TMP_InputField paceInputField;
    [SerializeField] private TextMeshProUGUI paceHintLabel;
    [SerializeField] private TextMeshProUGUI validationLabel;
    [SerializeField] private Button startButton;

    [Header("Legacy slider support")]
    [SerializeField] private Slider paceSlider;
    [SerializeField] private TextMeshProUGUI paceDisplayLabel;

    [Header("Setup")]
    [SerializeField] private bool buildSetupPanelAtRuntime = true;

    [Header("Ready Check / Session (auto-found if empty)")]
    [SerializeField] private ReadyCheckController readyCheck;
    [SerializeField] private RunSessionController sessionController;

    private float _selectedPaceMinutesPerKm = 5.0f;
    private bool _hasStarted;

    // Onboarding fields (企画書 §5 — 身体情報の初期入力)
    private TMP_InputField _heightInputField;
    private TMP_InputField _weightInputField;
    private TextMeshProUGUI _genderButtonLabel;
    private string _selectedGender = "Other";

    void Awake()
    {
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (readyCheck == null)
            readyCheck = FindFirstObjectByType<ReadyCheckController>(FindObjectsInactive.Include);
        if (sessionController == null)
            sessionController = FindFirstObjectByType<RunSessionController>(FindObjectsInactive.Include);
    }

    void Start()
    {
#if UNITY_IOS && !UNITY_EDITOR
        // 実機(UaaL)ではSwift UIが設定画面を持つ — Unity側のセットアップUIは
        // グラス視界に一瞬映るのを防ぐため最初から構築しない
        buildSetupPanelAtRuntime = false;
#endif
        if (buildSetupPanelAtRuntime && setupPanel == null)
            BuildSetupPanel();

        EnsureCanvasVisible();

        if (paceInputField != null)
        {
            paceInputField.text = DefaultPaceText;
            paceInputField.onEndEdit.AddListener(OnPaceInputChanged);
            OnPaceInputChanged(paceInputField.text);
        }

        if (paceSlider != null)
        {
            paceSlider.minValue = MinPaceMinutesPerKm;
            paceSlider.maxValue = MaxPaceMinutesPerKm;
            paceSlider.onValueChanged.AddListener(OnPaceSliderMoved);
            paceSlider.value = _selectedPaceMinutesPerKm;
            OnPaceSliderMoved(paceSlider.value);
        }

        if (startButton != null)
        {
            startButton.onClick.RemoveListener(StartRunning);
            startButton.onClick.AddListener(StartRunning);
        }

        if (avatarEngine == null)
            SetValidationMessage("Avatar not found in scene.", true);
    }

    public void OnPaceSliderMoved(float sliderValue)
    {
        ApplyPace(sliderValue);

        if (paceDisplayLabel != null)
            paceDisplayLabel.text = $"Target Pace: {FormatPace(sliderValue)}";

        if (paceInputField != null)
            paceInputField.SetTextWithoutNotify(FormatPaceInput(sliderValue));
    }

    public void OnPaceInputChanged(string rawInput)
    {
        if (TryParsePace(rawInput, out float pace))
        {
            ApplyPace(pace);
            SetValidationMessage($"Target: {FormatPace(pace)}", false);

            if (paceSlider != null)
                paceSlider.SetValueWithoutNotify(pace);

            if (paceDisplayLabel != null)
                paceDisplayLabel.text = $"Target Pace: {FormatPace(pace)}";
        }
        else
        {
            SetValidationMessage("Enter pace as M:SS (e.g. 5:30) or decimal (e.g. 5.5). Range: 3:30–7:00 /km.", true);
        }
    }

    /// <summary>Swiftブリッジ用: SwiftのUIが設定画面を持つ場合、Unity側のセットアップUIを閉じる。</summary>
    public void HideSetupUiForExternalControl()
    {
        _hasStarted = true; // suppress the local START RUN flow
        if (setupPanel != null) setupPanel.SetActive(false);
        if (paceSlider != null) paceSlider.gameObject.SetActive(false);
        if (paceDisplayLabel != null) paceDisplayLabel.gameObject.SetActive(false);
    }

    /// <summary>Hybrid input (企画書 §5): +/- buttons adjust pace by ±5 seconds.</summary>
    public void AdjustPaceSeconds(int deltaSeconds)
    {
        float newPace = Mathf.Clamp(_selectedPaceMinutesPerKm + deltaSeconds / 60f,
            MinPaceMinutesPerKm, MaxPaceMinutesPerKm);
        ApplyPace(newPace);

        if (paceInputField != null)
            paceInputField.SetTextWithoutNotify(FormatPaceInput(newPace));
        if (paceSlider != null)
            paceSlider.SetValueWithoutNotify(newPace);

        SetValidationMessage($"Target: {FormatPace(newPace)}", false);
    }

    public void StartRunning()
    {
        if (_hasStarted) return;

        // Ready check gate (企画書 §5): required devices must be connected
        if (readyCheck != null && !readyCheck.AllRequiredReady)
        {
            SetValidationMessage("AR Glass not connected — see READY CHECK (F1 to simulate).", true);
            return;
        }

        string paceText = paceInputField != null ? paceInputField.text : DefaultPaceText;
        if (!TryParsePace(paceText, out float pace))
        {
            SetValidationMessage("Invalid pace. Use M:SS between 3:30 and 7:00 per km.", true);
            return;
        }

        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);

        if (avatarEngine == null)
        {
            SetValidationMessage("Cannot start — AvatarEngine is missing.", true);
            return;
        }

        SaveUserProfile();

        // Unity単体走行: Swift主導フラグを確実に解除(スプリット供給を有効化)
        ARSessionManagerBridge.ExternalMetricsActive = false;

        ApplyPace(pace);
        avatarEngine.StartPacing();
        _hasStarted = true;

        // Hand off to session controller: sleep lock + guard layer + finish flow
        if (sessionController != null)
            sessionController.OnRunStarted();

        if (setupPanel != null)
            setupPanel.SetActive(false);

        if (paceSlider != null)
            paceSlider.gameObject.SetActive(false);

        if (paceDisplayLabel != null)
            paceDisplayLabel.gameObject.SetActive(false);
    }

    private void ApplyPace(float paceMinutesPerKm)
    {
        _selectedPaceMinutesPerKm = Mathf.Clamp(paceMinutesPerKm, MinPaceMinutesPerKm, MaxPaceMinutesPerKm);
        if (avatarEngine != null)
            avatarEngine.UpdateTargetPace(_selectedPaceMinutesPerKm);
    }

    private void BuildSetupPanel()
    {
        Canvas canvas = FindCanvas();
        if (canvas == null)
        {
            Debug.LogError("[UI] No Canvas found — cannot build run setup panel.");
            return;
        }

        EnsureCanvasVisible(canvas);

        if (paceSlider != null)
            paceSlider.gameObject.SetActive(false);

        if (paceDisplayLabel != null)
            paceDisplayLabel.gameObject.SetActive(false);

        setupPanel = CreatePanel(canvas.transform, "RunSetupPanel",
            new Color(0.04f, 0.06f, 0.1f, 0.92f));

        RectTransform panelRt = setupPanel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;

        RectTransform card = CreateCard(setupPanel.transform);

        CreateLabel(card, "AR Pacesetter", 34, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(420f, 48f));

        CreateLabel(card, "Set your target pace, then start the run.", 18, FontStyles.Normal,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(420f, 32f),
            new Color(0.75f, 0.82f, 0.9f, 1f));

        CreateLabel(card, "Pace (min/km)", 16, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -128f), new Vector2(420f, 28f),
            new Color(0.6f, 0.75f, 0.95f, 1f));

        paceInputField = CreateInputField(card, new Vector2(0f, -168f));
        paceInputField.text = DefaultPaceText;

        // Hybrid input (企画書 §5): +/- buttons for 5-second fine adjustment
        CreateSmallButton(card, "−5s", new Vector2(-165f, -168f), () => AdjustPaceSeconds(-5));
        CreateSmallButton(card, "+5s", new Vector2(165f, -168f), () => AdjustPaceSeconds(+5));

        paceHintLabel = CreateLabel(card, "Examples: 5:00, 5:30, 6:15  •  Range: 3:30 – 7:00 /km", 14,
            FontStyles.Italic, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -228f),
            new Vector2(420f, 28f), new Color(0.55f, 0.65f, 0.78f, 1f));

        BuildProfileRow(card);

        validationLabel = CreateLabel(card, "", 14, FontStyles.Normal,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -344f), new Vector2(420f, 28f),
            new Color(1f, 0.55f, 0.45f, 1f));

        startButton = CreateStartButton(card, new Vector2(0f, -384f));
        startButton.onClick.AddListener(StartRunning);

        OnPaceInputChanged(DefaultPaceText);
    }

    // ── Onboarding row (企画書 §5 — 身長・体重・性別の初期入力) ─────────────────

    private void BuildProfileRow(RectTransform card)
    {
        CreateLabel(card, "Runner Profile — Height (cm) / Weight (kg) / Gender", 14, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -262f), new Vector2(440f, 24f),
            new Color(0.6f, 0.75f, 0.95f, 1f));

        _heightInputField = CreateInputField(card, new Vector2(-150f, -292f));
        ResizeInputField(_heightInputField, new Vector2(120f, 44f), 20);
        _heightInputField.text = UserProfile.HeightCm.ToString("F0");

        _weightInputField = CreateInputField(card, new Vector2(0f, -292f));
        ResizeInputField(_weightInputField, new Vector2(120f, 44f), 20);
        _weightInputField.text = UserProfile.WeightKg.ToString("F0");

        _selectedGender = UserProfile.Gender;
        Button genderButton = CreateSmallButton(card, _selectedGender, new Vector2(150f, -292f), CycleGender);
        genderButton.GetComponent<RectTransform>().sizeDelta = new Vector2(120f, 44f);
        _genderButtonLabel = genderButton.GetComponentInChildren<TextMeshProUGUI>();
    }

    private void CycleGender()
    {
        _selectedGender = _selectedGender switch
        {
            "Male" => "Female",
            "Female" => "Other",
            _ => "Male"
        };
        if (_genderButtonLabel != null)
            _genderButtonLabel.text = _selectedGender;
    }

    private void SaveUserProfile()
    {
        if (_heightInputField != null && float.TryParse(_heightInputField.text, out float height))
            UserProfile.HeightCm = height;
        if (_weightInputField != null && float.TryParse(_weightInputField.text, out float weight))
            UserProfile.WeightKg = weight;
        UserProfile.Gender = _selectedGender;
        UserProfile.Save();
    }

    private static void ResizeInputField(TMP_InputField field, Vector2 size, int fontSize)
    {
        field.GetComponent<RectTransform>().sizeDelta = size;
        if (field.textComponent is TextMeshProUGUI text) text.fontSize = fontSize;
        if (field.placeholder is TextMeshProUGUI ph) ph.fontSize = fontSize;
    }

    private void EnsureCanvasVisible()
    {
        Canvas canvas = FindCanvas();
        if (canvas != null)
            EnsureCanvasVisible(canvas);
    }

    private static void EnsureCanvasVisible(Canvas canvas)
    {
        if (canvas.transform.localScale == Vector3.zero)
            canvas.transform.localScale = Vector3.one;
    }

    private Canvas FindCanvas()
    {
        GameObject hud = GameObject.Find("HUD_Canvas") ?? GameObject.Find("Canvas");
        if (hud != null)
        {
            Canvas c = hud.GetComponent<Canvas>();
            if (c != null) return c;
        }

        return FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
    }

    private void SetValidationMessage(string message, bool isError)
    {
        if (validationLabel == null) return;
        validationLabel.text = message;
        validationLabel.color = isError
            ? new Color(1f, 0.55f, 0.45f, 1f)
            : new Color(0.45f, 1f, 0.75f, 1f);
    }

    public static bool TryParsePace(string input, out float minutesPerKm)
    {
        minutesPerKm = 5f;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        input = input.Trim().ToLowerInvariant();
        input = input.Replace("/km", string.Empty).Trim();

        if (input.Contains(":"))
        {
            string[] parts = input.Split(':');
            if (parts.Length != 2)
                return false;

            if (!int.TryParse(parts[0], out int minutes))
                return false;
            if (!int.TryParse(parts[1], out int seconds))
                return false;

            if (seconds < 0 || seconds >= 60)
                return false;

            minutesPerKm = minutes + seconds / 60f;
            return minutesPerKm >= MinPaceMinutesPerKm && minutesPerKm <= MaxPaceMinutesPerKm;
        }

        if (!float.TryParse(input, out minutesPerKm))
            return false;

        return minutesPerKm >= MinPaceMinutesPerKm && minutesPerKm <= MaxPaceMinutesPerKm;
    }

    private static string FormatPace(float minutesPerKm)
    {
        int minutes = Mathf.FloorToInt(minutesPerKm);
        int seconds = Mathf.RoundToInt((minutesPerKm - minutes) * 60f);
        if (seconds >= 60)
        {
            minutes++;
            seconds = 0;
        }

        return $"{minutes}:{seconds:00} /km";
    }

    private static string FormatPaceInput(float minutesPerKm)
    {
        int minutes = Mathf.FloorToInt(minutesPerKm);
        int seconds = Mathf.RoundToInt((minutesPerKm - minutes) * 60f);
        if (seconds >= 60)
        {
            minutes++;
            seconds = 0;
        }

        return $"{minutes}:{seconds:00}";
    }

    private static GameObject CreatePanel(Transform parent, string name, Color color)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.GetComponent<Image>().color = color;
        return panel;
    }

    private static RectTransform CreateCard(Transform parent)
    {
        GameObject card = new GameObject("SetupCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(parent, false);

        Image img = card.GetComponent<Image>();
        img.color = new Color(0.08f, 0.12f, 0.18f, 0.98f);

        RectTransform rt = card.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(510f, 500f);
        rt.anchoredPosition = Vector2.zero;
        return rt;
    }

    private static Button CreateSmallButton(RectTransform parent, string text, Vector2 anchoredPos,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject btnGo = new GameObject($"Button_{text}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);

        btnGo.GetComponent<Image>().color = new Color(0.16f, 0.24f, 0.34f, 1f);

        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(80f, 52f);

        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
        textGo.transform.SetParent(btnGo.transform, false);
        TextMeshProUGUI label = textGo.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 18;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        Button button = btnGo.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        return button;
    }

    private static TextMeshProUGUI CreateLabel(RectTransform parent, string text, float fontSize,
        FontStyles style, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size,
        Color? color = null)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color ?? Color.white;
        label.raycastTarget = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return label;
    }

    private static TMP_InputField CreateInputField(RectTransform parent, Vector2 anchoredPos)
    {
        GameObject fieldRoot = new GameObject("PaceInput", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fieldRoot.transform.SetParent(parent, false);

        Image bg = fieldRoot.GetComponent<Image>();
        bg.color = new Color(0.12f, 0.16f, 0.22f, 1f);

        RectTransform fieldRt = fieldRoot.GetComponent<RectTransform>();
        fieldRt.anchorMin = new Vector2(0.5f, 1f);
        fieldRt.anchorMax = new Vector2(0.5f, 1f);
        fieldRt.pivot = new Vector2(0.5f, 1f);
        fieldRt.anchoredPosition = anchoredPos;
        fieldRt.sizeDelta = new Vector2(220f, 52f);

        GameObject textArea = new GameObject("Text Area", typeof(RectTransform));
        textArea.transform.SetParent(fieldRoot.transform, false);
        RectTransform textAreaRt = textArea.GetComponent<RectTransform>();
        textAreaRt.anchorMin = Vector2.zero;
        textAreaRt.anchorMax = Vector2.one;
        textAreaRt.offsetMin = new Vector2(12f, 8f);
        textAreaRt.offsetMax = new Vector2(-12f, -8f);

        GameObject placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer));
        placeholderGo.transform.SetParent(textArea.transform, false);
        TextMeshProUGUI placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();
        placeholder.text = "5:00";
        placeholder.fontSize = 28;
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;

        RectTransform placeholderRt = placeholderGo.GetComponent<RectTransform>();
        placeholderRt.anchorMin = Vector2.zero;
        placeholderRt.anchorMax = Vector2.one;
        placeholderRt.offsetMin = Vector2.zero;
        placeholderRt.offsetMax = Vector2.zero;

        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
        textGo.transform.SetParent(textArea.transform, false);
        TextMeshProUGUI inputText = textGo.AddComponent<TextMeshProUGUI>();
        inputText.fontSize = 28;
        inputText.color = Color.white;
        inputText.alignment = TextAlignmentOptions.MidlineLeft;

        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        TMP_InputField input = fieldRoot.AddComponent<TMP_InputField>();
        input.textViewport = textAreaRt;
        input.textComponent = inputText;
        input.placeholder = placeholder;
        input.contentType = TMP_InputField.ContentType.Custom;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterValidation = TMP_InputField.CharacterValidation.None;
        input.caretColor = new Color(0f, 0.85f, 1f, 1f);
        input.selectionColor = new Color(0f, 0.85f, 1f, 0.35f);

        return input;
    }

    private static Button CreateStartButton(RectTransform parent, Vector2 anchoredPos)
    {
        GameObject btnGo = new GameObject("Start Run Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);

        Image img = btnGo.GetComponent<Image>();
        img.color = new Color(0f, 0.73f, 1f, 1f);

        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(260f, 56f);

        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
        textGo.transform.SetParent(btnGo.transform, false);
        TextMeshProUGUI label = textGo.AddComponent<TextMeshProUGUI>();
        label.text = "START RUN";
        label.fontSize = 22;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        Button button = btnGo.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.2f, 0.85f, 1f, 1f);
        colors.pressedColor = new Color(0f, 0.55f, 0.85f, 1f);
        button.colors = colors;

        return button;
    }
}
