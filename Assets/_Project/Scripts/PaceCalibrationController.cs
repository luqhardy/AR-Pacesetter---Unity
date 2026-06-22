using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PaceCalibrationController : MonoBehaviour
{
    [Header("Engine Reference")]
    [SerializeField] private AvatarEngine avatarEngine;

    [Header("UI Component Hookups")]
    [SerializeField] private Slider paceSlider;
    [SerializeField] private TextMeshProUGUI paceDisplayLabel;
    [SerializeField] private Button startButton;

    void Start()
    {
        // 1. Resolve Dependencies
        if (avatarEngine == null)
        {
            // Update: search including inactive objects as the avatar might be in Standby
            avatarEngine = GetComponent<AvatarEngine>() ?? FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        }

        // 2. Resolve Start Button
        if (startButton == null)
        {
            // Search for button by name in the scene
            GameObject startBtnObj = GameObject.Find("Start Button") ?? GameObject.Find("StartButton");
            if (startBtnObj != null)
            {
                startButton = startBtnObj.GetComponent<Button>();
            }
        }

        // 3. Hook up Start Button
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(StartRunning); // Avoid duplicate listeners
            startButton.onClick.AddListener(StartRunning);
            Debug.Log($"[UI] Start Button hooked to {startButton.gameObject.name}");
        }
        else
        {
            Debug.LogWarning("[UI] Start Button not assigned or found. Attempting dynamic creation.");
            CreateDynamicStartButton();
        }

        // 4. Hook up Pace Slider
        if (paceSlider != null)
        {
            paceSlider.minValue = 3.5f;
            paceSlider.maxValue = 7.0f;
            paceSlider.onValueChanged.RemoveListener(OnPaceSliderMoved);
            paceSlider.onValueChanged.AddListener(OnPaceSliderMoved);

            // Set default
            if (avatarEngine != null)
            {
                paceSlider.value = avatarEngine.TargetPaceMinutesPerKm;
                OnPaceSliderMoved(paceSlider.value);
            }
            else
            {
                paceSlider.value = 5.0f;
                OnPaceSliderMoved(5.0f);
            }
        }
        else
        {
            Debug.LogError("[UI] Pace Slider reference is missing in PaceCalibrationController!");
        }

        if (avatarEngine == null)
        {
            Debug.LogError("[UI] AvatarEngine not found! Start Run button will not work. Ensure Avatar is in the scene.");
        }
    }

    public void OnPaceSliderMoved(float sliderValue)
    {
        if (avatarEngine != null)
        {
            avatarEngine.UpdateTargetPace(sliderValue);
        }

        int minutes = Mathf.FloorToInt(sliderValue);
        int seconds = Mathf.FloorToInt((sliderValue - minutes) * 60f);

        if (paceDisplayLabel != null)
        {
            paceDisplayLabel.text = string.Format("Target Pace: {0}:{1:00} /km", minutes, seconds);
        }
    }

    private void StartRunning()
    {
        if (avatarEngine == null)
        {
            // Late binding attempt including inactive
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        }

        if (avatarEngine != null)
        {
            Debug.Log("[UI] Start Button Clicked - Starting Pacing.");
            avatarEngine.StartPacing();
            
            // Only hide the button if we actually have an engine to start
            if (startButton != null)
            {
                startButton.gameObject.SetActive(false);
            }
        }
        else
        {
            Debug.LogError("[UI] Start Button Clicked but AvatarEngine is still missing! Is the avatar prefab missing?");
        }
    }

    private void CreateDynamicStartButton()
    {
        Canvas canvas = null;
        GameObject hudCanvas = GameObject.Find("HUD_Canvas") ?? GameObject.Find("Canvas");
        if (hudCanvas != null)
            canvas = hudCanvas.GetComponent<Canvas>();
        
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        
        if (canvas == null)
        {
            Debug.LogError("[UI] Cannot create dynamic button: No Canvas found in scene!");
            return;
        }

        Debug.Log($"[UI] Creating dynamic Start Button on {canvas.name}");

        // Create Button GameObject
        GameObject btnObj = new GameObject("Dynamic Start Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(canvas.transform, false);

        // Position it nicely above the slider (slider is usually centered bottom)
        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 250f); // Positioned above the slider
        rt.sizeDelta = new Vector2(180f, 60f);

        // Styling
        Image img = btnObj.GetComponent<Image>();
        img.color = new Color(0f, 0.73f, 1f, 0.95f); // Tech Cyan

        Button btn = btnObj.GetComponent<Button>();
        btn.onClick.AddListener(StartRunning);
        startButton = btn;

        // Create Text child
        GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
        txtObj.transform.SetParent(btnObj.transform, false);
        
        RectTransform txtRt = txtObj.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.sizeDelta = Vector2.zero;

        TextMeshProUGUI tmpText = txtObj.AddComponent<TextMeshProUGUI>();
        if (paceDisplayLabel != null && paceDisplayLabel.font != null)
            tmpText.font = paceDisplayLabel.font;
        tmpText.text = "START RUN";
        tmpText.alignment = TextAlignmentOptions.Center;
        tmpText.fontSize = 18f;
        tmpText.color = Color.white;
        tmpText.fontStyle = FontStyles.Bold;
    }
}
