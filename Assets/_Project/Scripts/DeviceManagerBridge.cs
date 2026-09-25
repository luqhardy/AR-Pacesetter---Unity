using System;
using UnityEngine;

/// <summary>
/// Swift → Unity デバイス管理ブリッジ (AR-runner の UnityBridge.swift 契約)。
/// GameObject名は必ず "DeviceManager"。
///   ConnectXREAL {model?, pixelWidth?, pixelHeight?, refreshHz?}
///                      — XREALグラス接続 → ReadyチェックをConnectedへ。
///                        表示メトリクスがあればグラスの画角で描くプロファイルを選ぶ
///   DisconnectXREAL {} — グラス切断(§8.3) → スタンバイ移行(アバター消去)。
///                        走行セッションは終了させず、CSVログ書き出しは継続する
///   UpdateGlassPose {yaw, pitch, roll, timestamp}
///                      — グラス実機の頭部姿勢(将来用。iOSでは現状供給元が無い)
/// </summary>
public class DeviceManagerBridge : MonoBehaviour
{
    public const string RequiredGameObjectName = "DeviceManager";

    [Serializable]
    private class SwiftCommand
    {
        public string command;

        // ConnectXREAL の表示メトリクス (iOSが知っている値のみ。画角はグラスから取得できない)
        public string model;
        public int pixelWidth;
        public int pixelHeight;
        public float refreshHz;

        // UpdateGlassPose (度)
        public float yaw;
        public float pitch;
        public float roll;
        public float timestamp;
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

                // 光学シースルーのグラスへ出す間はカメラ映像を描かない。
                // 現実の上に「現実の動画」を重ねると二重像になり全体が濁るため
                SetPassthrough(false);

                // グラスの画角・眼の位置で描く出力リグへ切り替える。
                // iPhoneカメラの内部パラメータのまま出すと3.0m前方のアバターが実寸の角度で見えない
                EnableGlassOutput(cmd);

                // §8.3: 再接続でも即座にアバターを出現させない。Swiftが準備画面へ戻り、
                // ユーザー操作後に ResumeSession/StartSession が来てから復帰する
                Debug.Log("[SWIFT BRIDGE] ConnectXREAL — glass Connected (アバター復帰は再スタート操作を待つ)。");
                break;

            case "DisconnectXREAL":
                // iPhone表示へ戻るのでカメラ映像を復帰させる(ビデオシースルー)
                SetPassthrough(true);
                DisableGlassOutput();
                HandleGlassDisconnected();
                break;

            case "UpdateGlassPose":
                ApplyGlassPose(cmd);
                break;

            default:
                Debug.LogWarning($"[SWIFT BRIDGE] Unknown device command: {cmd.command}");
                break;
        }
    }

    /// <summary>パススルー表示の切り替え(コントローラ未配置でも落ちない)。</summary>
    private void SetPassthrough(bool enabled)
    {
        var passthrough = FindFirstObjectByType<ARPassthroughController>(FindObjectsInactive.Include);
        if (passthrough != null)
            passthrough.SetPassthroughEnabled(enabled);
    }

    /// <summary>
    /// 接続ペイロードから表示プロファイルを決めて出力リグを起動する。
    ///
    /// <para>解像度・リフレッシュレートはiOSが知っているが、<b>機種名も画角もグラスからは取得できない</b>
    /// (XREALのSDKはAndroid専用)。メトリクスが無い場合や未知の解像度の場合は、本プロジェクトの
    /// 実機である XREAL One として扱う — 画角を上書きしない方が確実に見え方を外すため。</para>
    /// </summary>
    private void EnableGlassOutput(SwiftCommand cmd)
    {
        var rig = FindFirstObjectByType<GlassViewRig>(FindObjectsInactive.Include);
        if (rig == null) return;

        GlassDisplayProfile profile =
            GlassDisplayProfile.Resolve(cmd.model, cmd.pixelWidth, cmd.pixelHeight, cmd.refreshHz);

        if (profile == null)
        {
            profile = GlassDisplayProfile.XrealOne.WithDisplayMode(cmd.pixelWidth, cmd.pixelHeight, cmd.refreshHz);
            Debug.Log($"[SWIFT BRIDGE] 表示メトリクスから機種を特定できないため既定プロファイルを使用: {profile}");
        }

        rig.EnableGlassOutput(profile);
    }

    private void DisableGlassOutput()
    {
        var rig = FindFirstObjectByType<GlassViewRig>(FindObjectsInactive.Include);
        if (rig != null) rig.DisableGlassOutput();
    }

    /// <summary>
    /// グラス実機の頭部姿勢を出力リグへ渡す(将来用)。
    /// 供給が途切れれば自動で §4.1 の進行方向ヨーへ落ちるので、欠測しても破綻しない。
    /// </summary>
    private void ApplyGlassPose(SwiftCommand cmd)
    {
        var rig = FindFirstObjectByType<GlassViewRig>(FindObjectsInactive.Include);
        if (rig == null) return;

        // 鮮度判定はUnityの時間軸で行う。Swiftの timestamp は CACurrentMediaTime 系で
        // 起点が違うためそのまま渡すと必ず「古い」と判定される — 到着時刻で刻む(0を渡す)
        rig.SetExternalHeadPose(Quaternion.Euler(cmd.pitch, cmd.yaw, cmd.roll), 0f);
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
