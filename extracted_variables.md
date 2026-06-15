# Extracted Variables (変数一覧)

This report lists all extracted variables (fields and properties) from C# scripts within the project.

## Core Project Scripts

### [AvatarAnimatorControllerGenerator.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Editor/AvatarAnimatorControllerGenerator.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `CONTROLLER_PATH` | `string` | `private const` |
| `Y_BOT_IDLE` | `string` | `private const` |
| `Y_BOT_JOGGING` | `string` | `private const` |
| `Y_BOT_RUNNING` | `string` | `private const` |
| `Y_BOT_RUNNING2` | `string` | `private const` |

### [AvatarAnimatorControllerWirer.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Editor/AvatarAnimatorControllerWirer.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `CONTROLLER_PATH` | `string` | `private const` |
| `AVATAR_PREFAB_PATH` | `string` | `private const` |

### [ARStarterAssetsSampleProjectValidation.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Samples/XR Interaction Toolkit/3.3.0/AR Starter Assets/Editor/Scripts/ARStarterAssetsSampleProjectValidation.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `k_SampleDisplayName` | `string` | `const` |
| `k_Category` | `string` | `const` |
| `k_StarterAssetsSampleName` | `string` | `const` |
| `k_XRIPackageName` | `string` | `const` |
| `k_ARFPackageName` | `string` | `const` |
| `k_ARFPackageMinVersionString` | `string` | `const` |
| `k_TimeOutInSeconds` | `float` | `const` |
| `s_ARFPackageMinVersion` | `PackageVersion` | `static readonly` |
| `s_BuildTargetGroups` | `BuildTargetGroup[]` | `static readonly` |
| `s_BuildValidationRules` | `List<BuildValidationRule>` | `static readonly` |
| `s_ARFPackageAddRequest` | `AddRequest` | `static` |

### [StarterAssetsSampleProjectValidation.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/Editor/Scripts/StarterAssetsSampleProjectValidation.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `k_Category` | `string` | `const` |
| `k_StarterAssetsSampleName` | `string` | `const` |
| `k_TeleportLayerName` | `string` | `const` |
| `k_TeleportLayerIndex` | `int` | `const` |
| `k_ProjectValidationSettingsPath` | `string` | `const` |
| `k_ShaderGraphPackageName` | `string` | `const` |
| `k_InputSystemPackageName` | `string` | `const` |
| `s_RecommendedPackageVersion` | `PackageVersion` | `static readonly` |
| `k_InputActionAssetName` | `string` | `const` |
| `k_InputActionAssetGuid` | `string` | `const` |
| `s_BuildTargetGroups` | `BuildTargetGroup[]` | `static readonly` |
| `s_BuildValidationRules` | `List<BuildValidationRule>` | `static readonly` |
| `s_ShaderGraphPackageAddRequest` | `AddRequest` | `static` |
| `s_InputSystemPackageAddRequest` | `AddRequest` | `static` |

### [AnalyticsManager.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/AnalyticsManager.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `userCamera` | `Transform` | `private` |
| `avatarContainer` | `Transform` | `private` |
| `ambientTemperatureCelsius` | `float` | `private` |
| `_totalSyncSum` | `float` | `private` |
| `_totalSyncCount` | `int` | `private` |
| `_currentKmSyncSum` | `float` | `private` |
| `_currentKmSyncCount` | `int` | `private` |
| `_lastEvaluatedKilometerMarker` | `float` | `private` |
| `_cumulativeFatigueIndex` | `float` | `private` |
| `OnSplitReached` | `event SplitReachedDelegate` | `public` |

