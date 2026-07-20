using System;
using UnityEngine;

/// <summary>
/// Swift → Unity デバイス管理ブリッジ (AR-runner の UnityBridge.swift 契約)。
/// GameObject名は必ず "DeviceManager"。
///   ConnectXREAL {}    — XREALグラス接続 → ReadyチェックをConnectedへ
///   DisconnectXREAL {} — グラス切断(§8.3) → スタンバイ移行(アバター消去)。
///                        走行セッションは終了させず、CSVログ書き出しは継続する
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
    [SerializeField] private GameStateController stateController;
    [SerializeField] private AvatarEngine avatarEngine;

    /// <summary>グラス切断でスタンバイ中か(再接続時に即復帰させないための状態)。</summary>
    public bool IsGlassDisconnected { get; private set; }

    void Awake()
    {
        if (gameObject.name != RequiredGameObjectName)
            Debug.LogWarning($"[SWIFT BRIDGE] GameObject must be named '{RequiredGameObjectName}' (current: '{gameObject.name}').");

        if (readyCheck == null)
            readyCheck = FindFirstObjectByType<ReadyCheckController>(FindObjectsInactive.Include);
        if (stateController == null)
            stateController = FindFirstObjectByType<GameStateController>(FindObjectsInactive.Include);
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
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
                IsGlassDisconnected = false;
                // §8.3: 再接続でも即座にアバターを出現させない。Swiftが準備画面へ戻り、
                // ユーザー操作後に ResumeSession/StartSession が来てから復帰する
                Debug.Log("[SWIFT BRIDGE] ConnectXREAL — glass Connected (アバター復帰は再スタート操作を待つ)。");
                break;

            case "DisconnectXREAL":
                HandleGlassDisconnected();
                break;

            default:
                Debug.LogWarning($"[SWIFT BRIDGE] Unknown device command: {cmd.command}");
                break;
        }
    }

    /// <summary>
    /// §8.3 ARグラス切断時の緊急処理:
    /// スタンバイへ移行してアバターを消去する。走行セッション自体は終了させないため、
    /// F-11のCSVログ書き出し(RunTelemetryLoggerはHasStarted && !IsSessionEndedで動作)は
    /// バックグラウンドで継続する。再接続時は即復帰させず準備画面からの再スタートを待つ。
    /// </summary>
    private void HandleGlassDisconnected()
    {
        IsGlassDisconnected = true;

        if (readyCheck != null)
            readyCheck.SetDeviceState("glass", ReadyCheckController.DeviceState.Disconnected);

        bool running = avatarEngine != null && avatarEngine.HasStarted && !avatarEngine.IsSessionEnded;
        if (running && stateController != null)
        {
            stateController.TransitionToState(GameStateController.ARVisionState.Standby);
            Debug.LogWarning("[SWIFT BRIDGE] DisconnectXREAL — スタンバイ移行(アバター消去)。CSVログは継続。");
        }
        else
        {
            Debug.Log("[SWIFT BRIDGE] DisconnectXREAL — 非走行中のため状態遷移なし。");
        }
    }
}
