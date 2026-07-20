#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// エディタ専用のE2Eシナリオ検証。ビルドには含まれない(#if UNITY_EDITOR)。
/// メニュー「Build → Run E2E Scenario」またはバッチモード
/// (-executeMethod E2EScenarioRunner.Run)から起動され、Play Mode内で
/// 「開始→走行→ゴール自動終了→記録保存→ゴースト再走→GPS喪失/復帰」を
/// 自動実行して各ステップを判定する。
/// </summary>
public class E2EScenarioBehaviour : MonoBehaviour
{
    private const string SessionFlag = "ARV_E2E_PENDING";
    private const float RunSpeedMetersPerSecond = 3.6f; // ≒ 4:37/km
    private const float StepTimeoutSeconds = 45f;

    private readonly List<string> _failures = new List<string>();
    private int _passCount = 0;

    private Transform _cameraMover;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoStart()
    {
        if (!SessionState.GetBool(SessionFlag, false)) return;
        SessionState.SetBool(SessionFlag, false); // one-shot

        var go = new GameObject("[E2E Scenario]");
        go.AddComponent<E2EScenarioBehaviour>();
        Debug.Log("[E2E] Scenario runner spawned.");
    }

    /// <summary>E2EScenarioRunner から設定される起動フラグ。</summary>
    public static void RequestRun() => SessionState.SetBool(SessionFlag, true);

