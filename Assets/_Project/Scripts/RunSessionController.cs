using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 走行セッションの終了フローとリザルト画面 (企画書 §4, §5 / 要件定義 4.2):
///  - 走行開始後: 自動スリープ無効化＋誤操作防止ガードレイヤー表示
///  - 終了: 「HOLD TO FINISH」1.5秒長押し（エディタは F キー長押し）
///  - リザルト: 平均シンクロ率から4段階ランク (Perfect / Great / Good / Try Again)
///    ＋ S〜D グレード ＋ アバターによるコメント生成
///  - セッションをアプリ内DB（JSON）へ保存し、iOSでは HealthKit 同期をキュー
/// </summary>
public class RunSessionController : MonoBehaviour
{
    [Header("Engine Links")]
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private AnalyticsManager analytics;
    [SerializeField] private PeripheralHUDManager hudManager;
    [SerializeField] private SafetyEventLogger safetyLogger;
    [SerializeField] private RunAudioEngine audioEngine;
    [SerializeField] private RunTelemetryLogger telemetryLogger;

    [Header("Finish Gesture")]
    [SerializeField] private float holdToFinishSeconds = 1.5f;

    private GameObject _guardLayer;
    private Image _holdProgressFill;
    private TextMeshProUGUI _holdLabel;
    private GameObject _resultPanel;

    private bool _runActive = false;
    private bool _finished = false;
    private float _holdTimer = 0f;
    private bool _uiHoldActive = false;

    // Swift UI (Unity as a Library) が画面を持つ場合は true:
    // Unity側のガードレイヤー/リザルトパネルは描画しない
    private bool _externalUiMode = false;
    private RunSessionRecord _lastRecord;

    // ゴースト機能用のペース推移サンプリング (5秒毎)
    private const float PaceSampleIntervalSeconds = 5f;
    private readonly System.Collections.Generic.List<PaceSample> _paceSamples
        = new System.Collections.Generic.List<PaceSample>();
    private float _nextPaceSampleTime = 0f;

    /// <summary>
    /// Swift(CoreLocation)報告の累積距離(m)。ブリッジがUpdateMetrics毎に更新する。
    /// 鮮度が保たれている間のみUnity内部計測より優先(実機ではGPSが正)。
    /// 供給が途絶えた古い値で記録が固まるのを防ぐため、5秒でUnity計測へフォールバック。
    /// </summary>
    public double ExternalDistanceMeters
    {
        get => _externalDistanceMeters;
        set
        {
            _externalDistanceMeters = value;
            _externalDistanceTimestamp = Time.time;
        }
    }

    private const float ExternalDistanceFreshSeconds = 5f;
    private double _externalDistanceMeters = -1;
    private float _externalDistanceTimestamp = -999f;

    // 記録・サンプリングに使う現在距離: 新鮮なGPS報告を優先、なければUnity計測
    private float CurrentDistanceMeters
    {
        get
        {
            bool externalFresh = _externalDistanceMeters > 0
                && Time.time - _externalDistanceTimestamp <= ExternalDistanceFreshSeconds;
            return externalFresh
                ? (float)_externalDistanceMeters
                : (hudManager != null ? hudManager.DistanceMeters : 0f);
        }
    }

    /// <summary>ゴール判定・記録が共有する正規の現在距離(m)。ソース不一致を防ぐ。</summary>
    public float AuthoritativeDistanceMeters => CurrentDistanceMeters;

    public RunSessionRecord LastRecord => _lastRecord;
    public bool IsFinished => _finished;

    void Awake()
    {
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (analytics == null)
            analytics = FindFirstObjectByType<AnalyticsManager>(FindObjectsInactive.Include);
        if (hudManager == null)
            hudManager = FindFirstObjectByType<PeripheralHUDManager>(FindObjectsInactive.Include);
        if (safetyLogger == null)
            safetyLogger = FindFirstObjectByType<SafetyEventLogger>(FindObjectsInactive.Include);
        if (audioEngine == null)
            audioEngine = FindFirstObjectByType<RunAudioEngine>(FindObjectsInactive.Include);
        if (telemetryLogger == null)
            telemetryLogger = FindFirstObjectByType<RunTelemetryLogger>(FindObjectsInactive.Include);
    }

