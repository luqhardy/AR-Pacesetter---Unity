using System;
using UnityEngine;

/// <summary>
/// Swift → Unity デバイス管理ブリッジ (AR-runner の UnityBridge.swift 契約)。
/// GameObject名は必ず "DeviceManager"。
///   ConnectXREAL {model?, pixelWidth?, pixelHeight?, refreshHz?, phoneDisplayOnly?}
///                      — XREALグラス接続 → ReadyチェックをConnectedへ。
///                        表示メトリクスがあればグラスの画角で描くプロファイルを選ぶ。
///                        phoneDisplayOnly は外部画面が無いときの手動接続で、描画はiPhoneのまま
///   DisconnectXREAL {} — グラス切断(§8.3) → スタンバイ移行(アバター消去)。
///                        走行セッションは終了させず、CSVログ書き出しは継続する
///   UpdateGlassPose {yaw, pitch, roll, timestamp}
///                      — グラス実機の頭部姿勢(将来用。iOSでは現状供給元が無い)
///   SetGlassPassthrough {enabled} / SetPhonePassthrough {enabled}
///                      — グラスの背景 / iPhone の画面にカメラ映像を出すか(開発者モード・どちらも既定ON)
///   SetHeadYawSource {source}
///                      — 頭マウントの描画のヨーの元。"phone"(既定・ARKitカメラ)| "glassImu"(UpdateHeadYaw のヨー)
///   UpdateHeadYaw {yaw}
///                      — 頭のヨー(度・基準は任意・右が正)。AirPods か XREAL One 自身のIMU(Swift の XrealImuReceiver)。進行方向との差を首振りとして
///                        グラスの描画の向きへ足し、首を振ってもアバターが進行方向の3m先に留まって見える
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

        // 外部画面が無いときの手動接続(ミラーリングの予備手段とは別)。iPhone の画面には
        // グラス用の描画を出さない — ARCameraBackground を止めるとカメラ映像が消える
        public bool phoneDisplayOnly;

        // UpdateGlassPose (度)。UpdateHeadYaw は yaw だけを使う(AirPods の頭のヨー・右が正)
        public float yaw;
        public float pitch;
        public float roll;
        public float timestamp;

        // SetGlassPassthrough / SetPhonePassthrough(開発者モード): グラスの背景 / iPhone の画面にカメラ映像を出すか
        public bool enabled;

        // SetHeadYawSource(開発者モード): 頭マウントの描画のヨーの元。"phone"(既定) | "glassImu"
        public string source;
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

                if (cmd.phoneDisplayOnly)
                {
                    EnsureRenderingOnPhone();
                    Debug.Log("[SWIFT BRIDGE] ConnectXREAL — 外部画面なしの手動接続。描画はiPhoneのまま。");
                    break;
                }

                // ARKitカメラの背景は止める(グラス出力中は何も描かないカメラになる)。
                // グラスへのカメラ映像は出力リグが出す(既定ON・SetGlassPassthrough で切替)
                SetPassthrough(false);

                // グラスの画角・眼の位置で描く出力リグへ切り替える。
                // iPhoneカメラの内部パラメータのまま出すと3.0m前方のアバターが実寸の角度で見えない
                EnableGlassOutput(cmd);

                // HUDの所有者: グラスに出ている間はUnityのF-07 HUD(Swiftの hideUnityHud と同じ規則)。
                // StartSession時にしか決めていなかったため、iPhoneで走り出してから
                // グラスを挿すとグラスにHUDが出なかった。表示メトリクスがある = 実際の外部
                // ディスプレイ接続のときだけ切り替える(準備画面の手動「接続」には付かない)
                if (cmd.pixelWidth > 0 && cmd.pixelHeight > 0)
                    SetUnityHudVisible(true);

                // §8.3: 再接続でも即座にアバターを出現させない。Swiftが準備画面へ戻り、
                // ユーザー操作後に ResumeSession/StartSession が来てから復帰する
                Debug.Log("[SWIFT BRIDGE] ConnectXREAL — glass Connected (アバター復帰は再スタート操作を待つ)。");
                break;

            case "DisconnectXREAL":
                // iPhone表示へ戻るのでカメラ映像を復帰させる(ビデオシースルー)
                SetPassthrough(true);
                DisableGlassOutput();
                // iPhone表示に戻ればSwiftUIがHUDを持つ(二重表示しない)
                SetUnityHudVisible(false);
                HandleGlassDisconnected();
                break;

            case "UpdateGlassPose":
                ApplyGlassPose(cmd);
                break;

            case "UpdateHeadYaw":
                // AirPods の頭のヨー(基準は任意・右が正)
                ApplyHeadYaw(cmd);
                break;

            case "SetGlassPassthrough":
            {
                var rig = FindFirstObjectByType<GlassViewRig>(FindObjectsInactive.Include);
                if (rig != null) rig.SetCameraPassthroughOnGlass(cmd.enabled);
                else Debug.LogWarning("[SWIFT BRIDGE] SetGlassPassthrough ignored — GlassViewRig not found.");
                break;
            }

            case "SetHeadYawSource":
            {
                // 頭マウントの描画のヨーを iPhone(ARKitカメラ)とグラスのIMUのどちらから取るか(開発者モード)
                var rig = ResolveGlassRig();
                if (rig != null) rig.SetHeadYawSource(HeadPoseMath.ParseHeadYawSource(cmd.source));
                else Debug.LogWarning("[SWIFT BRIDGE] SetHeadYawSource ignored — GlassViewRig not found.");
                break;
            }

            case "SetPhonePassthrough":
            {
                // iPhone の画面のカメラ映像(開発者モード)。グラス出力中は設定だけ覚え、iPhone へ戻ったときに使う
                var passthrough = FindFirstObjectByType<ARPassthroughController>(FindObjectsInactive.Include);
                if (passthrough != null) passthrough.SetPhoneCameraWanted(cmd.enabled);
                else Debug.LogWarning("[SWIFT BRIDGE] SetPhonePassthrough ignored — ARPassthroughController not found.");
                break;
            }

            default:
                Debug.LogWarning($"[SWIFT BRIDGE] Unknown device command: {cmd.command}");
                break;
        }
    }

    private void SetUnityHudVisible(bool visible)
    {
        var hud = FindFirstObjectByType<PeripheralHUDManager>(FindObjectsInactive.Include);
        if (hud != null && hud.IsHudVisible != visible)
            hud.SetHudVisible(visible);
    }

    /// <summary>パススルー表示の切り替え(コントローラ未配置でも落ちない)。</summary>
    private void SetPassthrough(bool enabled)
    {
        var passthrough = FindFirstObjectByType<ARPassthroughController>(FindObjectsInactive.Include);
        if (passthrough != null)
            passthrough.SetPassthroughEnabled(enabled);
    }

    /// <summary>
    /// 出力リグが動いていればiPhoneの描画へ戻す。ミラーリング中に手動接続してから
    /// グラスを抜いた場合は DisconnectXREAL が来ないので、ここで解く。
    /// 動いていないときは何もしない(描画中の ARCameraBackground の設定を崩さない)
    /// </summary>
    private void EnsureRenderingOnPhone()
    {
        var rig = FindFirstObjectByType<GlassViewRig>(FindObjectsInactive.Include);
        if (rig == null || !rig.IsGlassOutputActive) return;

        SetPassthrough(true);
        rig.DisableGlassOutput();
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
        var rig = ResolveGlassRig();
        if (rig == null) return;

        // 鮮度判定はUnityの時間軸で行う。Swiftの timestamp は CACurrentMediaTime 系で
        // 起点が違うためそのまま渡すと必ず「古い」と判定される — 到着時刻で刻む(0を渡す)
        rig.SetExternalHeadPose(Quaternion.Euler(cmd.pitch, cmd.yaw, cmd.roll), 0f);
    }

    /// <summary>
    /// 首振りを出力リグへ渡す(タスク表 ARグラス「首を振り向けた時に想定する場所を走る」)。
    /// 供給が途切れれば進行方向ヨーだけに戻る
    /// </summary>
    private void ApplyHeadYaw(SwiftCommand cmd)
    {
        var rig = ResolveGlassRig();
        if (rig != null) rig.SetHeadYaw(cmd.yaw);
    }

    // 頭の向きは約30Hzで届く。届くたびにシーン全体を探さない — 見つけたものを持ち、
    // 見つからない間も探し直すのは1秒に1回まで
    private GlassViewRig _glassRig;
    private float _nextGlassRigLookupTime;

    private GlassViewRig ResolveGlassRig()
    {
        if (_glassRig == null && Time.unscaledTime >= _nextGlassRigLookupTime)
        {
            _nextGlassRigLookupTime = Time.unscaledTime + 1f;
            _glassRig = FindFirstObjectByType<GlassViewRig>(FindObjectsInactive.Include);
        }
        return _glassRig;
    }

    /// <summary>
    /// §8.3 ARグラス切断時の緊急処理:
    /// スタンバイへ移行してアバターを消去する。走行セッション自体は終了させないため、
    /// F-11のCSVログ書き出し(RunTelemetryLogger はアバターとは別のGameObjectで
    /// IsRunMotionActive の間動作)はバックグラウンドで継続する。
    /// 再接続時は即復帰させず準備画面からの再スタートを待つ — 原因を GlassDisconnected として
    /// 残すので、その間にGPSが復帰しても GpsSignalMonitor はアバターを戻さない。
    /// </summary>
    private void HandleGlassDisconnected()
    {
        IsGlassDisconnected = true;

        if (readyCheck != null)
            readyCheck.SetDeviceState("glass", ReadyCheckController.DeviceState.Disconnected);

        bool running = avatarEngine != null && avatarEngine.HasStarted && !avatarEngine.IsSessionEnded;
        if (running && stateController != null)
        {
            stateController.EnterStandby(GameStateController.StandbyCause.GlassDisconnected);
            Debug.LogWarning("[SWIFT BRIDGE] DisconnectXREAL — スタンバイ移行(アバター消去)。CSVログは継続。");
        }
        else
        {
            Debug.Log("[SWIFT BRIDGE] DisconnectXREAL — 非走行中のため状態遷移なし。");
        }
    }
}
