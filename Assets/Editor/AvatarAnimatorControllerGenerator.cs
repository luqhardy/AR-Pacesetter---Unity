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

    // 基本設計書 §7.3 の速度閾値(km/h → m/s)
    private const float WalkThresholdMetersPerSec = 0.0278f;   // 0.1 km/h
    private const float RunThresholdMetersPerSec = 1.3889f;    // 5.0 km/h
    private const float SprintBlendMetersPerSec = 4.1667f;     // 15.0 km/h(視覚バリエーション)
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
        // Enforce humanoid type on models and animations before generator runs
        PrepareRigs();

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

        // Float: PlaybackSpeed (§7.3 歩行の再生速度同期)。
        // 既定1.0 — コード側が未設定でもアニメーションが停止しないようにする
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = "PlaybackSpeed",
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 1.0f
        });

        // Bool: IsHalted
        controller.AddParameter("IsHalted", AnimatorControllerParameterType.Bool);

        // Bool: IsInPlaceJog (redundant but used by GroundSnap.cs)
        controller.AddParameter("IsInPlaceJog", AnimatorControllerParameterType.Bool);

        // Triggers
        controller.AddParameter("Overtaken", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Sprint", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("RunResume", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Nod", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Beckon", AnimatorControllerParameterType.Trigger);        // 離隔待機の手招き (AvatarEngine)
        controller.AddParameter("Goodbye", AnimatorControllerParameterType.Trigger);       // 終了時の挨拶 (AvatarVFXController)
        controller.AddParameter("CalmDownSign", AnimatorControllerParameterType.Trigger);  // バイタル警告 (AvatarVisualsAndActions)

        Debug.Log("✓ Added 11 animator parameters");
    }

    private static void CreateAnimationLayers(AnimatorController controller)
    {
        // Enable IK Pass on base layer
        var layers = controller.layers;
        if (layers.Length > 0)
        {
            layers[0].iKPass = true;
            controller.layers = layers;
        }

        // Get the base layer (default layer)
        var rootStateMachine = controller.layers[0].stateMachine;
        rootStateMachine.name = "Base Layer";

        // Load animation clips properly
        var idleClip = LoadAnimationClipFromFBX(Y_BOT_IDLE);
        var joggingClip = LoadAnimationClipFromFBX(Y_BOT_JOGGING);
        var runningClip = LoadAnimationClipFromFBX(Y_BOT_RUNNING);
        var running2Clip = LoadAnimationClipFromFBX(Y_BOT_RUNNING2);

        if (idleClip == null || joggingClip == null || runningClip == null)
        {
            EditorUtility.DisplayDialog("Error", "Could not load animation clips. Ensure Y Bot FBX files are in Assets/", "OK");
            return;
        }

        // 1. Create Locomotion blend tree (1D Speed blending)
        var locomotionBlendTree = CreateLocomotionBlendTree(controller, rootStateMachine, idleClip, joggingClip, runningClip, running2Clip);

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

        // 6. Beckon: 離隔待機の手招き (企画書4.1)。RunResumeで解除
        //    ※プレースホルダー。Mixamoの "Waving" / "Beckoning" 系に差し替え推奨
        var beckonState = rootStateMachine.AddState("Beckon", new Vector3(300, 420, 0));
        beckonState.motion = idleClip;

        // 7. Goodbye: 終了時の挨拶 → 消滅 (企画書4.1 VFX)
        //    ※プレースホルダー。Mixamoの "Bow" / "Waving" 系に差し替え推奨
        var goodbyeState = rootStateMachine.AddState("Goodbye", new Vector3(300, 500, 0));
        goodbyeState.motion = idleClip;

        // 8. CalmDownSign: バイタル警告のハンドサイン (企画書4.1)
        //    ※プレースホルダー。Mixamoの "Hand Raising" 系に差し替え推奨
        var calmDownState = rootStateMachine.AddState("CalmDownSign", new Vector3(300, 580, 0));
        calmDownState.motion = idleClip;

        // Create transitions
        CreateTransitions(rootStateMachine, locomotionBlendTree, inPlaceHaltState, beingOvertakenState, sprintSurgeState, nodState);
        CreateGestureTransitions(rootStateMachine, locomotionBlendTree, beckonState, goodbyeState, calmDownState);

        Debug.Log("✓ Created state machine with 8 states and blend tree");
    }

    private static AnimatorState CreateLocomotionBlendTree(AnimatorController controller, AnimatorStateMachine stateMachine, AnimationClip idle, AnimationClip jogging, AnimationClip running, AnimationClip running2)
    {
        // Create the Locomotion state with a 1D blend tree
        var locomotionState = stateMachine.AddState("Locomotion", new Vector3(50, 100, 0));

        // Create 1D blend tree
        var blendTree = new BlendTree();
        blendTree.name = "Locomotion BlendTree";
        blendTree.blendType = BlendTreeType.Simple1D;
        blendTree.blendParameter = "Speed";

        // ★必須: 自動閾値(既定ON)は子を割り当てた瞬間に閾値を[0,1]へ均等再配置して
        // しまう。設計書§7.3のkm/h基準閾値を保持するため必ず無効化する
        blendTree.useAutomaticThresholds = false;

        // ★必須: BlendTreeはコントローラのサブアセットとして登録しないと保存時に
        // 破棄され、Locomotionのm_Motionが空(fileID:0)になる=ロコモーションが
        // 一切再生されない。以前はこの登録が無く F-06 が無効化されていた
        blendTree.hideFlags = HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(blendTree, controller);

        // 基本設計書 §7.3 のモーション遷移ルール(km/h基準)を m/s のSpeedへ換算:
        //   待機(Idle) : 0 km/h
        //   歩行(Walk) : 0.1 km/h 以上 5.0 km/h 未満  → 0.0278 〜 1.3889 m/s
        //   走行(Run)  : 5.0 km/h 以上               → 1.3889 m/s 以上
        // Running2 はRun以上の高速域(15km/h≒4.17m/s)でブレンドする視覚バリエーション
        var children = new List<ChildMotion>();

        children.Add(new ChildMotion { motion = idle, threshold = 0.0f, timeScale = 1f });
        children.Add(new ChildMotion { motion = jogging, threshold = WalkThresholdMetersPerSec, timeScale = 1f });
        children.Add(new ChildMotion { motion = running, threshold = RunThresholdMetersPerSec, timeScale = 1f });

        if (running2 != null)
        {
            children.Add(new ChildMotion { motion = running2, threshold = SprintBlendMetersPerSec, timeScale = 1f });
        }

        blendTree.children = children.ToArray();

        // Assign blend tree to state
        locomotionState.motion = blendTree;
        locomotionState.timeParameterActive = false;

        // §7.3: 歩行は速度に応じて再生速度を同期させる。
        // PlaybackSpeed(既定1.0)をコード側から毎フレーム供給する
        locomotionState.speedParameterActive = true;
        locomotionState.speedParameter = "PlaybackSpeed";

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

    private static void CreateGestureTransitions(
        AnimatorStateMachine stateMachine,
        AnimatorState locomotionState,
        AnimatorState beckonState,
        AnimatorState goodbyeState,
        AnimatorState calmDownState)
    {
        // Any State → Beckon (離隔待機開始)。ユーザーが追いつくと RunResume で解除
        var anyToBeckon = stateMachine.AddAnyStateTransition(beckonState);
        anyToBeckon.AddCondition(AnimatorConditionMode.If, 0, "Beckon");
        anyToBeckon.hasExitTime = false;
        anyToBeckon.duration = 0.2f;
        anyToBeckon.canTransitionToSelf = false;

        var beckonToLocomotion = beckonState.AddTransition(locomotionState);
        beckonToLocomotion.AddCondition(AnimatorConditionMode.If, 0, "RunResume");
        beckonToLocomotion.hasExitTime = false;
        beckonToLocomotion.duration = 0.25f;

        // Any State → Goodbye (終了挨拶)。以降は消滅VFXに任せるが安全のため出口も用意
        var anyToGoodbye = stateMachine.AddAnyStateTransition(goodbyeState);
        anyToGoodbye.AddCondition(AnimatorConditionMode.If, 0, "Goodbye");
        anyToGoodbye.hasExitTime = false;
        anyToGoodbye.duration = 0.2f;
        anyToGoodbye.canTransitionToSelf = false;

        var goodbyeToLocomotion = goodbyeState.AddTransition(locomotionState);
        goodbyeToLocomotion.hasExitTime = true;
        goodbyeToLocomotion.exitTime = 0.95f;
        goodbyeToLocomotion.duration = 0.25f;

        // Any State → CalmDownSign (心拍過負荷) → 再生し終えたらLocomotionへ
        var anyToCalm = stateMachine.AddAnyStateTransition(calmDownState);
        anyToCalm.AddCondition(AnimatorConditionMode.If, 0, "CalmDownSign");
        anyToCalm.hasExitTime = false;
        anyToCalm.duration = 0.2f;
        anyToCalm.canTransitionToSelf = false;

        var calmToLocomotion = calmDownState.AddTransition(locomotionState);
        calmToLocomotion.hasExitTime = true;
        calmToLocomotion.exitTime = 0.9f;
        calmToLocomotion.duration = 0.25f;

        Debug.Log("✓ Created gesture transitions (Beckon/Goodbye/CalmDownSign)");
    }

    private static void PrepareRigs()
    {
        // Enforce humanoid type on Y Bot model first to get its avatar reference
        EnsureModelIsHumanoid("Assets/Y Bot.fbx", null, false);

        // Find the Y Bot avatar in sub-assets
        Avatar ybotAvatar = null;
        var subAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/Y Bot.fbx");
        foreach (var asset in subAssets)
        {
            if (asset is Avatar avatar)
            {
                ybotAvatar = avatar;
                break;
            }
        }

        if (ybotAvatar == null)
        {
            Debug.LogError("[AvatarAnimatorControllerGenerator] Could not find humanoid avatar on Assets/Y Bot.fbx!");
        }

        // Configure all animation FBX rigs to copy the Y Bot avatar
        EnsureModelIsHumanoid(Y_BOT_IDLE, ybotAvatar, true);
        EnsureModelIsHumanoid(Y_BOT_JOGGING, ybotAvatar, true);
        EnsureModelIsHumanoid(Y_BOT_RUNNING, ybotAvatar, true);
        EnsureModelIsHumanoid(Y_BOT_RUNNING2, ybotAvatar, true);

        // Enforce humanoid setup on the custom companion model as well
        EnsureModelIsHumanoid("Assets/Avatar.fbx", null, false);
    }

    private static void EnsureModelIsHumanoid(string assetPath, Avatar sourceAvatar, bool isAnimationOnly)
    {
        ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[RIG REPAIR] Could not find asset at path: {assetPath}");
            return;
        }

        bool changed = false;

        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            changed = true;
        }

        if (isAnimationOnly)
        {
            if (sourceAvatar != null && importer.sourceAvatar != sourceAvatar)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = sourceAvatar;
                changed = true;
            }

            // Enforce loop settings on Mixamo animations
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }
            if (clips != null && clips.Length > 0)
            {
                bool clipChanged = false;
                foreach (var c in clips)
                {
                    if (!c.loopTime || !c.loopPose)
                    {
                        c.loopTime = true;
                        c.loopPose = true;
                        clipChanged = true;
                    }
                }
                if (clipChanged)
                {
                    importer.clipAnimations = clips;
                    changed = true;
                }
            }
        }
        else
        {
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }
        }

        if (changed)
        {
            importer.SaveAndReimport();
            Debug.Log($"[RIG REPAIR] Successfully configured model/animation import settings for humanoid compatibility and looping: {assetPath}");
        }
    }

    private static AnimationClip LoadAnimationClipFromFBX(string path)
    {
        var allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
        AnimationClip fallback = null;
        
        foreach (var asset in allAssets)
        {
            if (asset is AnimationClip clip)
            {
                if (!clip.name.StartsWith("__preview__"))
                {
                    return clip; // Found the real clip
                }
                fallback = clip; // Store preview clip just in case
            }
        }
        
        if (fallback != null)
        {
            Debug.LogError($"[RIG ERROR] Could not find a real animation clip in {path}! Falling back to empty dummy clip '{fallback.name}'. Ensure you downloaded the animation from Mixamo, not just the T-Pose character!");
            return fallback;
        }

        Debug.LogError($"[RIG ERROR] The file {path} has NO animation clips whatsoever! Animations will be completely broken.");
        return null;
    }
}