    void Update()
    {
        // Fallback: detect a start that did not go through OnRunStarted
        if (!_runActive && !_finished && avatarEngine != null && avatarEngine.HasStarted)
            OnRunStarted();

        if (!_runActive || _finished) return;

        // ゴースト機能: 走行中のペース推移を5秒毎に記録 (距離はGPS優先)
        if (hudManager != null && hudManager.ElapsedTimeSeconds >= _nextPaceSampleTime)
        {
            _nextPaceSampleTime = hudManager.ElapsedTimeSeconds + PaceSampleIntervalSeconds;
            _paceSamples.Add(new PaceSample
            {
                t = hudManager.ElapsedTimeSeconds,
                meters = CurrentDistanceMeters
            });
        }

        bool holding = _uiHoldActive;
#if UNITY_EDITOR
        holding |= Input.GetKey(KeyCode.F);
#endif

        if (holding)
        {
            _holdTimer += Time.deltaTime;
            if (_holdProgressFill != null)
                _holdProgressFill.fillAmount = _holdTimer / holdToFinishSeconds;

            if (_holdTimer >= holdToFinishSeconds)
                FinishRun();
        }
        else if (_holdTimer > 0f)
        {
            _holdTimer = 0f;
            if (_holdProgressFill != null)
                _holdProgressFill.fillAmount = 0f;
        }
    }

    /// <summary>Called by PaceCalibrationController immediately after StartPacing.</summary>
    public void OnRunStarted() => OnRunStarted(showUnityUi: true);

    /// <summary>Swiftブリッジ経由の開始は showUnityUi=false(Swift側がUIを持つ)。</summary>
    public void OnRunStarted(bool showUnityUi)
    {
        if (_runActive) return;
        _runActive = true;
        _externalUiMode = !showUnityUi;

        // 企画書 §5: 走行中は自動スリープを無効化
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        if (!_externalUiMode)
            BuildGuardLayer();
        Debug.Log($"[SESSION] Run started — sleep disabled (externalUi={_externalUiMode}).");
    }

    /// <summary>Swiftブリッジ用の終了API。集計済みレコードを返す。</summary>
    public RunSessionRecord FinishRunExternal()
    {
        if (!_finished)
            FinishRun();
        return _lastRecord;
    }

    /// <summary>
    /// 再走行対応: 終了済みセッションを破棄し、全コンポーネントを次の走行が
    /// 開始できる状態に戻す(履歴・保存済みJSONはそのまま残る)。
    /// </summary>
    public void ResetForNewSession()
    {
        if (_runActive)
        {
            Debug.LogWarning("[SESSION] ResetForNewSession ignored — a run is still active.");
            return;
        }

        _finished = false;
        _holdTimer = 0f;
        _uiHoldActive = false;
        _paceSamples.Clear();
        _nextPaceSampleTime = 0f;
        ExternalDistanceMeters = -1;
        // Swift主導フラグも解除(次がUnity単体走行ならスプリット供給を復帰させる。
        // Swift主導の再走行時はブリッジがリセット直後に再度trueにする)
        ARSessionManagerBridge.ExternalMetricsActive = false;

        if (_resultPanel != null) Destroy(_resultPanel);
        if (_guardLayer != null) Destroy(_guardLayer);
        _resultPanel = null;
        _guardLayer = null;
        _holdProgressFill = null;
        _holdLabel = null;

        if (avatarEngine != null) avatarEngine.ResetSession();
        if (analytics != null) analytics.ResetSession();
        if (hudManager != null) hudManager.ResetSession();
        if (safetyLogger != null) safetyLogger.ResetSession();
        if (audioEngine != null) audioEngine.ResetSession();
        if (telemetryLogger != null) telemetryLogger.ResetSession();
        var gpsMonitor = FindFirstObjectByType<GpsSignalMonitor>(FindObjectsInactive.Include);
        if (gpsMonitor != null) gpsMonitor.ResetSession();
        // 再走行でもカウントダウンをやり直す
        var countdown = FindFirstObjectByType<CountdownDisplay>(FindObjectsInactive.Include);
        if (countdown != null) countdown.ResetSession();

        Debug.Log("[SESSION] Reset complete — ready for a new run.");
    }

    private void FinishRun()
    {
        _finished = true;
        _runActive = false;
        Screen.sleepTimeout = SleepTimeout.SystemSetting;

        if (avatarEngine != null)
            avatarEngine.IsSessionEnded = true; // アバターは待機状態へ (IsHaltedはGroundSnapが毎フレーム上書きするため不可)

        if (audioEngine != null)
            audioEngine.PlayGoalFanfare();

        if (_guardLayer != null)
            _guardLayer.SetActive(false);

        RunSessionRecord record = BuildSessionRecord();
        _lastRecord = record;
        string savedPath = SessionDataStore.SaveSession(record);

        if (!_externalUiMode)
            BuildResultPanel(record, savedPath);
        Debug.Log($"[SESSION] Run finished — rank {record.rankLabel} ({record.grade}).");
    }

