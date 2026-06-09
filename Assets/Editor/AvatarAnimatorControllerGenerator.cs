using UnityEngine;
using UnityEditor.Animations;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Generates the AvatarAnimatorController with all parameters, states, and transitions.
/// Runs automatically on editor startup if controller doesn't exist.
/// Manual run via: Menu > Assets > Create Avatar Animator Controller
/// </summary>
[InitializeOnLoad]
public class AvatarAnimatorControllerGenerator
{
    private const string CONTROLLER_PATH = "Assets/_Project/Resources/AvatarAnimatorController.controller";
    private const string Y_BOT_IDLE = "Assets/Y Bot@Idle.fbx";
    private const string Y_BOT_JOGGING = "Assets/Y Bot@Jogging.fbx";
    private const string Y_BOT_RUNNING = "Assets/Y Bot@Running.fbx";
    private const string Y_BOT_RUNNING2 = "Assets/Y Bot@Running2.fbx";

    static AvatarAnimatorControllerGenerator()
    {
        EditorApplication.delayCall += InitializeController;
    }

    private static void InitializeController()
    {
        // Only generate if controller doesn't exist
        if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH))
        {
            Debug.Log("[AvatarAnimatorControllerGenerator] Controller not found. Generating...");
            GenerateAvatarAnimatorController();
        }
    }

    [MenuItem("Assets/Create Avatar Animator Controller")]
    public static void GenerateAvatarAnimatorController()
    {
        // Ensure Resources folder exists
        string resourcesPath = "Assets/_Project/Resources";
        if (!AssetDatabase.IsValidFolder(resourcesPath))
        {
            AssetDatabase.CreateFolder("Assets/_Project", "Resources");
            Debug.Log($"Created folder: {resourcesPath}");
        }

        // Delete existing controller if it exists
        var existingController = AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH);
        if (existingController != null)
        {
            AssetDatabase.DeleteAsset(CONTROLLER_PATH);
            Debug.Log("Deleted existing AvatarAnimatorController");
        }

        // Create new controller
        var controller = AnimatorController.CreateAnimatorControllerAtPath(CONTROLLER_PATH);
        Debug.Log($"Created AnimatorController at {CONTROLLER_PATH}");

        // Add parameters
        AddParameters(controller);

        // Create state machine layers
        CreateAnimationLayers(controller);

        // Save and refresh
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("✓ AvatarAnimatorController generation complete!");
    }

    private static void AddParameters(AnimatorController controller)
    {
        // Float: Speed
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

        // Bool: IsHalted
        controller.AddParameter("IsHalted", AnimatorControllerParameterType.Bool);

        // Bool: IsInPlaceJog (redundant but used by GroundSnap.cs)
        controller.AddParameter("IsInPlaceJog", AnimatorControllerParameterType.Bool);

        // Triggers
        controller.AddParameter("Overtaken", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Sprint", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("RunResume", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Nod", AnimatorControllerParameterType.Trigger);

        Debug.Log("✓ Added 7 animator parameters");
    }

    private static void CreateAnimationLayers(AnimatorController controller)
    {
        // Get the base layer (default layer)
        var rootStateMachine = controller.layers[0].stateMachine;
        rootStateMachine.name = "Base Layer";

        // Load animation clips
        var idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Y_BOT_IDLE);
        var joggingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Y_BOT_JOGGING);
        var runningClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Y_BOT_RUNNING);
        var running2Clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Y_BOT_RUNNING2);

        if (idleClip == null || joggingClip == null || runningClip == null)
        {
            EditorUtility.DisplayDialog("Error", "Could not load animation clips. Ensure Y Bot FBX files are in Assets/", "OK");
            return;
        }

        // 1. Create Locomotion blend tree (1D Speed blending)
        var locomotionBlendTree = CreateLocomotionBlendTree(rootStateMachine, idleClip, joggingClip, runningClip, running2Clip);

        // 2. Create InPlaceHalt state
        var inPlaceHaltState = rootStateMachine.AddState("InPlaceHalt", new Vector3(300, 100, 0));
        inPlaceHaltState.motion = joggingClip; // In-place jog animation

        // 3. Create BeingOvertaken state
        var beingOvertakenState = rootStateMachine.AddState("BeingOvertaken", new Vector3(300, 180, 0));
        beingOvertakenState.motion = joggingClip; // Fallback; ideally replace with sidestep animation

        // 4. Create SprintSurge state
        var sprintSurgeState = rootStateMachine.AddState("SprintSurge", new Vector3(300, 260, 0));
        sprintSurgeState.motion = running2Clip ?? runningClip; // Use fastest animation

        // 5. Create Nod state
        var nodState = rootStateMachine.AddState("Nod", new Vector3(300, 340, 0));
        nodState.motion = idleClip; // Fallback; ideally use dedicated nod gesture

        // Create transitions
        CreateTransitions(rootStateMachine, locomotionBlendTree, inPlaceHaltState, beingOvertakenState, sprintSurgeState, nodState);

        Debug.Log("✓ Created state machine with 5 states and blend tree");
    }

    private static AnimatorState CreateLocomotionBlendTree(AnimatorStateMachine stateMachine, AnimationClip idle, AnimationClip jogging, AnimationClip running, AnimationClip running2)
    {
        // Create the Locomotion state with a 1D blend tree
        var locomotionState = stateMachine.AddState("Locomotion", new Vector3(50, 100, 0));

        // Create 1D blend tree
        var blendTree = new BlendTree();
        blendTree.name = "Locomotion BlendTree";
        blendTree.blendType = BlendTreeType.Simple1D;
        blendTree.blendParameter = "Speed";

        // Add motion thresholds
        var children = new List<ChildMotion>();

        // Speed = 0.0: Idle
        children.Add(new ChildMotion { motion = idle, threshold = 0.0f, timeScale = 1f });

        // Speed = 1.5: Jogging
        children.Add(new ChildMotion { motion = jogging, threshold = 1.5f, timeScale = 1f });

        // Speed = 3.0: Running
        children.Add(new ChildMotion { motion = running, threshold = 3.0f, timeScale = 1f });

        // Speed = 4.5+: Running2 (sprint feel)
        if (running2 != null)
        {
            children.Add(new ChildMotion { motion = running2, threshold = 4.5f, timeScale = 1f });
        }

        blendTree.children = children.ToArray();

        // Assign blend tree to state
        locomotionState.motion = blendTree;
        locomotionState.timeParameterActive = false;

        return locomotionState;
    }

    private static void CreateTransitions(
        AnimatorStateMachine stateMachine,
        AnimatorState locomotionState,
        AnimatorState inPlaceHaltState,
        AnimatorState beingOvertakenState,
        AnimatorState sprintSurgeState,
        AnimatorState nodState)
    {
        // Transition: Locomotion → InPlaceHalt (IsHalted == true)
        var locomotionToHalt = locomotionState.AddTransition(inPlaceHaltState);
        locomotionToHalt.AddCondition(AnimatorConditionMode.If, 0, "IsHalted");
        locomotionToHalt.hasExitTime = false;
        locomotionToHalt.exitTime = 0;
        locomotionToHalt.duration = 0.2f;

        // Transition: InPlaceHalt → Locomotion (IsHalted == false)
        var haltToLocomotion = inPlaceHaltState.AddTransition(locomotionState);
        haltToLocomotion.AddCondition(AnimatorConditionMode.IfNot, 0, "IsHalted");
        haltToLocomotion.hasExitTime = false;
        haltToLocomotion.exitTime = 0;
        haltToLocomotion.duration = 0.25f;

        // Transition: Locomotion → BeingOvertaken (Overtaken trigger)
        var locomotionToOvertaken = locomotionState.AddTransition(beingOvertakenState);
        locomotionToOvertaken.AddCondition(AnimatorConditionMode.If, 0, "Overtaken");
        locomotionToOvertaken.hasExitTime = false;
        locomotionToOvertaken.exitTime = 0;
        locomotionToOvertaken.duration = 0.25f;

        // Transition: BeingOvertaken → Locomotion (RunResume trigger)
        var overtakenToLocomotion = beingOvertakenState.AddTransition(locomotionState);
        overtakenToLocomotion.AddCondition(AnimatorConditionMode.If, 0, "RunResume");
        overtakenToLocomotion.hasExitTime = false;
        overtakenToLocomotion.exitTime = 0;
        overtakenToLocomotion.duration = 0.3f;

        // Transition: Locomotion → SprintSurge (Sprint trigger)
        var locomotionToSprint = locomotionState.AddTransition(sprintSurgeState);
        locomotionToSprint.AddCondition(AnimatorConditionMode.If, 0, "Sprint");
        locomotionToSprint.hasExitTime = false;
        locomotionToSprint.exitTime = 0;
        locomotionToSprint.duration = 0.2f;

        // Transition: SprintSurge → Locomotion (RunResume trigger)
        var sprintToLocomotion = sprintSurgeState.AddTransition(locomotionState);
        sprintToLocomotion.AddCondition(AnimatorConditionMode.If, 0, "RunResume");
        sprintToLocomotion.hasExitTime = false;
        sprintToLocomotion.exitTime = 0;
        sprintToLocomotion.duration = 0.3f;

        // Transition: Any State → Nod (Nod trigger)
        var anyStateToNod = stateMachine.AddAnyStateTransition(nodState);
        anyStateToNod.AddCondition(AnimatorConditionMode.If, 0, "Nod");
        anyStateToNod.hasExitTime = false;
        anyStateToNod.exitTime = 0;
        anyStateToNod.duration = 0.1f;

        // Transition: Nod → Locomotion (has exit time at 90%)
        var nodToLocomotion = nodState.AddTransition(locomotionState);
        nodToLocomotion.hasExitTime = true;
        nodToLocomotion.exitTime = 0.9f;
        nodToLocomotion.duration = 0.25f;

        Debug.Log("✓ Created all state transitions");
    }
}