### [AvatarEngine.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/AvatarEngine.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `userCamera` | `Transform` | `private` |
| `gameStateController` | `GameStateController` | `private` |
| `targetPaceMinutesPerKm` | `float` | `private` |
| `leadDistanceMeters` | `float` | `private` |
| `slowdownFraction` | `float` | `private` |
| `maxLeadBeforeSlow` | `float` | `private` |
| `catchupBoostFraction` | `float` | `private` |
| `catchupTriggerUnderrun` | `float` | `private` |
| `maxTurnDegreesPerSecond` | `float` | `private` |
| `overtakenConfirmSeconds` | `float` | `private` |
| `sprintMultiplier` | `float` | `private` |
| `sprintHoldSeconds` | `float` | `private` |
| `overtakenSidestepMeters` | `float` | `private` |
| `_isKalmanInitialized` | `bool` | `private` |
| `_targetPacingPosition` | `Vector3` | `private` |
| `_calculatedTargetSpeedMetersPerSecond` | `float` | `private` |
| `_lastFrameUserPosition` | `Vector3` | `private` |
| `delta` | `Vector3` | `public` |
| `time` | `float` | `public` |
| `_movementHistory` | `Queue<MovementFrame>` | `private` |
| `dir` | `Vector3` | `public` |
| `timestamp` | `float` | `public` |
| `_headingHistory` | `Queue<WeightedHeading>` | `private` |
| `_currentLinearDirection` | `Vector3` | `private` |
| `_smoothRotation` | `Quaternion` | `private` |
| `_lastFrameDeltaTime` | `float` | `private` |
| `_lastCleanKalmanVelocity` | `Vector3` | `private` |
| `JitterThresholdSeconds` | `float` | `private const` |
| `_effectiveSpeedMultiplier` | `float` | `private` |
| `_overtakeState` | `OvertakeState` | `private` |
| `_overtakenTimer` | `float` | `private` |
| `_sprintTimer` | `float` | `private` |
| `_sidestepOffset` | `Vector3` | `private` |
| `IsHalted` | `bool` | `public` |
| `TargetPaceMinutesPerKm` | `float` | `public` |
| `CurrentOvertakeState` | `OvertakeState` | `public` |

### [AvatarIKRelay.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/AvatarIKRelay.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `TargetController` | `OvertakeBehaviourController` | `public` |

### [AvatarModelSwitcher.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/AvatarModelSwitcher.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `activeAvatar` | `AvatarType` | `public` |
| `defaultCapsuleObject` | `GameObject` | `private` |
| `customVRChatObject` | `GameObject` | `private` |
| `gameStateController` | `GameStateController` | `private` |
| `visualsController` | `AvatarVisualsAndActions` | `private` |

### [AvatarVisualsAndActions.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/AvatarVisualsAndActions.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `userCamera` | `Transform` | `private` |
| `avatarRenderer` | `MeshRenderer` | `private` |
| `_avatarSkinnedRenderer` | `SkinnedMeshRenderer` | `private` |
| `baseIntensity` | `float` | `private` |
| `pulseAmplitude` | `float` | `private` |
| `_glowMaterial` | `Material` | `private` |
| `_currentHeartRate` | `int` | `private` |
| `_normalCyan` | `Color` | `private` |
| `_amberWarning` | `Color` | `private` |

### [GameStateController.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/GameStateController.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `currentState` | `ARVisionState` | `public` |
| `avatarTarget` | `GameObject` | `private` |
| `avatarRenderer` | `MeshRenderer` | `private` |
| `avatarEngine` | `AvatarEngine` | `private` |
| `_avatarSkinnedRenderer` | `SkinnedMeshRenderer` | `private` |
| `_gpsLostTimer` | `float` | `private` |
| `_fadeCoroutine` | `Coroutine` | `private` |
| `SimulatedGPSAccuracyRadius` | `float` | `public` |
| `_activeMaterial` | `Material` | `private` |

### [GroundSnap.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/GroundSnap.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `userCamera` | `Transform` | `private` |
| `avatarEngine` | `AvatarEngine` | `private` |
| `smoothTime` | `float` | `private` |
| `stepThreshold` | `float` | `private` |
| `environmentLayerMask` | `LayerMask` | `private` |
| `obstacleLayerMask` | `LayerMask` | `private` |
| `obstacleDetectionDistance` | `float` | `private` |
| `minObstacleHeight` | `float` | `private` |
| `_targetY` | `float` | `private` |
| `_currentYVelocity` | `float` | `private` |
| `_simulateObstacleActive` | `bool` | `private` |
| `_wasHaltedLastFrame` | `bool` | `private` |
| `_isEasing` | `bool` | `private` |

### [HeartRateReceiver.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/HeartRateReceiver.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `visualsEngine` | `AvatarVisualsAndActions` | `private` |
| `hudManager` | `PeripheralHUDManager` | `private` |