    // ── セッション集計・ランク・コメント ─────────────────────────────────────

    private RunSessionRecord BuildSessionRecord()
    {
        float avgSync = analytics != null ? analytics.GetSessionAverageSync() : 0f;
        string grade = analytics != null ? analytics.EvaluateFinalSessionPerformanceRank() : "D";

        var record = new RunSessionRecord
        {
            dateIso = System.DateTime.Now.ToString("o"),
            distanceMeters = CurrentDistanceMeters, // 実機はGPS(Swift報告)優先
            elapsedSeconds = hudManager != null ? hudManager.ElapsedTimeSeconds : 0f,
            averageSyncRate = avgSync,
            grade = grade,
            rankLabel = GradeToRankLabel(grade),
            fatigueIndex = analytics != null ? analytics.GetCumulativeFatigue() : 0f,
            targetPaceMinutesPerKm = avatarEngine != null ? avatarEngine.TargetPaceMinutesPerKm : 0f,
        };

        // 消費カロリー: オンボーディングの実体重を使用(ランニング標準推定式)
        record.calories = UserProfile.WeightKg * (record.distanceMeters / 1000f) * 1.05f;

        if (safetyLogger != null)
            record.safetyEvents.AddRange(safetyLogger.Events);

        record.paceTimeline.AddRange(_paceSamples);

        record.avatarComment = GenerateAvatarComment(record);
        return record;
    }

    // 企画書 §4: 平均シンクロ率から4段階ランクを判定 (Perfect〜Try Again)
    private static string GradeToRankLabel(string grade)
    {
        switch (grade)
        {
            case "S": return "PERFECT";
            case "A": return "GREAT";
            case "B":
            case "C": return "GOOD";
            default: return "TRY AGAIN";
        }
    }

    // 企画書 §4: アバターによるコメント生成
    private static string GenerateAvatarComment(RunSessionRecord r)
    {
        float km = r.distanceMeters / 1000f;
        string baseComment = r.grade switch
        {
            "S" => $"完璧な並走だったね！シンクロ率{r.averageSyncRate:F0}%、ほとんど二人三脚だ。",
            "A" => $"すごくいいペース感覚！シンクロ率{r.averageSyncRate:F0}%。次はPERFECTを狙おう。",
            "B" => $"よく粘ったね。{km:F1}km お疲れさま。中盤の離れがもったいなかった！",
            "C" => $"今日は難しい日だったかな。それでも{km:F1}km 走り切ったのは立派だよ。",
            _ => "また一緒に走ろう。ペースは僕に任せて、君は前だけ見ていればいい。"
        };

        if (r.fatigueIndex > 50f)
            baseComment += " 暑さの中よく頑張った、しっかり水分補給してね。";
        if (r.safetyEvents.Count > 0)
            baseComment += $" 気になる地点が{r.safetyEvents.Count}箇所あったから、マップで確認しておいて。";

        return baseComment;
    }

    // ── ガードレイヤー UI（誤操作防止）───────────────────────────────────────