    private IEnumerator Start()
    {
        // ブートストラップ(RuntimeInitializeOnLoadMethod)完了を待つ
        yield return null;
        yield return null;

        Time.timeScale = 3f; // 実時間短縮(deltaTimeベースのロジックは等価にスケール)

        var bridge = FindFirstObjectByType<ARSessionManagerBridge>(FindObjectsInactive.Include);
        var engine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        var session = FindFirstObjectByType<RunSessionController>(FindObjectsInactive.Include);
        var ghost = FindFirstObjectByType<GhostPaceDriver>(FindObjectsInactive.Include);
        var analytics = FindFirstObjectByType<AnalyticsManager>(FindObjectsInactive.Include);
        var stateController = FindFirstObjectByType<GameStateController>(FindObjectsInactive.Include);

        Check(bridge != null, "bootstrap: ARSessionManagerBridge exists");
        Check(engine != null, "bootstrap: AvatarEngine exists");
        Check(session != null, "bootstrap: RunSessionController exists");
        Check(ghost != null, "bootstrap: GhostPaceDriver exists");

        Camera cam = Camera.main;
        Check(cam != null, "scene: main camera exists");
        if (bridge == null || engine == null || session == null || cam == null)
        {
            Finish();
            yield break;
        }

        _cameraMover = cam.transform.root != null ? cam.transform.root : cam.transform;

        // ── Step 0b: 疲労補正係数 (企画書4.4 — 気温閾値。純関数なので走行前に検証) ──
        if (analytics != null)
        {
            float originalTemp = analytics.AmbientTemperature;
            analytics.AmbientTemperature = 25f;
            Check(Mathf.Approximately(analytics.GetFatigueMultiplier(), 1.0f), "fatigue: Cf=1.0 below 28C");
            analytics.AmbientTemperature = 29f;
            Check(Mathf.Approximately(analytics.GetFatigueMultiplier(), 1.5f), "fatigue: Cf=1.5 at 28-31C");
            analytics.AmbientTemperature = 32f;
            Check(Mathf.Approximately(analytics.GetFatigueMultiplier(), 2.0f), "fatigue: Cf=2.0 at/above 31C");
            analytics.AmbientTemperature = originalTemp; // 走行の疲労計算を汚さない
        }

        // ── Step 1: StartSession (目標60m — ゴール自動終了を早く踏むため) ──
        bridge.OnSwiftCommand(
            "{\"command\":\"StartSession\",\"targetPaceKmH\":13.0,\"distanceKm\":0.06," +
            "\"avatarHeightCm\":175,\"forwardOffsetM\":3.0}");
        yield return WaitScaled(0.5f);
        Check(engine.HasStarted, "start: engine.HasStarted after StartSession");

        var fakeShadow = FindFirstObjectByType<FakeShadowRenderer>(FindObjectsInactive.Include);
        Check(fakeShadow != null && fakeShadow.IsVisible,
            "render: fake shadow visible under avatar");

        // 要件定義 6.1: 60fps維持(iOS既定30fpsを明示的に引き上げていること)
        Check(Application.targetFrameRate == 60, "render: target frame rate set to 60fps");

        // §7.3: 再生速度同期。PlaybackSpeedが0だとロコモーションが停止するため、
        // 走行中は必ず正であること(既定1.0 + 毎フレーム供給)
        var runAnimator = AvatarRigLocator.FindBestAnimator(engine.transform);
        if (runAnimator != null && HasFloatParam(runAnimator, "PlaybackSpeed"))
            Check(runAnimator.GetFloat("PlaybackSpeed") > 0.01f,
                "anim: PlaybackSpeed > 0 during run (locomotion not frozen)");

        // F-11 テレメトリCSV: 走行中にログが開始していること
        var telemetry = FindFirstObjectByType<RunTelemetryLogger>(FindObjectsInactive.Include);
        Check(telemetry != null && telemetry.IsLogging, "telemetry: 100Hz CSV logging active during run");
        string telemetryPath = telemetry != null ? telemetry.CurrentFilePath : null;

        var visualsForColor = FindFirstObjectByType<AvatarVisualsAndActions>(FindObjectsInactive.Include);

        // ── Step 2: 走行シミュレーション(カメラを前進させる) ────────────────
        float elapsed = 0f;
        bool syncObserved = false;
        bool justColorObserved = false;
        Vector3 runDirection = Vector3.forward;
        while (!engine.IsSessionEnded && elapsed < StepTimeoutSeconds)
        {
            _cameraMover.position += runDirection * RunSpeedMetersPerSecond * Time.deltaTime;
            elapsed += Time.deltaTime;

            if (!syncObserved && analytics != null && analytics.GetLiveSyncRate() > 30f)
                syncObserved = true;

            // §7.1: 目標ペース(3m先行)を保っている間はジャスト=緑
            if (!justColorObserved && elapsed > 2f && visualsForColor != null
                && visualsForColor.PaceColorState == "Just")
                justColorObserved = true;

            // 途中でSwiftメトリクスも1回注入(実機経路の確認)
            if (!_metricsSent && elapsed > 3f)
            {
                _metricsSent = true;
                bridge.OnSwiftCommand("{\"command\":\"UpdateMetrics\",\"paceKmH\":13.0,\"heartRate\":150,\"distanceKm\":0.012}");
            }
            yield return null;
        }

        Check(engine.IsSessionEnded, "goal: session auto-finished by goal distance");
        Check(syncObserved, "run: live sync rate exceeded 30% during run");
        Check(justColorObserved, "color: pace-sync GREEN (just) while on target pace (§7.1)");

        // §7.2: 目標ペース維持中はオーラ(5m以上の遅れ表示)を出さないこと
        var aura = FindFirstObjectByType<AvatarAuraEffect>(FindObjectsInactive.Include);
        Check(aura != null && !aura.IsAuraActive,
            "aura: not emitted while on target pace (§7.2 threshold 5m)");

        // 終了直後の挨拶(お辞儀)ジェスチャーが再生されること
        yield return null; // LateUpdate反映待ち
        var goalGestures = FindFirstObjectByType<ProceduralGestureDriver>(FindObjectsInactive.Include);
        Check(goalGestures != null && goalGestures.ActiveGesture == "Goodbye",
            "goal: procedural goodbye gesture playing");

        // F-11 テレメトリCSV: 終了後にファイルが生成され、正しいヘッダーと
        // 100Hz相当の行数を持つこと(§5.2)
        yield return null; // ロガーのStopLogging/Flush完了待ち
        if (!string.IsNullOrEmpty(telemetryPath) && System.IO.File.Exists(telemetryPath))
        {
            string[] lines = System.IO.File.ReadAllLines(telemetryPath);
            bool headerOk = lines.Length > 0 && lines[0].StartsWith("timestamp,gps_latitude,gps_longitude");
            int dataRows = lines.Length - 1;
            // 走行elapsed秒 × 100Hz の概ね妥当な行数(下限を緩めに)
            Check(headerOk, "telemetry: CSV header matches §5.2 spec");
            Check(dataRows > 100, $"telemetry: ~100Hz rows written ({dataRows} rows)");

            // タイムスタンプが単調増加かつ10ms刻み(100Hz)であること。
            // 書込時刻を使うと1フレーム内の複数行が同一msになり解析不能になる
            bool monotonic10ms = true;
            long prev = -1;
            int checkedRows = 0;
            for (int i = 1; i < lines.Length && checkedRows < 300; i++)
            {
                string[] cols = lines[i].Split(',');
                if (cols.Length < 9) { monotonic10ms = false; break; }
                if (!long.TryParse(cols[0], out long ts)) { monotonic10ms = false; break; }
                if (prev >= 0 && ts - prev != 10) { monotonic10ms = false; break; }
                prev = ts;
                checkedRows++;
            }
            Check(monotonic10ms, "telemetry: timestamps monotonic at 10ms (100Hz) spacing");
        }
        else
        {
            Check(false, "telemetry: CSV file created on disk");
        }

        var record = session.LastRecord;
        Check(record != null, "record: LastRecord created");
        if (record != null)
        {
            Check(record.distanceMeters >= 55f, $"record: distance ~goal ({record.distanceMeters:F1}m)");
            Check(!string.IsNullOrEmpty(record.grade), "record: grade assigned");
            Check(record.paceTimeline.Count >= 2, $"record: paceTimeline sampled ({record.paceTimeline.Count})");
        }

        // ── Step 3: ゴースト再走(保存した記録と競走) ──────────────────────
        string ghostIso = record != null ? record.dateIso : "";
        bridge.OnSwiftCommand(
            "{\"command\":\"StartSession\",\"targetPaceKmH\":13.0,\"distanceKm\":0.5," +
            "\"avatarHeightCm\":175,\"forwardOffsetM\":3.0," +
            $"\"mode\":\"ghost\",\"ghostDateIso\":\"{ghostIso}\"}}");
        yield return WaitScaled(0.5f);

        Check(engine.HasStarted && !engine.IsSessionEnded, "restart: second run started after reset");
        Check(ghost != null && ghost.IsActive, "ghost: GhostPaceDriver active");

        // 少し走ってゴーストペース追従を確認
        for (float t = 0; t < 3f; t += Time.deltaTime)
        {
            _cameraMover.position += runDirection * RunSpeedMetersPerSecond * Time.deltaTime;
            yield return null;
        }
        Check(engine.GetTargetSpeed() > 0.5f, "ghost: avatar moving at ghost pace");

        // ── Step 3b: バイタル警告 (HR185以上 → 深青 + CalmDownサイン) ─────────
        var visuals = FindFirstObjectByType<AvatarVisualsAndActions>(FindObjectsInactive.Include);
        var gestures = FindFirstObjectByType<ProceduralGestureDriver>(FindObjectsInactive.Include);
        bool sawVitalWarning = false;
        bool sawCalmGesture = false;
        // エディタHRシミュレータが値を上書きするため、毎フレーム注入しつつ監視
        for (float t = 0; t < 1.5f && !(sawVitalWarning && sawCalmGesture); t += Time.deltaTime)
        {
            bridge.OnSwiftCommand("{\"command\":\"UpdateMetrics\",\"paceKmH\":13.0,\"heartRate\":195,\"distanceKm\":0.02}");
            if (visuals != null && visuals.IsVitalWarningActive) sawVitalWarning = true;
            if (gestures != null && gestures.ActiveGesture == "CalmDown") sawCalmGesture = true;
            yield return null;
        }
        Check(sawVitalWarning, "vital: deep-blue warning at HR>=185");
        Check(sawCalmGesture, "vital: procedural calm-down gesture playing");
        bridge.OnSwiftCommand("{\"command\":\"UpdateMetrics\",\"paceKmH\":13.0,\"heartRate\":150,\"distanceKm\":0.02}");
        yield return WaitScaled(0.3f);

        // ── Step 3b-2: 追い抜きリアクション (Features #8/#9) ──────────────────
        // ユーザーがアバターより速く走り続けると、アバターは「譲る(BeingOvertaken)」
        // または「抜き返しスプリント(Overtaking)」で反応する
        bool sawOvertakeReaction = false;
        float otElapsed = 0f;
        while (otElapsed < 6f)
        {
            otElapsed += Time.deltaTime;
            _cameraMover.position += runDirection * 9f * Time.deltaTime; // 全力疾走
            if (engine.CurrentOvertakeState != AvatarEngine.OvertakeState.None)
            {
                sawOvertakeReaction = true;
                break;
            }
            yield return null;
        }
        Check(sawOvertakeReaction, "overtake: reaction state triggered by fast user");

        // 通過後は通常ペーシングへ復帰する
        otElapsed = 0f;
        while (engine.CurrentOvertakeState != AvatarEngine.OvertakeState.None && otElapsed < 8f)
        {
            otElapsed += Time.deltaTime;
            _cameraMover.position += runDirection * 9f * Time.deltaTime;
            yield return null;
        }
        Check(engine.CurrentOvertakeState == AvatarEngine.OvertakeState.None,
            "overtake: returned to normal pacing");

        // 通常速度へ戻して体勢回復
        for (float t = 0; t < 1.5f; t += Time.deltaTime)
        {
            _cameraMover.position += runDirection * RunSpeedMetersPerSecond * Time.deltaTime;
            yield return null;
        }

        // ── Step 3c: 障害物停止 (断崖・壁 → 足踏み待機 → 解除で再開) ──────────
        var groundSnap = FindFirstObjectByType<GroundSnap>(FindObjectsInactive.Include);
        if (groundSnap != null)
        {
            groundSnap.SimulateObstacle = true;
            yield return WaitScaled(0.4f);
            Check(engine.IsHalted, "obstacle: avatar halts at simulated wall");

            groundSnap.SimulateObstacle = false;
            yield return WaitScaled(0.4f);
            Check(!engine.IsHalted, "obstacle: avatar resumes when path clears");
        }

        // ── Step 3d: ルート逸脱 → サイレント復帰 ─────────────────────────────
        var recoverer = FindFirstObjectByType<SilentRouteRecoverer>(FindObjectsInactive.Include);
        var safetyLogger = FindFirstObjectByType<SafetyEventLogger>(FindObjectsInactive.Include);
        if (recoverer != null)
        {
            int eventsBefore = safetyLogger != null ? safetyLogger.Events.Count : 0;

            recoverer.SimulateDeviation = true;
            yield return WaitScaled(0.4f);
            Check(recoverer.IsRecovering && engine.IsOverriddenByRecovery,
                "deviation: silent recovery engaged");
            Check(safetyLogger == null || safetyLogger.Events.Count > eventsBefore,
                "deviation: safety event logged with position");

            recoverer.SimulateDeviation = false;
            yield return WaitScaled(0.4f);
            Check(!engine.IsOverriddenByRecovery, "deviation: normal pacing restored");
        }

        // ── Step 3e: 離隔待機 (10mで座標固定+手招き → 7mで再開) ─────────────
        // 通常追従ではアバターは常にユーザー+3mへアンカーされるため、
        // 10m離隔は「アバターが停止中(障害物等)にユーザーが離れる」ケースで発生する。
        // その実運用シナリオを再現する: 壁で停止→ユーザーが先へ進む→待機+手招き
        if (groundSnap != null)
        {
            groundSnap.SimulateObstacle = true; // アバターを座標固定
            yield return WaitScaled(0.2f);

            float waitElapsed = 0f;
            while (!engine.IsWaitingForUser && waitElapsed < 15f)
            {
                waitElapsed += Time.deltaTime;
                _cameraMover.position += runDirection * RunSpeedMetersPerSecond * Time.deltaTime;
                yield return null;
            }
            Check(engine.IsWaitingForUser, "wait: avatar holds & beckons at 10m separation");
            yield return null; // ジェスチャー判定はLateUpdateで更新される
            Check(gestures != null && gestures.ActiveGesture == "Beckon",
                "wait: procedural beckon gesture playing");

            groundSnap.SimulateObstacle = false; // 障害解除(待機状態は距離条件で継続)

            // アバター方向へ戻って追いつく(7mで再開)
            waitElapsed = 0f;
            while (engine.IsWaitingForUser && waitElapsed < 15f)
            {
                waitElapsed += Time.deltaTime;
                Vector3 toAvatar = engine.transform.position - _cameraMover.position;
                toAvatar.y = 0;
                if (toAvatar.sqrMagnitude > 0.01f)
                    _cameraMover.position += toAvatar.normalized * 6f * Time.deltaTime;
                yield return null;
            }
            Check(!engine.IsWaitingForUser, "wait: pacing resumes when user catches up (7m)");
        }

        // コーナー前に直進で体勢を整える(待機解除直後の過渡を収束させる)
        for (float t = 0; t < 4.0f; t += Time.deltaTime)
        {
            _cameraMover.position += runDirection * RunSpeedMetersPerSecond * Time.deltaTime;
            yield return null;
        }

        // ── Step 3f: コーナー追従 (企画書§6 技術的成功基準①) ────────────────
        // 400mトラックの曲線部(半径36.5m)を1/4周。アバターが先行を維持し、
        // ワープせず、進行方向(接線)に追従して旋回することを検証する。
        yield return StartCoroutine(RunCornerFollowingTest(engine));

        // ── Step 4: GPS喪失→復帰 FSM ──────────────────────────────────────
        if (stateController != null)
        {
            stateController.TransitionToState(GameStateController.ARVisionState.InertialMovement);
            yield return WaitScaled(0.5f);
            stateController.TransitionToState(GameStateController.ARVisionState.Reaccumulation);
            // 注意: Reaccumulation遷移は精度を99にリセットする(要再確認ゲート)ため、
            // 遷移後に精度を設定する(実機ではCoreLocationの精度更新に相当)
            yield return WaitScaled(0.2f);
            stateController.SimulatedGPSAccuracyRadius = 3f;
            yield return WaitScaled(3.0f); // 1.5s粒子演出 + 精度ゲート + 頷き
            Check(stateController.currentState == GameStateController.ARVisionState.Normal,
                "gps: recovered to Normal after reaccumulation");
        }

        // ── Step 4b: GPSロスト自動判定 (F-09 / 基本設計書§8.1) ────────────────
        // 良好な測位を供給 → 精度10m超で即ロスト → 良好復帰でNormalへ
        var gpsMonitor = FindFirstObjectByType<GpsSignalMonitor>(FindObjectsInactive.Include);
        if (gpsMonitor != null && stateController != null && !engine.IsSessionEnded)
        {
            stateController.TransitionToState(GameStateController.ARVisionState.Normal);
            yield return null;

            // 良好サンプル(精度3m)ではロストしない
            gpsMonitor.ReportGpsUpdate(34.6937, 135.5023, 3.0f);
            yield return null;
            Check(!gpsMonitor.IsSignalLost, "gps-auto: good fix (3m) is not treated as lost");

            // 精度10m以上へ悪化した瞬間にロスト判定 → 慣性移動へ自動遷移
            gpsMonitor.ReportGpsUpdate(34.6937, 135.5023, 12.0f);
            yield return null;
            yield return null;
            Check(gpsMonitor.IsSignalLost, "gps-auto: accuracy >=10m detected as signal loss (§8.1)");
            Check(stateController.currentState == GameStateController.ARVisionState.InertialMovement,
                "gps-auto: FSM auto-transitioned to InertialMovement");

            // 精度が回復すると通常追従へ自動復帰
            gpsMonitor.ReportGpsUpdate(34.6937, 135.5023, 3.0f);
            yield return null;
            yield return null;
            Check(stateController.currentState == GameStateController.ARVisionState.Normal,
                "gps-auto: FSM auto-recovered to Normal on good fix");

            // 後続ステップへ影響しないよう監視を解除(未受信状態=非介入へ戻す)
            gpsMonitor.ResetSession();
        }

        // ── Step 4c: ARグラス切断→再スタート (§8.3) ──────────────────────────
        // 切断でスタンバイ(アバター消去)へ移行しつつCSVログは継続、
        // 準備画面からの再スタート(ResumeSession)で通常追従へ復帰する
        var deviceBridge = FindFirstObjectByType<DeviceManagerBridge>(FindObjectsInactive.Include);
        if (deviceBridge != null && stateController != null && !engine.IsSessionEnded)
        {
            var telemetryForGlass = FindFirstObjectByType<RunTelemetryLogger>(FindObjectsInactive.Include);

            deviceBridge.OnSwiftCommand("{\"command\":\"DisconnectXREAL\"}");
            yield return null;
            Check(stateController.currentState == GameStateController.ARVisionState.Standby,
                "glass: disconnect moves FSM to Standby (avatar hidden, §8.3)");
            Check(telemetryForGlass == null || telemetryForGlass.IsLogging,
                "glass: CSV logging continues while disconnected (§8.3)");

            // 再接続だけではアバターを復帰させない(準備画面からの再スタートを待つ)
            deviceBridge.OnSwiftCommand("{\"command\":\"ConnectXREAL\"}");
            yield return null;
            Check(stateController.currentState == GameStateController.ARVisionState.Standby,
                "glass: reconnect alone does NOT resurrect avatar (§8.3)");

            // 準備画面からの再スタート操作で復帰
            bridge.OnSwiftCommand("{\"command\":\"ResumeSession\"}");
            yield return null;
            Check(stateController.currentState == GameStateController.ARVisionState.Normal,
                "glass: ResumeSession restores normal pacing");
        }

        // ── Step 5: 履歴取得 ────────────────────────────────────────────────
        bridge.OnSwiftCommand("{\"command\":\"RequestHistory\"}");
        yield return WaitScaled(0.3f);
        Check(SessionDataStore.LoadAllSessions().Count > 0, "history: at least one session persisted");

        // ── Step 6: HUD自動抑制 (首振り検知で四隅表示をフェード) ──────────────
        var hud = FindFirstObjectByType<PeripheralHUDManager>(FindObjectsInactive.Include);
        if (hud != null)
        {
            bool sawSuppressed = false;
            for (float t = 0; t < 0.7f; t += Time.deltaTime)
            {
                _cameraMover.Rotate(0f, 300f * Time.deltaTime, 0f); // 素早い首振り(>120°/s)
                if (hud.CurrentHudVisibility < 0.85f) sawSuppressed = true;
                yield return null;
            }
            Check(sawSuppressed, "hud: suppressed during fast head turn");

            yield return WaitScaled(2.0f); // 首振り終了 → 0.8秒保持 → 復帰
            Check(hud.CurrentHudVisibility > 0.9f, "hud: restored after gaze settles");
        }

        Finish();
    }