### [LatencyBenchmarkRunner.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/LatencyBenchmarkRunner.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `avatarEngine` | `AvatarEngine` | `private` |
| `userCamera` | `Transform` | `private` |
| `benchmarkHUDText` | `TextMeshProUGUI` | `private` |
| `rollingWindowFrames` | `int` | `private` |
| `budgetMs` | `float` | `private` |
| `BudgetImuMs` | `float` | `private const` |
| `BudgetKalmanMs` | `float` | `private const` |
| `BudgetFrameMs` | `float` | `private const` |
| `BudgetSubmitMs` | `float` | `private const` |
| `_benchmarkActive` | `bool` | `private` |
| `_sw` | `Stopwatch` | `private readonly` |
| `_imuTimes` | `Queue<double>` | `private` |
| `_kalmanTimes` | `Queue<double>` | `private` |
| `_frameTimes` | `Queue<double>` | `private` |
| `_submitTimes` | `Queue<double>` | `private` |
| `_totalTimes` | `Queue<double>` | `private` |
| `_frameCount` | `int` | `private` |
| `_overBudgetCount` | `int` | `private` |
| `_simulatedImuPosition` | `Vector3` | `private` |
| `_t_total` | `double _t_imu, _t_kalman, _t_frame, _t_submit,` | `private` |
| `Q` | `float` | `const` |
| `R` | `float` | `const` |

### [OvertakeBehaviourController.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/OvertakeBehaviourController.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `userCamera` | `Transform` | `private` |
| `avatarAnimator` | `Animator` | `private` |
| `headTurnSpeed` | `float` | `private` |
| `sprintParticles` | `ParticleSystem` | `private` |
| `overtakenParticles` | `ParticleSystem` | `private` |
| `_engine` | `AvatarEngine` | `private` |
| `_isLookingAtUser` | `bool` | `private` |
| `_defaultHeadRot` | `Quaternion` | `private` |
| `ActiveAnimator` | `Animator` | `public` |

### [PaceCalibrationController.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/PaceCalibrationController.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `avatarEngine` | `AvatarEngine` | `private` |
| `paceSlider` | `Slider` | `private` |
| `paceDisplayLabel` | `TextMeshProUGUI` | `private` |

### [PeripheralHUDManager.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/PeripheralHUDManager.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `textHeartRate` | `TextMeshProUGUI` | `private` |
| `textTime` | `TextMeshProUGUI` | `private` |
| `textDistance` | `TextMeshProUGUI` | `private` |
| `textPace` | `TextMeshProUGUI` | `private` |
| `textPitch` | `TextMeshProUGUI` | `private` |
| `textSyncRate` | `TextMeshProUGUI` | `private` |
| `textFatigueIndex` | `TextMeshProUGUI` | `private` |
| `textGrade` | `TextMeshProUGUI` | `private` |
| `textNotificationAlert` | `TextMeshProUGUI` | `private` |
| `userCamera` | `Transform` | `private` |
| `avatarEngine` | `AvatarEngine` | `private` |
| `analytics` | `AnalyticsManager` | `private` |
| `_elapsedTimeSeconds` | `float` | `private` |
| `_cumulativeDistanceMeters` | `float` | `private` |
| `_lastUserPosition` | `Vector3` | `private` |
| `_simulatedHeartRate` | `int` | `private` |
| `_simulatedPitch` | `float` | `private` |
| `_splitAlertCoroutine` | `Coroutine` | `private` |

### [SafetyAndSystemController.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/SafetyAndSystemController.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `avatarContainer` | `GameObject` | `private` |
| `stateController` | `GameStateController` | `private` |
| `userCamera` | `Transform` | `private` |
| `avatarEngine` | `AvatarEngine` | `private` |
| `redFlashScreenOverlay` | `Image` | `private` |
| `minimalistHudPanel` | `GameObject` | `private` |
| `alertAudioSource` | `AudioSource` | `private` |
| `ttcWarningChime` | `AudioClip` | `private` |
| `ttcDangerThreshold` | `float` | `private` |
| `ttcScanRadius` | `float` | `private` |
| `ttcScanRange` | `float` | `private` |
| `obstacleLayerMask` | `LayerMask` | `private` |
| `_hasEvacuatedDueToBattery` | `bool` | `private` |
| `_isTtcWarningActive` | `bool` | `private` |
| `_simulateTtcThreat` | `bool` | `private` |

### [SilentRouteRecoverer.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/_Project/Scripts/SilentRouteRecoverer.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `userCamera` | `Transform` | `private` |
| `avatarEngine` | `AvatarEngine` | `private` |
| `deviationThresholdMeters` | `float` | `private` |
| `recoveryTrailingDistance` | `float` | `private` |
| `routeWaypoints` | `Transform[]` | `private` |
| `_isRecoveringSilently` | `bool` | `private` |
| `_nearestSegmentIndex` | `int` | `private` |

## Other / Sample Scripts

