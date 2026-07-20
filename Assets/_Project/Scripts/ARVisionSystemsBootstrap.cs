using UnityEngine;

/// <summary>
/// シーンに手動配置しなくても新規マネージャー群が動作するよう、
/// AvatarEngine が存在するシーンのロード後に不足コンポーネントを自動生成する。
/// （既にシーンへ配置済みの場合は何もしない）
/// </summary>
public static class ARVisionSystemsBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureSystems()
    {
        // Only bootstrap in the pacing scene
        AvatarEngine engine = Object.FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (engine == null)
            return;

        // 要件定義 6.1: 描画頻度60fps(16.6ms/frame)を維持。
        // iOSのUnityは既定30fpsのため明示設定が必須(未設定だとM2Pが実質倍増する)。
        // モバイルでtargetFrameRateを効かせるためvSyncは無効化する
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;

        // VFX演出はアバター本体と同じGameObjectに載せる (企画書 4.1)
        if (engine.GetComponent<AvatarVFXController>() == null)
        {
            engine.gameObject.AddComponent<AvatarVFXController>();
            Debug.Log("[BOOTSTRAP] AvatarVFXController auto-attached to avatar.");
        }

        // フェイクシャドウ(企画書4.1 コア・レンダリング — AR空間の接地感)
        if (engine.GetComponent<FakeShadowRenderer>() == null)
        {
            engine.gameObject.AddComponent<FakeShadowRenderer>();
            Debug.Log("[BOOTSTRAP] FakeShadowRenderer auto-attached to avatar.");
        }

        // プロシージャルジェスチャー(手招き/挨拶/落ち着けサインの可視化)
        if (engine.GetComponent<ProceduralGestureDriver>() == null)
        {
            engine.gameObject.AddComponent<ProceduralGestureDriver>();
            Debug.Log("[BOOTSTRAP] ProceduralGestureDriver auto-attached to avatar.");
        }

        // サイレントルート復帰はアバターのtransformを操作するため同居必須。
        // シーンに未配置だと逸脱復帰機能が丸ごと不在になる(E2Eで検出)
        if (Object.FindFirstObjectByType<SilentRouteRecoverer>(FindObjectsInactive.Include) == null)
        {
            engine.gameObject.AddComponent<SilentRouteRecoverer>();
            Debug.Log("[BOOTSTRAP] SilentRouteRecoverer auto-attached to avatar.");
        }

        Ensure<SafetyEventLogger>();
        Ensure<RunAudioEngine>();
        Ensure<RunSessionController>();
        Ensure<ReadyCheckController>();
        Ensure<GhostPaceDriver>();
        Ensure<GpsSignalMonitor>(); // F-09 GPSロスト自動判定(§8.1)

        // F-11: 100Hz テレメトリCSVロガー(基本設計書§5.2 — PoCの核)。
        // アバターtransformを読むためエンジンと同居させる
        if (engine.GetComponent<RunTelemetryLogger>() == null)
        {
            engine.gameObject.AddComponent<RunTelemetryLogger>();
            Debug.Log("[BOOTSTRAP] RunTelemetryLogger auto-attached to avatar.");
        }

        // Swiftブリッジ受信オブジェクト — UnitySendMessage のターゲットになるため
        // GameObject名は UnityBridge.swift の sendMessageToGO と完全一致させる
        Ensure<ARSessionManagerBridge>(ARSessionManagerBridge.RequiredGameObjectName);
        Ensure<DeviceManagerBridge>(DeviceManagerBridge.RequiredGameObjectName);
    }

    private static void Ensure<T>(string gameObjectName = null) where T : MonoBehaviour
    {
        if (Object.FindFirstObjectByType<T>(FindObjectsInactive.Include) != null)
            return;

        var go = new GameObject(gameObjectName ?? $"[Auto] {typeof(T).Name}");
        go.AddComponent<T>();
        Debug.Log($"[BOOTSTRAP] {typeof(T).Name} auto-created as '{go.name}'.");
    }
}
