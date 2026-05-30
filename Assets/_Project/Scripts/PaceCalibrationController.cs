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

    void Start()
    {
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
}