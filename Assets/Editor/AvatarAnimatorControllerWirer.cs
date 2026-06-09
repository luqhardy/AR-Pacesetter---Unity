using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;

/// <summary>
/// Wires the AvatarAnimatorController to the Avatar_Container prefab.
/// Runs automatically after controller generation completes.
/// </summary>
[InitializeOnLoad]
public class AvatarAnimatorControllerWirer
{
    private const string CONTROLLER_PATH = "Assets/_Project/Resources/AvatarAnimatorController.controller";
    private const string AVATAR_PREFAB_PATH = "Assets/_Project/Prefabs/Avatar_Container.prefab";

    static AvatarAnimatorControllerWirer()
    {
        EditorApplication.delayCall += WireControllerToAvatar;
    }

    private static void WireControllerToAvatar()
    {
        // Check if controller exists
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH);
        if (controller == null)
        {
            Debug.Log("[AvatarAnimatorControllerWirer] Controller not ready yet. Will retry on next domain reload.");
            return;
        }

        // Load the Avatar_Container prefab
        var avatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AVATAR_PREFAB_PATH);
        if (avatarPrefab == null)
        {
            Debug.LogError($"[AvatarAnimatorControllerWirer] Could not find Avatar_Container prefab at {AVATAR_PREFAB_PATH}");
            return;
        }

        // Find the Animator component on the prefab
        var animator = avatarPrefab.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("[AvatarAnimatorControllerWirer] Avatar_Container does not have an Animator component!");
            return;
        }

        // Assign the controller
        animator.runtimeAnimatorController = controller;

        // Ensure Avatar Type is set to Humanoid
        var modelImporter = AssetImporter.GetAtPath(AVATAR_PREFAB_PATH) as ModelImporter;
        if (modelImporter != null)
        {
            // Note: Model importer is for FBX files, not prefabs. Just log for now.
        }

        // Mark prefab as modified
        EditorUtility.SetDirty(avatarPrefab);
        AssetDatabase.SaveAssets();

        Debug.Log("✓ [AvatarAnimatorControllerWirer] Successfully wired AvatarAnimatorController to Avatar_Container!");
        Debug.Log($"   Animator.runtimeAnimatorController: {animator.runtimeAnimatorController.name}");
    }

    [MenuItem("Assets/Wire Avatar Animator Controller (Manual)")]
    public static void ManualWire()
    {
        WireControllerToAvatar();
    }
}
