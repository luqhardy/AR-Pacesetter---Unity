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
        if (Object.FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include) == null)
            return;

        Ensure<SafetyEventLogger>();
        Ensure<RunAudioEngine>();
        Ensure<RunSessionController>();
        Ensure<ReadyCheckController>();

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
