#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 展示ブースの体験モードをエディタから起動する(Macが無いときの予備: Windowsノートにグラスを挿し、
/// Game ビューをグラス側の画面に出す)。来場者の移動は追えないので「立ったまま」だけを置く。
/// 実機では Swift の StartBoothDemo が同じ <see cref="BoothDemoController"/> を起動する。
/// </summary>
[InitializeOnLoad]
public static class BoothDemoMenu
{
    private const string MenuRoot = "Tools/AR Pacesetter/Booth Demo/";
    private const string PendingStartKey = "AR_PACESETTER_BOOTH_DEMO_PENDING_START";
    private const double WaitTimeoutSeconds = 15.0;

    private static bool _waiting;
    private static double _waitDeadline;

    static BoothDemoMenu()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem(MenuRoot + "Start Standing", priority = 1)]
    private static void StartStanding()
    {
        if (!EditorApplication.isPlaying)
        {
            SessionState.SetBool(PendingStartKey, true);
            Debug.Log("[BOOTH DEMO] Play Mode に入り、立ったまま体験を自動で開始します。");
            EditorApplication.EnterPlaymode();
            return;
        }

        BeginWhenReady();
    }

    [MenuItem(MenuRoot + "Stop", priority = 20)]
    private static void Stop()
    {
        BoothDemoController demo = Object.FindFirstObjectByType<BoothDemoController>(FindObjectsInactive.Include);
        if (demo != null)
            demo.Stop();
    }

    [MenuItem(MenuRoot + "Stop", true)]
    private static bool ValidateStop()
    {
        if (!EditorApplication.isPlaying) return false;
        BoothDemoController demo = Object.FindFirstObjectByType<BoothDemoController>(FindObjectsInactive.Include);
        return demo != null && demo.IsRunning;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingStartKey, false))
            return;
        SessionState.SetBool(PendingStartKey, false);
        BeginWhenReady();
    }

    private static void BeginWhenReady()
    {
        _waitDeadline = EditorApplication.timeSinceStartup + WaitTimeoutSeconds;
        if (_waiting) return;
        _waiting = true;
        EditorApplication.update += WaitForRuntimeSystems;
    }

    private static void WaitForRuntimeSystems()
    {
        if (!EditorApplication.isPlaying)
        {
            StopWaiting();
            return;
        }

        // ブートストラップがブリッジとデモ制御を生成するまで待つ
        BoothDemoController demo = Object.FindFirstObjectByType<BoothDemoController>(FindObjectsInactive.Include);
        bool bridgeReady = Object.FindFirstObjectByType<ARSessionManagerBridge>(FindObjectsInactive.Include) != null;
        if (demo != null && bridgeReady)
        {
            StopWaiting();
            // グラス接続時と同じ描画(XREAL One の画角・黒背景=透過)にする。Game ビューを
            // グラス側の画面へ出せば、iPhone無しでも見え方はほぼ同じになる
            DeviceManagerBridge device = Object.FindFirstObjectByType<DeviceManagerBridge>(FindObjectsInactive.Include);
            if (device != null)
                device.OnSwiftCommand("{\"command\":\"ConnectXREAL\",\"pixelWidth\":1920,\"pixelHeight\":1080}");
            demo.Begin(BoothDemoController.Mode.Standing);
            return;
        }

        if (EditorApplication.timeSinceStartup >= _waitDeadline)
        {
            StopWaiting();
            Debug.LogError("[BOOTH DEMO] ARSessionManagerBridge / BoothDemoController が見つかりません。SampleScene を開いてください。");
        }
    }

    private static void StopWaiting()
    {
        if (!_waiting) return;
        _waiting = false;
        EditorApplication.update -= WaitForRuntimeSystems;
    }
}
#endif
