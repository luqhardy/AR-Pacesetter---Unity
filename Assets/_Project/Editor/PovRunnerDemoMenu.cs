#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>One-click entry points for the non-production POV runner demonstration.</summary>
[InitializeOnLoad]
public static class PovRunnerDemoMenu
{
    private const string MenuRoot = "Tools/AR Pacesetter/POV Demo/";
    private const string PendingStartKey = "AR_PACESETTER_POV_DEMO_PENDING_START";
    private const double BridgeWaitTimeoutSeconds = 15.0;

    private static bool _waitingForRuntimeSystems;
    private static double _waitDeadline;

    static PovRunnerDemoMenu()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem(MenuRoot + "Start Automatic 60m Run", priority = 1)]
    private static void StartAutomaticRun()
    {
        if (!EditorApplication.isPlaying)
        {
            SessionState.SetBool(PendingStartKey, true);
            Debug.Log("[POV DEMO] Entering Play Mode; the invisible 60m runner will start automatically.");
            EditorApplication.EnterPlaymode();
            return;
        }

        BeginWhenRuntimeSystemsAreReady();
    }

    [MenuItem(MenuRoot + "Reach Goal Now", priority = 20)]
    private static void ReachGoalNow()
    {
        PovRunnerDemoController demo = Object.FindFirstObjectByType<PovRunnerDemoController>(
            FindObjectsInactive.Include);
        if (demo == null || !demo.IsRunning)
        {
            Debug.LogWarning("[POV DEMO] No automatic demo is currently running.");
            return;
        }

        demo.ReachGoalNow();
    }

    [MenuItem(MenuRoot + "Stop Demo", priority = 21)]
    private static void StopDemo()
    {
        PovRunnerDemoController demo = Object.FindFirstObjectByType<PovRunnerDemoController>(
            FindObjectsInactive.Include);
        if (demo == null || !demo.IsRunning)
        {
            Debug.LogWarning("[POV DEMO] No automatic demo is currently running.");
            return;
        }

        demo.StopDemo();
    }

    [MenuItem(MenuRoot + "Reach Goal Now", true)]
    [MenuItem(MenuRoot + "Stop Demo", true)]
    private static bool ValidateActiveDemoCommands()
    {
        if (!EditorApplication.isPlaying)
            return false;
        PovRunnerDemoController demo = Object.FindFirstObjectByType<PovRunnerDemoController>(
            FindObjectsInactive.Include);
        return demo != null && demo.IsRunning;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode
            || !SessionState.GetBool(PendingStartKey, false))
            return;

        SessionState.SetBool(PendingStartKey, false);
        BeginWhenRuntimeSystemsAreReady();
    }

    private static void BeginWhenRuntimeSystemsAreReady()
    {
        _waitDeadline = EditorApplication.timeSinceStartup + BridgeWaitTimeoutSeconds;
        if (_waitingForRuntimeSystems)
            return;

        _waitingForRuntimeSystems = true;
        EditorApplication.update += WaitForRuntimeSystems;
    }

    private static void WaitForRuntimeSystems()
    {
        if (!EditorApplication.isPlaying)
        {
            StopWaiting();
            return;
        }

        ARSessionManagerBridge bridge = Object.FindFirstObjectByType<ARSessionManagerBridge>(
            FindObjectsInactive.Include);
        if (bridge != null)
        {
            StopWaiting();
            StartRuntimeDemo(bridge);
            return;
        }

        if (EditorApplication.timeSinceStartup >= _waitDeadline)
        {
            StopWaiting();
            Debug.LogError("[POV DEMO] Timed out waiting for ARSessionManagerBridge. Make sure SampleScene is open.");
        }
    }

    private static void StartRuntimeDemo(ARSessionManagerBridge bridge)
    {
        PovRunnerDemoController demo = Object.FindFirstObjectByType<PovRunnerDemoController>(
            FindObjectsInactive.Include);
        if (demo == null)
        {
            var go = new GameObject("[Editor] Invisible POV Runner Demo")
            {
                // Runtime-only helper: it cannot accidentally be saved into the production scene.
                hideFlags = HideFlags.DontSave
            };
            demo = go.AddComponent<PovRunnerDemoController>();
        }

        demo.StartDemo();
    }

    private static void StopWaiting()
    {
        if (!_waitingForRuntimeSystems)
            return;
        _waitingForRuntimeSystems = false;
        EditorApplication.update -= WaitForRuntimeSystems;
    }
}
#endif
