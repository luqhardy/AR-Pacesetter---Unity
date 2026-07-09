using UnityEngine;

public class AvatarModelSwitcher : MonoBehaviour
{
    public enum AvatarType { DefaultCapsule, CustomVRChat }

    [Header("Active Model Toggle")]
    public AvatarType activeAvatar = AvatarType.CustomVRChat;

    [Header("Model References")]
    [SerializeField] private GameObject defaultCapsuleObject;
    [SerializeField] private GameObject customVRChatObject;

    [Header("External System Pipelines")]
    [SerializeField] private GameStateController gameStateController;
    [SerializeField] private AvatarVisualsAndActions visualsController;

    private void OnValidate()
    {
        UpdateActiveAvatarModel();
    }

    private void Start()
    {
        UpdateActiveAvatarModel();
    }

    public void SwitchAvatar(AvatarType newType)
    {
        activeAvatar = newType;
        UpdateActiveAvatarModel();
    }

    // --- THE TWO-WAY SMART TOGGLE ---
    // Tapping the button will now automatically cycle back and forth
    public void ToggleAvatar()
    {
        if (activeAvatar == AvatarType.DefaultCapsule)
        {
            SwitchAvatar(AvatarType.CustomVRChat);
        }
        else
        {
            SwitchAvatar(AvatarType.DefaultCapsule);
        }
    }

    public void SetToDefaultCapsuleMode()
    {
        SwitchAvatar(AvatarType.DefaultCapsule);
    }

    public void SetToCustomVRChatMode()
    {
        SwitchAvatar(AvatarType.CustomVRChat);
    }

    private void UpdateActiveAvatarModel()
    {
        // Guard: the scene may have these fields mis-wired (e.g. pointing at UI
        // icons). Toggling those would hide UI and rebind renderers/animators to
        // objects that have none — the visible model then never animates.
        if (!IsValidModelObject(defaultCapsuleObject) || !IsValidModelObject(customVRChatObject))
        {
            if (Application.isPlaying)
                AutoWireSingleModel();
            return;
        }

        defaultCapsuleObject.SetActive(activeAvatar == AvatarType.DefaultCapsule);
        customVRChatObject.SetActive(activeAvatar == AvatarType.CustomVRChat);

        GameObject currentActiveTarget = (activeAvatar == AvatarType.DefaultCapsule) ? defaultCapsuleObject : customVRChatObject;
        MeshRenderer activeRenderer = currentActiveTarget.GetComponentInChildren<MeshRenderer>();
        SkinnedMeshRenderer activeSkinnedRenderer = currentActiveTarget.GetComponentInChildren<SkinnedMeshRenderer>();

        if (gameStateController != null)
        {
            gameStateController.UpdateActiveRenderer(activeRenderer, activeSkinnedRenderer);
        }

        if (visualsController != null)
        {
            visualsController.UpdateActiveRenderer(activeRenderer, activeSkinnedRenderer);
        }

        // --- Animator Hot-Swapping ---
        Animator activeAnimator = currentActiveTarget.GetComponentInChildren<Animator>();
        if (activeAnimator == null)
        {
            activeAnimator = GetComponent<Animator>();
        }

        OvertakeBehaviourController overtakeController = GetComponent<OvertakeBehaviourController>();

        if (activeAnimator != null)
        {
            RuntimeAnimatorController correctController = Resources.Load<RuntimeAnimatorController>("AvatarAnimatorController");
            if (activeAnimator.runtimeAnimatorController != correctController)
            {
                activeAnimator.runtimeAnimatorController = correctController;
            }
            // Prevent Root Motion from conflicting with our scripted movement
            activeAnimator.applyRootMotion = false;

            // Wire up AvatarIKRelay dynamically so OnAnimatorIK is correctly relayed to OvertakeBehaviourController
            AvatarIKRelay relay = activeAnimator.GetComponent<AvatarIKRelay>();
            if (relay == null)
            {
                relay = activeAnimator.gameObject.AddComponent<AvatarIKRelay>();
            }
            relay.TargetController = overtakeController;
        }
        
        if (overtakeController != null)
        {
            overtakeController.UpdateActiveAnimator(activeAnimator);
        }
    }

    // A usable model reference: a world-space object with a real 3D renderer
    private static bool IsValidModelObject(GameObject go)
    {
        if (go == null) return false;
        if (go.GetComponent<RectTransform>() != null) return false; // UI element
        return go.GetComponentInChildren<MeshRenderer>(true) != null
            || go.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
    }

    /// <summary>
    /// Fallback when the switcher references are unusable: wire the single model
    /// that actually lives under this container (animator, glow renderer, IK relay).
    /// </summary>
    private void AutoWireSingleModel()
    {
        Debug.LogWarning("[MODEL SWITCHER] Model references are invalid (UI objects or missing). " +
                         "Auto-wiring the child model instead; capsule/VRChat toggling is disabled.");

        Animator activeAnimator = AvatarRigLocator.FindBestAnimator(transform);
        if (activeAnimator == null)
        {
            Debug.LogError("[MODEL SWITCHER] No Animator found under the avatar container.");
            return;
        }

        RuntimeAnimatorController correctController =
            Resources.Load<RuntimeAnimatorController>("AvatarAnimatorController");
        if (correctController != null && activeAnimator.runtimeAnimatorController != correctController)
            activeAnimator.runtimeAnimatorController = correctController;
        activeAnimator.applyRootMotion = false;

        // Route bio-luminescence / fade to the model's actual renderer
        SkinnedMeshRenderer skinned = activeAnimator.GetComponentInChildren<SkinnedMeshRenderer>(true);
        MeshRenderer mesh = skinned == null ? activeAnimator.GetComponentInChildren<MeshRenderer>(true) : null;

        if (gameStateController != null)
            gameStateController.UpdateActiveRenderer(mesh, skinned);
        if (visualsController != null)
            visualsController.UpdateActiveRenderer(mesh, skinned);

        // IK relay so OnAnimatorIK reaches the overtake controller
        OvertakeBehaviourController overtakeController = GetComponent<OvertakeBehaviourController>();
        AvatarIKRelay relay = activeAnimator.GetComponent<AvatarIKRelay>();
        if (relay == null)
            relay = activeAnimator.gameObject.AddComponent<AvatarIKRelay>();
        relay.TargetController = overtakeController;

        if (overtakeController != null)
            overtakeController.UpdateActiveAnimator(activeAnimator);
    }
}