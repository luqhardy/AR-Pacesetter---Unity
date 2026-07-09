using System;
using UnityEngine;

/// <summary>
/// Swift → Unity デバイス管理ブリッジ (AR-runner の UnityBridge.swift 契約)。
/// GameObject名は必ず "DeviceManager"。
///   ConnectXREAL {} — XREALグラス接続要求 → ReadyチェックをConnectedへ
/// </summary>
public class DeviceManagerBridge : MonoBehaviour
{
    public const string RequiredGameObjectName = "DeviceManager";

    [Serializable]
    private class SwiftCommand
    {
        public string command;
    }

    [SerializeField] private ReadyCheckController readyCheck;

    void Awake()
    {
        if (gameObject.name != RequiredGameObjectName)
            Debug.LogWarning($"[SWIFT BRIDGE] GameObject must be named '{RequiredGameObjectName}' (current: '{gameObject.name}').");

        if (readyCheck == null)
            readyCheck = FindFirstObjectByType<ReadyCheckController>(FindObjectsInactive.Include);
    }

    // Swift: sendMessageToGO(withName: "DeviceManager", functionName: "OnSwiftCommand", message: json)
    public void OnSwiftCommand(string json)
    {
        SwiftCommand cmd = null;
        try { cmd = JsonUtility.FromJson<SwiftCommand>(json); } catch { /* fall through */ }
        if (cmd == null || string.IsNullOrEmpty(cmd.command)) return;

        switch (cmd.command)
        {
            case "ConnectXREAL":
                // 実機では XREAL SDK の初期化/接続処理をここで起動する。
                // プロトタイプでは Readyチェックの状態遷移で接続完了を表現する。
                if (readyCheck != null)
                    readyCheck.SetDeviceState("glass", ReadyCheckController.DeviceState.Connected);
                Debug.Log("[SWIFT BRIDGE] ConnectXREAL — glass marked Connected.");
                break;

            default:
                Debug.LogWarning($"[SWIFT BRIDGE] Unknown device command: {cmd.command}");
                break;
        }
    }
}
