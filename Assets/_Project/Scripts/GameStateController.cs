using System.Collections;
using UnityEngine;

public class GameStateController : MonoBehaviour
{
    // The 5 mandatory states from the technical specification
    public enum ARVisionState
    {
        Normal,             // Normal operational state
        InertialMovement,   // GPS lost, estimating location based on momentum
        FadeOut,            // Smoothly hiding avatar after prolonged signal loss
        Standby,            // Completely hidden, waiting for solid lock
        Reaccumulation      // Materializing avatar back into the field
    }

    [Header("Current Status")]
    public ARVisionState currentState = ARVisionState.Normal;

    [Header("References")]
    [SerializeField] private GameObject avatarTarget;
    [SerializeField] private MeshRenderer avatarRenderer;

    private SkinnedMeshRenderer _avatarSkinnedRenderer;
    private float _gpsLostTimer = 0.0f;
    private Coroutine _fadeCoroutine;

    void Update()
    {
        switch (currentState)
        {
            case ARVisionState.Normal:
                HandleNormalState();
                break;
            case ARVisionState.InertialMovement:
                HandleInertialState();
                break;
            case ARVisionState.FadeOut:
                break;
            case ARVisionState.Standby:
                HandleStandbyState();
                break;
            case ARVisionState.Reaccumulation:
                break;
        }
    }

    private void HandleNormalState()
    {
        // Simulate a GPS loss event for testing by pressing 'G'
        if (Input.GetKeyDown(KeyCode.G))
        {
            TransitionToState(ARVisionState.InertialMovement);
        }
    }

    private void HandleInertialState()
    {
        _gpsLostTimer += Time.deltaTime;

        // Maintain trajectory for 5 seconds via inertial dead-reckoning
        if (_gpsLostTimer >= 5.0f)
        {
            TransitionToState(ARVisionState.FadeOut);
        }

        // If signal is restored before 5s, snap right back to normal
        if (Input.GetKeyDown(KeyCode.R))
        {
            TransitionToState(ARVisionState.Normal);
        }
    }

    private void HandleStandbyState()
    {
        // Simulate signal restoration by pressing 'R'
        if (Input.GetKeyDown(KeyCode.R))
        {
            TransitionToState(ARVisionState.Reaccumulation);
        }
    }

    public void TransitionToState(ARVisionState newState)
    {
        currentState = newState;
        Debug.Log($"AR Vision State Changed To: {newState}");

        switch (newState)
        {
            case ARVisionState.InertialMovement:
                _gpsLostTimer = 0.0f;
                break;

            case ARVisionState.FadeOut:
                if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = StartCoroutine(FadeAvatarAlpha(1.0f, 0.0f, 1.0f)); // 1-second fade out
                break;

            case ARVisionState.Standby:
                if (avatarTarget != null) avatarTarget.SetActive(false);
                break;

            case ARVisionState.Reaccumulation:
                if (avatarTarget != null) avatarTarget.SetActive(true);
                if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = StartCoroutine(ExecuteReaccumulationProcess());
                break;

            case ARVisionState.Normal:
                _gpsLostTimer = 0.0f;
                break;
        }
    }

    // Dynamic pipeline gateway to route tracking commands to VRChat or Capsule meshes dynamically
    public void UpdateActiveRenderer(MeshRenderer staticMesh, SkinnedMeshRenderer skinnedMesh)
    {
        avatarRenderer = staticMesh;
        _avatarSkinnedRenderer = skinnedMesh;
    }

    private IEnumerator FadeAvatarAlpha(float start, float end, float duration)
    {
        float elapsed = 0.0f;

        // Safety validation checklist: identify which component holds the material
        Material targetMat = null;
        if (avatarRenderer != null) targetMat = avatarRenderer.material;
        else if (_avatarSkinnedRenderer != null) targetMat = _avatarSkinnedRenderer.material;

        if (targetMat == null) yield break;
        Color matColor = targetMat.color;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float newAlpha = Mathf.Lerp(start, end, elapsed / duration);

            // Apply opacity transformations safely to whichever asset mesh is currently active
            if (avatarRenderer != null) avatarRenderer.material.color = new Color(matColor.r, matColor.g, matColor.b, newAlpha);
            if (_avatarSkinnedRenderer != null) _avatarSkinnedRenderer.material.color = new Color(matColor.r, matColor.g, matColor.b, newAlpha);

            yield return null;
        }

        if (end == 0.0f)
        {
            TransitionToState(ARVisionState.Standby); // Automatically enter standby when invisible
        }
    }

    private IEnumerator ExecuteReaccumulationProcess()
    {
        // 1.5s gathering of light particles at the 3m mark
        Debug.Log("Playing 1.5s Light Particle Accumulation FX...");
        yield return new WaitForSeconds(1.5f);

        // Safely snap visibility color parameters back to 100% opaque without breaking custom characters
        if (avatarRenderer != null)
        {
            Color c = avatarRenderer.material.color;
            avatarRenderer.material.color = new Color(c.r, c.g, c.b, 1.0f);
        }
        if (_avatarSkinnedRenderer != null)
        {
            Color c = _avatarSkinnedRenderer.material.color;
            _avatarSkinnedRenderer.material.color = new Color(c.r, c.g, c.b, 1.0f);
        }

        // Trigger "Nod" animation confirmation back to user
        Debug.Log("Avatar plays confirmation 'Nod' animation.");

        TransitionToState(ARVisionState.Normal); // Handshake complete
    }
}