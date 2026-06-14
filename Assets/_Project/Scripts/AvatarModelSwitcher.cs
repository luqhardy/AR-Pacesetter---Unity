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
        if (defaultCapsuleObject == null || customVRChatObject == null) return;

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
        if (activeAnimator != null)
        {
            RuntimeAnimatorController correctController = Resources.Load<RuntimeAnimatorController>("AvatarAnimatorController");
            if (activeAnimator.runtimeAnimatorController != correctController)
            {
                activeAnimator.runtimeAnimatorController = correctController;
            }
            // Prevent Root Motion from conflicting with our scripted movement
            activeAnimator.applyRootMotion = false;
        }
        
        OvertakeBehaviourController overtakeController = GetComponent<OvertakeBehaviourController>();
        if (overtakeController != null)
        {
            overtakeController.UpdateActiveAnimator(activeAnimator);
        }
    }
}