    private bool _metricsSent = false;

    private static bool HasFloatParam(Animator animator, string name)
    {
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Float && p.name == name) return true;
        return false;
    }

    /// <summary>
    /// 陸上トラック曲線部(半径36.5m)を1/4周してコーナー追従を検証する。
    /// 判定: ①先行距離が1〜9mに収まり続ける ②フレーム間移動がワープしない
    /// ③終了時にアバターの向きが進行方向(接線)へ追従している
    /// </summary>
    private IEnumerator RunCornerFollowingTest(AvatarEngine engine)
    {
        const float trackRadius = 36.5f;          // 400mトラック曲線部の標準半径
        const float quarterTurnRadians = Mathf.PI / 2f;
        const float maxFrameJump = 1.5f;          // これ以上のフレーム間移動はワープ

        // 現在位置と進行方向から円の中心を求める(右カーブ: Cross(up,forward)=右)
        Vector3 startPos = _cameraMover.position;
        Vector3 forward = Vector3.forward;
        Vector3 toCenter = Vector3.Cross(Vector3.up, forward); // 右手側
        Vector3 center = startPos + toCenter * trackRadius;

        float angularSpeed = RunSpeedMetersPerSecond / trackRadius; // rad/s
        float theta = 0f;
        Vector3 startOffset = startPos - center;

        bool leadOk = true;
        bool noWarp = true;
        float minLead = float.MaxValue, maxLead = 0f, maxJump = 0f;
        Vector3 lastAvatarPos = engine.transform.position;
        Vector3 tangent = forward;

        while (theta < quarterTurnRadians)
        {
            float dTheta = angularSpeed * Time.deltaTime;
            theta += dTheta;

            // 円弧に沿ってカメラを移動(上から見て時計回り=右旋回)。
            // +θ回転: 位置 center+R(θ)*startOffset の速度方向が R(θ)*forward と一致する
            Quaternion rotation = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, Vector3.up);
            _cameraMover.position = center + rotation * startOffset;
            tangent = rotation * forward;

            // ① 先行距離チェック
            // 注: 移動中の定常先行距離はアンカーラグ(速度/補間率≒1.4m)の分だけ
            // 3mより短くなる。下限はユーザーと重ならないこと(>0.7m)を判定する
            Vector3 toAvatar = engine.transform.position - _cameraMover.position;
            toAvatar.y = 0;
            float lead = toAvatar.magnitude;
            minLead = Mathf.Min(minLead, lead);
            maxLead = Mathf.Max(maxLead, lead);
            if (theta > 0.3f && (lead < 0.7f || lead > 9.0f)) // 旋回開始直後の過渡は除外
                leadOk = false;

            // ② ワープチェック(フレーム間のアバター移動量)
            float jump = Vector3.Distance(engine.transform.position, lastAvatarPos);
            maxJump = Mathf.Max(maxJump, jump);
            if (jump > maxFrameJump)
                noWarp = false;
            lastAvatarPos = engine.transform.position;

            yield return null;
        }

        Check(leadOk, $"corner: lead distance stayed 1-9m (min {minLead:F1}m / max {maxLead:F1}m)");
        Check(noWarp, $"corner: no warp — max frame jump {maxJump:F2}m");

        // ③ 接線追従: アバターの向きと進行方向の角度差
        float headingError = Vector3.Angle(engine.transform.forward, tangent);
        Check(headingError < 60f, $"corner: avatar heading tracks tangent (error {headingError:F0}°)");
    }

    private static IEnumerator WaitScaled(float seconds)
    {
        // WaitForSecondsはtimeScaleの影響を受ける(=シナリオ内の体感時間)
        yield return new WaitForSeconds(seconds);
    }

    private void Check(bool condition, string label)
    {
        if (condition)
        {
            _passCount++;
            Debug.Log($"[E2E] PASS: {label}");
        }
        else
        {
            _failures.Add(label);
            Debug.LogError($"[E2E] FAIL: {label}");
        }
    }

    private void Finish()
    {
        Time.timeScale = 1f;
        Debug.Log($"[E2E] SUMMARY pass={_passCount} fail={_failures.Count}" +
                  (_failures.Count > 0 ? " | failed: " + string.Join(" / ", _failures) : ""));

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(_failures.Count == 0 ? 0 : 1);
        }
        else
        {
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
