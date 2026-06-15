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
        if (avatarEngine == null)
            avatarEngine = FindObjectOfType<AvatarEngine>();

        // Try to find the button dynamically if not assigned
        if (startButton == null)
        {
            GameObject startBtnObj = GameObject.Find("Start Button") ?? GameObject.Find("StartButton");
            if (startBtnObj != null)
            {
                startButton = startBtnObj.GetComponent<Button>();
            }
        }

        if (startButton != null)
        {
            startButton.onClick.AddListener(StartRunning);
        }
        else
        {
            CreateDynamicStartButton();
        }

        if (paceSlider == null || avatarEngine == null) return;

        // Initialize slider properties programmatically
        paceSlider.minValue = 3.5f; // Elite 3:30/km minimum limit
        paceSlider.maxValue = 7.0f; // Easy 7:00/km maximum recovery pace

        // Add a listener loop to catch updates automatically when the player drags the slider
        paceSlider.onValueChanged.AddListener(OnPaceSliderMoved);

        // Set baseline default position
        paceSlider.value = 5.0f;
        OnPaceSliderMoved(5.0f);
    }

    public void OnPaceSliderMoved(float sliderValue)
    {
        if (avatarEngine != null)
        {
            // Update the math engine's target velocity matrix instantly
            avatarEngine.UpdateTargetPace(sliderValue);
        }

        // Format raw mathematical decimals back into readable running splits
        // Example: 4.5 minutes turns into 4 minutes and 30 seconds (4:30/km)
        int minutes = Mathf.FloorToInt(sliderValue);
        int seconds = Mathf.FloorToInt((sliderValue - minutes) * 60f);

        if (paceDisplayLabel != null)
        {
            paceDisplayLabel.text = string.Format("Target Pace: {0}:{1:00} /km", minutes, seconds);
        }
    }

    private void StartRunning()
    {
        if (avatarEngine != null)
        {
            avatarEngine.StartPacing();
        }

        if (startButton != null)
        {
            startButton.gameObject.SetActive(false);
        }
    }

    private void CreateDynamicStartButton()
    {
        Canvas canvas = null;
        GameObject hudCanvas = GameObject.Find("HUD_Canvas");
        if (hudCanvas != null)
            canvas = hudCanvas.GetComponent<Canvas>();
        if (canvas == null)
            canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

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
