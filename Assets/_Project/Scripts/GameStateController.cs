using System.Collections;
using UnityEngine;

public class GameStateController : MonoBehaviour
{
    // The 5 mandatory states from AGENTS.md §5
    public enum ARVisionState
    {
        Normal,
        InertialMovement,
        FadeOut,
        Standby,
        Reaccumulation
    }

    [Header("Current Status")]
    public ARVisionState currentState = ARVisionState.Normal;

    [Header("References")]
    [SerializeField] private GameObject avatarTarget;
    [SerializeField] private MeshRenderer avatarRenderer;

    private SkinnedMeshRenderer _avatarSkinnedRenderer;
    private float _gpsLostTimer = 0.0f;
    private Coroutine _fadeCoroutine;

    // ── GPS Accuracy Gate (AGENTS.md §5 — accuracy radius ≤5m required) ────
    // In production this is fed by ARKit/CoreLocation. In the editor press 'A'.
    public float SimulatedGPSAccuracyRadius { get; set; } = 99f; // 99 = uncertain

    // ────────────────────────────────────────────────────────────────────────
    void Update()
    {
        switch (currentState)
        {
            case ARVisionState.Normal:           HandleNormalState();        break;
            case ARVisionState.InertialMovement: HandleInertialState();      break;
            case ARVisionState.FadeOut:          HandleFadeOutState();       break;
            case ARVisionState.Standby:          HandleStandbyState();       break;
            case ARVisionState.Reaccumulation:   HandleReaccumulationState();break;
        }
    }

    // ── State handlers ───────────────────────────────────────────────────────
    private void HandleNormalState()
    {
        if (Input.GetKeyDown(KeyCode.G))
            TransitionToState(ARVisionState.InertialMovement);
    }

    private void HandleInertialState()
    {
        _gpsLostTimer += Time.deltaTime;

        if (_gpsLostTimer >= 5.0f)
            TransitionToState(ARVisionState.FadeOut);

        if (Input.GetKeyDown(KeyCode.R))
            TransitionToState(ARVisionState.Normal);
    }

    private void HandleFadeOutState()
    {
        // AGENTS.md §5: GPS restored before 1s completes → return directly to Normal
        if (Input.GetKeyDown(KeyCode.R))
            TransitionToState(ARVisionState.Normal);
    }

    private void HandleStandbyState()
    {
        if (Input.GetKeyDown(KeyCode.R))
            TransitionToState(ARVisionState.Reaccumulation);
    }

    private void HandleReaccumulationState()
    {
        // 'A' simulates GPS accuracy settling to ≤5m in the editor
        if (Input.GetKeyDown(KeyCode.A))
        {
            SimulatedGPSAccuracyRadius = 4.0f; // inside the 5m gate
            Debug.Log("[SIMULATOR] GPS accuracy settled to 4m — ReAccumulation gate now open.");
        }
    }

    // ── Transition dispatcher ────────────────────────────────────────────────
    public void TransitionToState(ARVisionState newState)
    {
        currentState = newState;
        Debug.Log($"[FSM] AR Vision State → {newState}");

        switch (newState)
        {
            case ARVisionState.InertialMovement:
                _gpsLostTimer = 0.0f;
                break;

            case ARVisionState.FadeOut:
                if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = StartCoroutine(FadeAvatarAlpha(GetCurrentAlpha(), 0.0f, 1.0f));
                break;

            case ARVisionState.Standby:
                if (avatarTarget != null) avatarTarget.SetActive(false);
                break;

            case ARVisionState.Reaccumulation:
                // Reset accuracy so it must be re-confirmed (press A in editor)
                SimulatedGPSAccuracyRadius = 99f;
                if (avatarTarget != null) avatarTarget.SetActive(true);
                if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = StartCoroutine(ExecuteReaccumulationProcess());
                break;

            case ARVisionState.Normal:
                _gpsLostTimer = 0.0f;
                if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
                RestoreAvatarAlpha();
                break;
        }
    }

    // ── Renderer hot-swap (called by AvatarModelSwitcher) ───────────────────
    public void UpdateActiveRenderer(MeshRenderer staticMesh, SkinnedMeshRenderer skinnedMesh)
    {
        avatarRenderer        = staticMesh;
        _avatarSkinnedRenderer = skinnedMesh;
    }

    // ── Coroutines ───────────────────────────────────────────────────────────
    private IEnumerator FadeAvatarAlpha(float start, float end, float duration)
    {
        Material mat = GetActiveMaterial();
        if (mat == null) yield break;

        Color baseColor = mat.color;
        float elapsed = 0.0f;

        while (elapsed < duration)
        {
            // Allow GPS recovery to interrupt the fade at any point (AGENTS.md §5)
            if (currentState == ARVisionState.Normal) yield break;

            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(start, end, elapsed / duration);
            ApplyAlpha(baseColor, alpha);
            yield return null;
        }

        if (end == 0.0f)
            TransitionToState(ARVisionState.Standby);
    }

    private IEnumerator ExecuteReaccumulationProcess()
    {
        // Step 1: 1.5s particle-gathering animation
        Debug.Log("[REACCUMULATION] Playing 1.5s light-particle gathering FX…");
        yield return new WaitForSeconds(1.5f);

        // Step 2: AGENTS.md §5 accuracy gate — wait until radius ≤ 5m
        Debug.Log("[REACCUMULATION] Waiting for GPS accuracy ≤5m… (press A in Editor)");
        while (SimulatedGPSAccuracyRadius > 5.0f)
            yield return null;

        // Step 3: Materialize and confirm
        RestoreAvatarAlpha();

        Animator anim = avatarTarget != null
            ? avatarTarget.GetComponentInChildren<Animator>()
            : null;
        if (anim != null) anim.SetTrigger("Nod");

        Debug.Log("[REACCUMULATION] GPS lock confirmed. Returning to Normal.");
        TransitionToState(ARVisionState.Normal);
    }

    // ── Material helpers ─────────────────────────────────────────────────────
    private float GetCurrentAlpha()
    {
        Material m = GetActiveMaterial();
        return m != null ? m.color.a : 1.0f;
    }

    private Material GetActiveMaterial()
    {
        if (avatarRenderer        != null) return avatarRenderer.material;
        if (_avatarSkinnedRenderer != null) return _avatarSkinnedRenderer.material;
        return null;
    }

    private void ApplyAlpha(Color baseColor, float alpha)
    {
        Color c = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        if (avatarRenderer        != null) avatarRenderer.material.color        = c;
        if (_avatarSkinnedRenderer != null) _avatarSkinnedRenderer.material.color = c;
    }

    private void RestoreAvatarAlpha()
    {
        Material m = GetActiveMaterial();
        if (m == null) return;
        Color c = m.color;
        ApplyAlpha(c, 1.0f);
    }
}