    private void BuildGuardLayer()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null) return;

        _guardLayer = new GameObject("GuardLayer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _guardLayer.transform.SetParent(canvas.transform, false);

        // Nearly invisible but blocks stray taps across the whole screen
        Image blocker = _guardLayer.GetComponent<Image>();
        blocker.color = new Color(0f, 0f, 0f, 0.02f);
        blocker.raycastTarget = true;

        RectTransform rt = _guardLayer.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        BuildHoldToFinishButton(_guardLayer.transform);
    }

    private void BuildHoldToFinishButton(Transform parent)
    {
        GameObject btn = new GameObject("HoldToFinish", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        btn.transform.SetParent(parent, false);

        Image bg = btn.GetComponent<Image>();
        bg.color = new Color(0.15f, 0.05f, 0.05f, 0.75f);
        bg.raycastTarget = true;

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 28f);
        rt.sizeDelta = new Vector2(260f, 56f);

        // Radial-style progress fill behind the label
        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.transform.SetParent(btn.transform, false);
        _holdProgressFill = fillGo.GetComponent<Image>();
        _holdProgressFill.color = new Color(0.9f, 0.25f, 0.2f, 0.85f);
        _holdProgressFill.raycastTarget = false;
        _holdProgressFill.type = Image.Type.Filled;
        _holdProgressFill.fillMethod = Image.FillMethod.Horizontal;
        _holdProgressFill.fillAmount = 0f;
        // Filled type needs a sprite to render; a white-texture sprite is enough
        _holdProgressFill.sprite = Sprite.Create(Texture2D.whiteTexture,
            new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
            new Vector2(0.5f, 0.5f));

        RectTransform fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer));
        labelGo.transform.SetParent(btn.transform, false);
        _holdLabel = labelGo.AddComponent<TextMeshProUGUI>();
        _holdLabel.text = "HOLD TO FINISH (1.5s)";
        _holdLabel.fontSize = 18;
        _holdLabel.fontStyle = FontStyles.Bold;
        _holdLabel.alignment = TextAlignmentOptions.Center;
        _holdLabel.color = Color.white;
        _holdLabel.raycastTarget = false;

        RectTransform labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        HoldButtonRelay relay = btn.AddComponent<HoldButtonRelay>();
        relay.OnHoldChanged = held => _uiHoldActive = held;
    }

    // ── リザルト画面 ─────────────────────────────────────────────────────────

    private void BuildResultPanel(RunSessionRecord record, string savedPath)
    {
        Canvas canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null) return;

        _resultPanel = new GameObject("ResultPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _resultPanel.transform.SetParent(canvas.transform, false);
        _resultPanel.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.1f, 0.94f);

        RectTransform rt = _resultPanel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        RectTransform card = CreateCard(_resultPanel.transform);

        Color rankColor = record.grade switch
        {
            "S" => new Color(1f, 0.85f, 0.2f),
            "A" => new Color(0.3f, 0.9f, 1f),
            "B" => new Color(0.4f, 0.95f, 0.6f),
            "C" => new Color(0.8f, 0.8f, 0.85f),
            _ => new Color(1f, 0.55f, 0.45f)
        };

        int min = Mathf.FloorToInt(record.elapsedSeconds / 60f);
        int sec = Mathf.FloorToInt(record.elapsedSeconds % 60f);

        AddLabel(card, "RUN RESULT", 20, FontStyles.Bold, -24f, new Color(0.6f, 0.75f, 0.95f));
        AddLabel(card, record.rankLabel, 52, FontStyles.Bold, -58f, rankColor);
        AddLabel(card, $"Grade {record.grade}  •  Sync {record.averageSyncRate:F1}%", 20, FontStyles.Normal, -128f, Color.white);
        AddLabel(card,
            $"{record.distanceMeters / 1000f:F2} km   {min:00}:{sec:00}   Fatigue {record.fatigueIndex:F1}",
            18, FontStyles.Normal, -162f, new Color(0.75f, 0.82f, 0.9f));
        AddLabel(card, $"Safety events: {record.safetyEvents.Count}", 15, FontStyles.Normal, -194f,
            record.safetyEvents.Count == 0 ? new Color(0.45f, 1f, 0.75f) : new Color(1f, 0.7f, 0.4f));

        // アバターコメント
        TextMeshProUGUI comment = AddLabel(card, $"“{record.avatarComment}”", 17, FontStyles.Italic, -232f,
            new Color(0.55f, 0.95f, 1f));
        comment.rectTransform.sizeDelta = new Vector2(440f, 110f);
        comment.textWrappingMode = TextWrappingModes.Normal;

        AddLabel(card, "Session saved to app database" +
            "\n<size=11>" + savedPath + "</size>", 13, FontStyles.Normal, -352f,
            new Color(0.55f, 0.65f, 0.78f));
    }

    private static RectTransform CreateCard(Transform parent)
    {
        GameObject card = new GameObject("ResultCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(parent, false);
        card.GetComponent<Image>().color = new Color(0.08f, 0.12f, 0.18f, 0.98f);

        RectTransform rt = card.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(500f, 430f);
        rt.anchoredPosition = Vector2.zero;
        return rt;
    }

    private static TextMeshProUGUI AddLabel(RectTransform parent, string text, float size,
        FontStyles style, float y, Color color)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;
        label.raycastTarget = false;
        label.richText = true;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(460f, 60f);
        return label;
    }

    /// <summary>Forwards pointer hold state from the finish button to the controller.</summary>
    private class HoldButtonRelay : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public System.Action<bool> OnHoldChanged;

        public void OnPointerDown(PointerEventData eventData) => OnHoldChanged?.Invoke(true);
        public void OnPointerUp(PointerEventData eventData) => OnHoldChanged?.Invoke(false);
        public void OnPointerExit(PointerEventData eventData) => OnHoldChanged?.Invoke(false);
    }
}