### [ARPlaneMeshVisualizerFader.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/MobileARTemplateAssets/Scripts/ARPlaneMeshVisualizerFader.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `m_AlphaTweenableVariable` | `FloatTweenableVariable` | `readonly` |

### [ARTemplateMenuManager.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/MobileARTemplateAssets/Scripts/ARTemplateMenuManager.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `m_ARPlanes` | `List<ARPlane>` | `readonly` |
| `m_ARPlaneMeshVisualizers` | `Dictionary<ARPlane, ARPlaneMeshVisualizer>` | `readonly` |
| `m_ARPlaneMeshVisualizerFaders` | `Dictionary<ARPlane, ARPlaneMeshVisualizerFader>` | `readonly` |

### [GoalManager.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/MobileARTemplateAssets/Scripts/GoalManager.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `Completed` | `bool` | `public` |
| `stepObject` | `GameObject` | `public` |
| `buttonText` | `string` | `public` |
| `includeSkipButton` | `bool` | `public` |
| `k_NumberOfSurfacesTappedToCompleteGoal` | `int` | `const` |

### [CutoutMaskUI.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/MobileARTemplateAssets/UI/Scripts/CutoutMaskUI.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `k_StencilComp` | `int` | `static readonly` |

### [ARFeatheredPlaneMeshVisualizer.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Samples/XR Interaction Toolkit/3.3.0/AR Starter Assets/Scripts/ARFeatheredPlaneMeshVisualizer.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `s_FeatheringUVs` | `List<Vector3>` | `static` |
| `s_Vertices` | `List<Vector3>` | `static` |

### [ControllerInputActionManager.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/Scripts/ControllerInputActionManager.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `m_LocomotionUsers` | `HashSet<InputAction>` | `readonly` |
| `m_BindingsGroup` | `BindingsGroup` | `readonly` |
| `sqrStickReleaseThreshold` | `float` | `const` |

### [GazeInputManager.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/Scripts/GazeInputManager.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `k_EyeGazeLayoutName` | `string` | `const` |

### [MaterialPipelineHandler.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/Scripts/MaterialPipelineHandler.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `material` | `Material` | `public` |
| `useSRPShaderName` | `bool` | `public` |
| `scriptableRenderPipelineShaderName` | `string` | `public` |
| `scriptableRenderPipelineShader` | `Shader` | `public` |
| `useBuiltinShaderName` | `bool` | `public` |
| `builtInPipelineShaderName` | `string` | `public` |
| `builtInPipelineShader` | `Shader` | `public` |
| `baseFieldCount` | `int` | `const` |

### [ObjectSpawner.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/Scripts/ObjectSpawner.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `isSpawnOptionRandomized` | `bool` | `public` |
| `objectSpawned` | `event Action<GameObject>` | `public` |

### [PermissionsManager.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/Scripts/PermissionsManager.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `k_DefaultPermissionId` | `string` | `const` |
| `platformType` | `XRPlatformType` | `public` |
| `permissions` | `List<PermissionRequest>` | `public` |
| `permissionId` | `string` | `public` |
| `enabled` | `bool` | `public` |
| `requested` | `bool` | `public` |
| `responseReceived` | `bool` | `public` |
| `granted` | `bool` | `public` |
| `onPermissionGranted` | `UnityEvent<string>` | `public` |
| `onPermissionDenied` | `UnityEvent<string>` | `public` |

### [PlatformUnderstanding.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/Scripts/PlatformUnderstanding.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `k_RuntimeNameMeta` | `string` | `const` |
| `k_RuntimeNameAndroidXR` | `string` | `const` |
| `s_CurrentPlatform` | `XRPlatformType` | `static` |
| `s_Initialized` | `bool` | `static` |

### [RotationAxisLockGrabTransformer.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/Scripts/RotationAxisLockGrabTransformer.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `registrationMode` | `override RegistrationMode` | `protected` |

### [XRPokeFollowAffordance.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Samples/XR Interaction Toolkit/3.3.0/Starter Assets/Scripts/XRPokeFollowAffordance.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `m_TransformTweenableVariable` | `Vector3TweenableVariable` | `readonly` |
| `m_BindingsGroup` | `BindingsGroup` | `readonly` |

### [SceneTemplate_RotateCube.cs](file:///Users/luqmanhardy/Documents/GitHub/AR-Pacesetter---Unity/Assets/Settings/Project Configuration/SceneTemplate_RotateCube.cs)

| Variable Name | Type | Modifiers |
|---|---|---|
| `rotateSpeed` | `float` | `public` |
| `objectRotation` | `Vector3` | `public` |

