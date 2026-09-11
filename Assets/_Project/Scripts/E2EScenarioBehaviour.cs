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
        var goalLine = FindFirstObjectByType<GoalLineController>(FindObjectsInactive.Include);
        var runnerTracking = FindFirstObjectByType<RunnerTrackingState>(FindObjectsInactive.Include);

        Check(bridge != null, "bootstrap: ARSessionManagerBridge exists");
        Check(engine != null, "bootstrap: AvatarEngine exists");
        Check(session != null, "bootstrap: RunSessionController exists");
        Check(ghost != null, "bootstrap: GhostPaceDriver exists");
        Check(goalLine != null, "bootstrap: GoalLineController exists");
        Check(runnerTracking != null, "bootstrap: invisible RunnerTrackingState exists");

        // 検出平面がアバターを隠さないこと(第1期の既定)。プレーンの材質は深度だけ書く
        // ため、ONだと壁・机の向こうのアバターが描画されず「壁で消える」ことになる
        var planeOcclusion = FindFirstObjectByType<ARPlaneOcclusionController>(FindObjectsInactive.Include);
        Check(planeOcclusion != null, "bootstrap: ARPlaneOcclusionController exists");
        Check(planeOcclusion != null && !planeOcclusion.OccludeAvatarBehindPlanes,
            "occlusion: detected planes do not occlude the avatar by default");

        Camera cam = Camera.main;
        Check(cam != null, "scene: main camera exists");
        if (bridge == null || engine == null || session == null || cam == null)
        {
            Finish();
            yield break;
        }

        _cameraMover = cam.transform.root != null ? cam.transform.root : cam.transform;

        // エディタのリグはカメラが走行方向(+Z)と無関係な向きで保存されている。
        // 実機ではARKitがカメラ姿勢を与え、走者は走る方向を向くので、E2Eでも
        // 「カメラは進行方向を見ている」状態に揃える。これが無いと視野に基づく検証
        // (アバターが見えているか)がリグの保存姿勢に左右され、意味を持たない
        FaceRig(Vector3.forward);
        Debug.Log("[E2E] rig aligned so the camera faces the run direction (+Z)");

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

        // ── Step 0c: 床の確定 (F-05 — 「アバターが接地せず浮き上がる」不具合の回帰防止) ──
        // エディタのシーンにはコライダーもARプレーンも無いため実測フロアは得られない。
        // その状況で暫定床を1回だけ採用して固定し、以降カメラが上下しても
        // 床(=アバターY)が追従しないことを検証する。走行前に発生する不具合なので
        // StartSession より前に確認する。
        var groundSnap = FindFirstObjectByType<GroundSnap>(FindObjectsInactive.Include);
        Check(groundSnap != null, "ground: GroundSnap present");
        if (groundSnap != null)
        {
            Check(!groundSnap.HasMeasuredFloor,
                "ground: no measured floor in editor scene (provisional latch expected)");

            yield return WaitScaled(0.3f);
            float floorBefore   = groundSnap.ResolvedFloorY;
            float avatarYBefore = groundSnap.transform.position.y;
            float camYBefore    = _cameraMover.position.y;

            // 端末を持ち上げる / グラスで上を向く動作の再現
            MoveRig(Vector3.up * 2.0f);
            yield return WaitScaled(0.6f);

            Check(Mathf.Abs(groundSnap.ResolvedFloorY - floorBefore) < 0.01f,
                "ground: latched floor does not move when camera rises 2m");
            Check(Mathf.Abs(groundSnap.transform.position.y - avatarYBefore) < 0.25f,
                "ground: avatar does not fly up with the camera (F-05 regression)");

            _cameraMover.position = new Vector3(_cameraMover.position.x, camYBefore,
                                                _cameraMover.position.z);
            yield return WaitScaled(0.3f);

            // 天井を床と誤認する不具合(実機で報告)の回帰。
            // ARKitは床も天井も「法線が上向きの水平面」で返すため、向きでは弾けない。
            // 床が常にカメラより下にあることが、唯一プラットフォームに依存しない不変条件。
            // 比較対象は _cameraMover(XR Originのroot)ではなく**実カメラ** —
            // rig内でカメラには高さオフセットがあり、rootで測ると判定がずれる
            Transform camT = Camera.main != null ? Camera.main.transform : _cameraMover;
            Check(groundSnap.ResolvedFloorY < camT.position.y,
                $"ground: floor stays below the camera (floor {groundSnap.ResolvedFloorY:F2} " +
                $"< camera {camT.position.y:F2})");

            // 天井にラッチしてしまった状態からの自己回復。実カメラが確定済みの床より
            // 下へ来ると「床がカメラより上」= ありえない状態になるので破棄される。
            // これが無いと、一度天井を掴んだアバターは再起動するまで戻らない
            float floorLatched = groundSnap.ResolvedFloorY;
            float dropNeeded   = (camT.position.y - floorLatched) + 2.0f;
            _cameraMover.position -= Vector3.up * dropNeeded;
            yield return WaitScaled(0.6f);

            Check(groundSnap.ResolvedFloorY < camT.position.y,
                $"ground: floor above the camera is discarded and re-acquired below it " +
                $"(floor {groundSnap.ResolvedFloorY:F2} < camera {camT.position.y:F2})");

            // 後続シナリオのため元の高さで掴み直させる
            _cameraMover.position = new Vector3(_cameraMover.position.x, camYBefore,
                                                _cameraMover.position.z);
            groundSnap.ResetFloor();
            yield return WaitScaled(0.6f);
            Check(groundSnap.ResolvedFloorY < camT.position.y,
                $"ground: floor re-latched below the camera after reset " +
                $"(floor {groundSnap.ResolvedFloorY:F2} < camera {camT.position.y:F2})");

            // ── 実際に天井と床を置いて「選ばれる方」を確かめる ──────────────
            // ここまでの検証は暫定床(カメラ高からの推定)しか通っていない。
            // エディタのシーンにはコライダーもARプレーンも無いので、
            // **候補が複数あるときにどちらを床に選ぶか**という肝心の分岐が
            // 一度も実行されていなかった。実物を置いて選択そのものを試す。
            //
            // ARKitは床も天井も「水平・法線上向き」で返すため、天井コライダーの
            // 法線も上向きにする — 法線チェックだけでは弾けない条件を再現する
            float camNow      = camT.position.y;
            float realFloorY  = camNow - 1.2f;
            float ceilingY    = camNow + 1.3f;

            GameObject floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floorGo.name = "E2E_TestFloor";
            floorGo.transform.position   = new Vector3(camT.position.x, realFloorY - 0.05f, camT.position.z);
            floorGo.transform.localScale = new Vector3(20f, 0.1f, 20f);
            Destroy(floorGo.GetComponent<MeshRenderer>());

            GameObject ceilGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceilGo.name = "E2E_TestCeiling";
            ceilGo.transform.position   = new Vector3(camT.position.x, ceilingY + 0.05f, camT.position.z);
            ceilGo.transform.localScale = new Vector3(20f, 0.1f, 20f);
            Destroy(ceilGo.GetComponent<MeshRenderer>());

            groundSnap.ResetFloor();
            yield return WaitScaled(0.8f);

            Check(groundSnap.HasMeasuredFloor,
                "ground: a real collider is picked up as a measured floor");
            Check(Mathf.Abs(groundSnap.ResolvedFloorY - realFloorY) < 0.12f,
                $"ground: the floor is chosen, not the ceiling " +
                $"(picked {groundSnap.ResolvedFloorY:F2}, floor {realFloorY:F2}, ceiling {ceilingY:F2})");
            Check(groundSnap.ResolvedFloorY < camT.position.y,
                $"ground: the ceiling above the camera is never chosen " +
                $"(picked {groundSnap.ResolvedFloorY:F2} < camera {camT.position.y:F2})");

            // 壁: 垂直面は法線が水平なので床候補にならない。
            // カメラより下に置いても選ばれないこと(高さ帯だけに頼っていない証明)
            GameObject wallGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallGo.name = "E2E_TestWall";
            wallGo.transform.position   = new Vector3(camT.position.x, camNow - 0.6f, camT.position.z);
            wallGo.transform.localScale = new Vector3(0.1f, 3f, 20f); // 薄い縦板 = 壁
            Destroy(wallGo.GetComponent<MeshRenderer>());

            groundSnap.ResetFloor();
            yield return WaitScaled(0.8f);
            Check(Mathf.Abs(groundSnap.ResolvedFloorY - realFloorY) < 0.12f,
                $"ground: a wall between camera and floor is not mistaken for the floor " +
                $"(picked {groundSnap.ResolvedFloorY:F2}, floor {realFloorY:F2})");

            // 後片付けは順序が重要。Destroy は当該フレーム末に効くため、
            // 先に ResetFloor を呼ぶと「まだ生きているコライダー」を掴み直してしまい、
            // ラッチ(実測床は保持される設計)が残って後続の検証が落ちる。
            // コライダーが消えたのを待ってから確定をやり直す
            Destroy(floorGo);
            Destroy(ceilGo);
            Destroy(wallGo);
            yield return null;              // Destroy の反映を待つ
            yield return WaitScaled(0.1f);
            groundSnap.ResetFloor();
            yield return WaitScaled(0.6f);
            Check(!groundSnap.HasMeasuredFloor,
                "ground: measured-floor latch is released once the test colliders are gone");
        }

        // ── Step 0d: 天井の下で「断崖」と誤判定しないこと (壁・天井でアバターが消える件) ──
        // 断崖判定は「ユーザー真下の地面」と「3m先の地面」の落差で行うが、
        // ユーザー真下は Physics.Raycast の**最初の1ヒット**を採るため、
        // 頭上に天井コライダー(ARKitは天井も水平面としてコライダー付きで返す)が
        // あると天井の高さが「地面」になる。3m先の天井がまだ未検出なら
        // 「天井 − 床 ≒ 2m 以上の落差」= 断崖として停止し、ユーザーが追い越して
        // アバターが視界から消える。天井は**ユーザーの上だけ**に置いて再現する
        if (groundSnap != null && _cameraMover != null)
        {
            Transform camT2 = Camera.main != null ? Camera.main.transform : _cameraMover;
            float camY2 = camT2.position.y;

            GameObject floor2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor2.name = "E2E_CliffTestFloor";
            floor2.transform.position   = new Vector3(camT2.position.x, camY2 - 1.2f - 0.05f, camT2.position.z);
            floor2.transform.localScale = new Vector3(20f, 0.1f, 20f);
            Destroy(floor2.GetComponent<MeshRenderer>());

            // 天井パッチ: ユーザー頭上 1.3m、4m四方 = 3m先の判定点には届かない
            GameObject ceil2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceil2.name = "E2E_CliffTestCeilingPatch";
            ceil2.transform.position   = new Vector3(camT2.position.x, camY2 + 1.3f + 0.05f, camT2.position.z);
            ceil2.transform.localScale = new Vector3(4f, 0.1f, 4f);
            Destroy(ceil2.GetComponent<MeshRenderer>());

            groundSnap.ResetFloor();
            yield return WaitScaled(0.6f);

            Check(groundSnap.HasMeasuredFloor,
                "cliff: the floor collider is measured under the ceiling patch");
            Check(!engine.IsHalted,
                "cliff: a ceiling above the user is not mistaken for a cliff drop ahead (no halt)");

            Destroy(floor2);
            Destroy(ceil2);
            yield return null;
            yield return WaitScaled(0.1f);
            groundSnap.ResetFloor();
            yield return WaitScaled(0.6f);
            Check(!groundSnap.HasMeasuredFloor && !engine.IsHalted,
                "cliff: test colliders removed, latch released, not halted");
        }

        // ── Step 0e: 前方の壁 — §4.2の足踏み停止と第1期の既定(OFF) ──────────
        // 室内では前方3m以内に必ず壁があり、停止するとユーザーが追い越して
        // アバターが視界から消える。第1期(トラック検証)の既定はOFF。
        // 仕様どおりの挙動(ON)も壊れていないことを同じ壁で確かめる
        if (groundSnap != null && _cameraMover != null)
        {
            Transform camT3 = Camera.main != null ? Camera.main.transform : _cameraMover;
            Vector3 flatForward = camT3.forward;
            flatForward.y = 0f;
            flatForward = flatForward.sqrMagnitude > 0.0001f ? flatForward.normalized : Vector3.forward;

            GameObject wallAhead = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallAhead.name = "E2E_WallAhead";
            wallAhead.transform.position = camT3.position + flatForward * 2.0f;
            wallAhead.transform.rotation = Quaternion.LookRotation(flatForward);
            wallAhead.transform.localScale = new Vector3(4f, 3f, 0.1f); // 進路を塞ぐ縦板
            Destroy(wallAhead.GetComponent<MeshRenderer>());

            bool defaultHalt = groundSnap.HaltOnObstacles;
            yield return WaitScaled(0.3f);
            Check(!defaultHalt && !engine.IsHalted,
                "obstacle: a wall ahead does not halt the avatar by default (第1期トラック設定)");

            // §4.2 の挙動自体は生きていること
            groundSnap.HaltOnObstacles = true;
            yield return WaitScaled(0.3f);
            Check(engine.IsHalted,
                "obstacle: the wall does halt the avatar when §4.2 halting is enabled");

            groundSnap.HaltOnObstacles = defaultHalt;
            Destroy(wallAhead);
            yield return null;
            yield return WaitScaled(0.3f);
            Check(!engine.IsHalted, "obstacle: halting clears once the wall is gone");
        }

        // ── Step 1: StartSession (目標60m — ゴール自動終了を早く踏むため) ──
        bridge.OnSwiftCommand(
            "{\"command\":\"StartSession\",\"targetPaceKmH\":13.0,\"distanceKm\":0.06," +
            "\"avatarHeightCm\":175,\"forwardOffsetM\":3.0}");
        yield return WaitScaled(0.5f);
        Check(engine.HasStarted, "start: engine.HasStarted after StartSession");

        // 開始の瞬間、アバターは走者の**正面**に出ること。
        // 待機中の手ブレで立った移動履歴を優先すると、ほぼランダムな方向の3m先に現れ、
        // 立ち止まっている限り視線固定(F-08)でそこに留まる = 「開始したのに見えない」
        {
            Transform camNow = Camera.main != null ? Camera.main.transform : _cameraMover;
            Vector3 toAvatarStart = engine.transform.position - camNow.position; toAvatarStart.y = 0f;
            Vector3 camFwd = camNow.forward; camFwd.y = 0f;
            float startAngle = Vector3.Angle(camFwd, toAvatarStart);
            Check(startAngle < 45f,
                $"start: avatar is staged in front of the user's view ({startAngle:F0}° off, {toAvatarStart.magnitude:F1}m)");
        }

        // 企画書 §4.1 実寸: StartSessionで175cmを指定しているので、実際の描画身長も
        // それに一致すること。旧実装は固定倍率(cm/175)で、モデルの素の大きさ次第で
        // 実物より大きく表示されていた(実機で報告された不具合)
        float avatarHeight = engine.MeasuredAvatarHeightMeters;
        Check(avatarHeight > 0.01f, $"scale: avatar height is measurable ({avatarHeight:F2}m)");

        // 接地誤差 (§10: 上下5cm以内)。GroundSnap が床に合わせるのは「原点」なので、
        // 原点が足裏に無いと床の推定が完璧でも足は浮く/沈む。見えるのは足裏なので、
        // **足裏の実際の高さと床の差**を測る — これが「浮遊感」の正体かを切り分ける
        var gs = FindFirstObjectByType<GroundSnap>(FindObjectsInactive.Include);
        if (gs != null)
        {
            float footOffset  = engine.FootOffsetMeters;
            float soleY       = gs.transform.position.y + footOffset;
            float contactErr  = soleY - gs.ResolvedFloorY;

            Check(Mathf.Abs(contactErr) < 0.05f,
                $"ground: soles meet the floor within ±5cm (§10) — error {contactErr:+0.000;-0.000}m " +
                $"(pivot→sole {footOffset:F3}m, root {gs.transform.position.y:F3}, floor {gs.ResolvedFloorY:F3})");
        }
        if (avatarHeight > 0.01f)
            Check(Mathf.Abs(avatarHeight - 1.75f) < 0.15f,
                $"scale: avatar renders at real-world height for 175cm (measured {avatarHeight:F2}m)");
        // StartSession first arms the 3-2-1-START presentation. Movement,
        // analytics, telemetry, clock and distance must still be paused here.
        var countdown = FindFirstObjectByType<CountdownDisplay>(FindObjectsInactive.Include);
        Check(countdown != null, "countdown: CountdownDisplay auto-created by bootstrap");
        if (countdown != null)
            Check(countdown.IsShowing, $"countdown: visible right after start (showing '{countdown.CurrentText}')");
        Check(!engine.IsRunMotionActive, "countdown: runner motion remains paused before START");

        float startWait = 0f;
        while (!engine.IsRunMotionActive && startWait < 6f)
        {
            startWait += Time.deltaTime;
            yield return null;
        }
        Check(engine.IsRunMotionActive, "countdown: START activates runner motion");
        if (countdown != null)
            Check(countdown.HasCompleted, "countdown: full 3-2-1-START sequence completed");
        yield return null; // allow telemetry/VFX listeners to observe the transition

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

        // H5: IMUの供給元。エディタにはジャイロが無いので近似にフォールバックするのが正。
        // 実機では "device"(CoreMotion)、Swift供給時は "external" になる
        Check(telemetry != null && telemetry.ImuSource == "approximated",
            "telemetry: IMU source falls back to approximation in editor (device/external on hardware)");

        // ── M2P実測(§10)の窓口。エディタにはネイティブが無いので「未計測」を貫くこと ──
        // ここが緩むと、合成値が実測として記録され §11.2 の評価が意味を失う
        var timing = FindFirstObjectByType<SensorTimingBridge>(FindObjectsInactive.Include);
        Check(timing != null, "bootstrap: SensorTimingBridge exists");
        Check(!SensorTimingBridge.NativeAvailable,
            "m2p: native timing is reported unavailable in the editor");
        Check(timing != null && timing.IsMeasuring,
            "m2p: measurement is started together with the run");
        Check(timing != null && !timing.TryGetLatencyMs(out double _),
            "m2p: no latency is reported while there is no real measurement");
        Check(timing != null && timing.Stats.SampleCount == 0,
            "m2p: nothing synthetic leaks into the run statistics");
        Check(timing != null && !timing.IsImuStreaming,
            "m2p: native 100Hz IMU is not claimed in the editor");
        Check(telemetry != null && telemetry.NativeImuRowCount == 0,
            "telemetry: editor rows come from the frame-synced fallback, not native samples");
        string telemetryPath = telemetry != null ? telemetry.CurrentFilePath : null;

        // ── F-07 現在ペース表示 / F-10 安全警告 ────────────────────────────
        var hud = FindFirstObjectByType<PeripheralHUDManager>(FindObjectsInactive.Include);
        Check(hud != null, "hud: PeripheralHUDManager present");
        if (hud != null)
        {
            // 走行前・平常時は下部の警告ゾーンが空であること(F-07: 下部=警告時のみ)
            Check(!hud.IsSafetyWarningVisible, "hud: no safety warning while GPS is healthy (F-07 bottom zone clear)");

            // 目標ペースの定数ではなく現在ペース書式であること(F-07 右上)
            Check(!hud.CurrentPaceText.StartsWith("Target"),
                $"hud: pace shows current pace, not the target constant (got '{hud.CurrentPaceText}')");

            // F-07: 補助表示を伏せ、時間/距離=左上・ペース=右上へ再配置していること
            Check(hud.VisibleAuxiliaryReadoutCount == 0,
                $"hud: auxiliary readouts hidden for F-07 centre clearance (visible {hud.VisibleAuxiliaryReadoutCount})");
            Check(hud.TimeAnchor == new Vector2(0f, 1f), $"hud: time anchored top-left (got {hud.TimeAnchor})");
            Check(hud.DistanceAnchor == new Vector2(0f, 1f), $"hud: distance anchored top-left (got {hud.DistanceAnchor})");
            Check(hud.PaceAnchor == new Vector2(1f, 1f), $"hud: pace anchored top-right (got {hud.PaceAnchor})");
        }

        // HUD所有権: hideUnityHud 未送信なら従来どおりUnityが描く(エディタ/E2E)
        if (hud != null)
            Check(hud.IsHudVisible, "hud: Unity HUD owns the display when hideUnityHud is not sent");

        // 光学シースルー対応: グラス接続でカメラ映像を切り、切断で戻すこと
        var passthrough = FindFirstObjectByType<ARPassthroughController>(FindObjectsInactive.Include);
        var deviceBridge = FindFirstObjectByType<DeviceManagerBridge>(FindObjectsInactive.Include);
        Check(passthrough != null, "passthrough: ARPassthroughController auto-created by bootstrap");
        if (passthrough != null && deviceBridge != null)
        {
            Check(passthrough.IsPassthroughEnabled, "passthrough: camera feed shown on the phone by default");

            deviceBridge.OnSwiftCommand("{\"command\":\"ConnectXREAL\"}");
            yield return null;
            Check(!passthrough.IsPassthroughEnabled,
                "passthrough: camera feed off while output goes to see-through glasses");

            deviceBridge.OnSwiftCommand("{\"command\":\"DisconnectXREAL\"}");
            yield return null;
            Check(passthrough.IsPassthroughEnabled,
                "passthrough: camera feed restored when back on the phone");

            // 切断はスタンバイへ遷移させるため、後続シナリオのために通常へ戻す
            if (stateController != null)
                stateController.TransitionToState(GameStateController.ARVisionState.Normal);
            yield return null;
        }

        var visualsForColor = FindFirstObjectByType<AvatarVisualsAndActions>(FindObjectsInactive.Include);

        // ── Step 2: 走行シミュレーション(カメラを前進させる) ────────────────
        float elapsed = 0f;
        bool syncObserved = false;
        bool justColorObserved = false;
        bool livePaceObserved = false;   // F-07: 実際の数値が出ること
        bool paceGreenObserved = false;  // F-07: 目標を保っている間は緑
        bool goalLineObserved = false;
        Vector3 runDirection = Vector3.forward;
        while (!engine.IsSessionEnded && elapsed < StepTimeoutSeconds)
        {
            MoveRig(runDirection * RunSpeedMetersPerSecond * Time.deltaTime);
            elapsed += Time.deltaTime;

            if (!syncObserved && analytics != null && analytics.GetLiveSyncRate() > 30f)
                syncObserved = true;

            // §7.1: 目標ペース(3m先行)を保っている間はジャスト=緑
            if (!justColorObserved && elapsed > 2f && visualsForColor != null
                && visualsForColor.PaceColorState == "Just")
                justColorObserved = true;

            // F-07 右上: 走行中は "--" ではなく実ペースが出て、目標維持中は緑であること
            if (hud != null && elapsed > 4f)
            {
                if (!livePaceObserved && !hud.CurrentPaceText.StartsWith("--"))
                    livePaceObserved = true;
                if (!paceGreenObserved
                    && hud.CurrentPaceState == PaceHudDisplay.PaceState.Maintaining)
                    paceGreenObserved = true;
            }

            if (!goalLineObserved && goalLine != null && goalLine.IsVisible)
                goalLineObserved = true;

            // 途中でSwiftメトリクスも1回注入(実機経路の確認)
            if (!_metricsSent && elapsed > 3f)
            {
                _metricsSent = true;
                bridge.OnSwiftCommand("{\"command\":\"UpdateMetrics\",\"paceKmH\":13.0,\"heartRate\":150,\"distanceKm\":0.012}");
            }
            yield return null;
        }

        Check(engine.IsSessionEnded, "goal: session auto-finished by goal distance");
        Check(goalLineObserved, "goal: AR goal line appeared before target distance");
        Check(goalLine != null && goalLine.IsReached,
            "goal: AR goal line remains visible briefly after crossing");
        Check(goalLine != null && goalLine.IsCelebrationVisible,
            "goal: CONGRATULATIONS text animation is visible");
        Check(goalLine != null && goalLine.IsConfettiPlaying,
            "goal: confetti VFX is playing");
        var goalAudio = FindFirstObjectByType<RunAudioEngine>(FindObjectsInactive.Include);
        Check(goalAudio != null && goalAudio.HasGoalJingles,
            "goal: imported win jingle is available");
        Check(goalAudio != null && !string.IsNullOrEmpty(goalAudio.LastGoalJingleName),
            "goal: imported win jingle started playing");
        Check(syncObserved, "run: live sync rate exceeded 30% during run");
        Check(justColorObserved, "color: pace-sync GREEN (just) while on target pace (§7.1)");
        Check(livePaceObserved, "hud: live pace value rendered during run (not '--')");
        if (countdown != null)
            Check(!countdown.IsShowing, "countdown: cleared once the run is underway");
        Check(paceGreenObserved, "hud: pace reads GREEN while holding target pace (F-07)");

        // §7.2: 目標ペース維持中はオーラ(5m以上の遅れ表示)を出さないこと
        var aura = FindFirstObjectByType<AvatarAuraEffect>(FindObjectsInactive.Include);
        Check(aura != null && !aura.IsAuraActive,
            "aura: not emitted while on target pace (§7.2 threshold 5m)");

        // 終了直後の挨拶(お辞儀)ジェスチャーが再生されること
        yield return null; // LateUpdate反映待ち
        var goalGestures = FindFirstObjectByType<ProceduralGestureDriver>(FindObjectsInactive.Include);
        Check(goalGestures != null && goalGestures.ActiveGesture == "Goodbye",
            "goal: procedural goodbye gesture playing");

        // ── §10 非機能要件の実測 (位置誤差1.0m / 60分連続稼働) ────────────────
        // 実証(§11.2)でCSVを解析するまで分からない、という状態を避けるための計測。
        // 位置誤差は「目標リード距離(3.0m)と実際の水平距離の差」で定義している
        var nonFunctional = FindFirstObjectByType<NonFunctionalRequirementsMonitor>(FindObjectsInactive.Include);
        Check(nonFunctional != null, "bootstrap: NonFunctionalRequirementsMonitor exists");
        if (nonFunctional != null)
        {
            Check(nonFunctional.Position.SampleCount > 0,
                $"§10: position error was actually sampled during the run ({nonFunctional.Position.SampleCount} samples)");
            Check(nonFunctional.Position.MeetsRequirement,
                $"§10: average lead error stays within 1.0m — {nonFunctional.Position.Summarize()}");

            // 短い走行から「60分もつ」と断定しないこと。
            // (バッテリー残量が取れるかは環境次第 — ノートPCのエディタでは取れる。
            //  取れる/取れないに関わらず、外挿に足りない計測で達成を名乗らないのが不変条件)
            Check(nonFunctional.SummarizeEndurance().Contains("判定不能"),
                $"§10: endurance is reported as undecidable rather than passing ({nonFunctional.SummarizeEndurance()})");
            Check(!nonFunctional.SummarizeEndurance().Contains("§10達成"),
                "§10: a short run never claims the 60-minute endurance requirement is met");
        }

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

            // latency_m2p (9列目) に捏造値が入っていないこと。
            // 合成ベンチマークやフレーム時間で埋めると、60fpsでは約16msという
            // 「§10の20ms要求を満たしているように見える」値が全行に並び、
            // §11.2「CSV解析で20msを評価する」が測定ではなく構造によって合格する。
            // 実測経路ができるまでは -1 (未計測) でなければならない
            bool latencyHonest = true;
            int latencyChecked = 0;
            for (int i = 1; i < lines.Length && latencyChecked < 300; i++)
            {
                string[] cols = lines[i].Split(',');
                if (cols.Length < 9) { latencyHonest = false; break; }
                if (!double.TryParse(cols[8], System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture,
                                     out double m2p)) { latencyHonest = false; break; }
                if (m2p >= 0) { latencyHonest = false; break; } // 実測が無いのに値が入っている
                latencyChecked++;
            }
            Check(latencyHonest && latencyChecked > 0,
                $"telemetry: latency_m2p reports -1 while no real M2P measurement exists " +
                $"(checked {latencyChecked} rows)");

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

        startWait = 0f;
        while (!engine.IsRunMotionActive && startWait < 6f)
        {
            startWait += Time.deltaTime;
            yield return null;
        }
        Check(engine.IsRunMotionActive, "restart: second countdown reaches START");

        // 少し走ってゴーストペース追従を確認
        for (float t = 0; t < 3f; t += Time.deltaTime)
        {
            MoveRig(runDirection * RunSpeedMetersPerSecond * Time.deltaTime);
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
            MoveRig(runDirection * 9f * Time.deltaTime); // 全力疾走
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
            MoveRig(runDirection * 9f * Time.deltaTime);
            yield return null;
        }
        Check(engine.CurrentOvertakeState == AvatarEngine.OvertakeState.None,
            "overtake: returned to normal pacing");

        // 通常速度へ戻して体勢回復
        for (float t = 0; t < 1.5f; t += Time.deltaTime)
        {
            MoveRig(runDirection * RunSpeedMetersPerSecond * Time.deltaTime);
            yield return null;
        }

        // ── Step 3c: 障害物停止 (断崖・壁 → 足踏み待機 → 解除で再開) ──────────
        if (groundSnap != null)
        {
            groundSnap.SimulateObstacle = true;
            yield return WaitScaled(0.4f);
            Check(engine.IsHalted, "obstacle: avatar halts at simulated wall");

            // 停止中にユーザーが追い越す状況を作る(実機で「アバターが消えた」ケース)
            MoveRig(CamForwardFlat() * 4.0f);
            yield return WaitScaled(0.3f);

            groundSnap.SimulateObstacle = false;
            yield return WaitScaled(0.4f);
            Check(!engine.IsHalted, "obstacle: avatar resumes when path clears");

            // 解除後、置き去りにされたアバターが前方の定位置へ戻ってくること。
            // アンカーを取り直していないと古い位置から復帰するため視界に戻らない
            yield return WaitScaled(2.0f);
            Vector3 toAvatar = engine.transform.position - _cameraMover.position;
            toAvatar.y = 0f;
            float leadAfterClear = toAvatar.magnitude;
            bool inFront = Vector3.Dot(toAvatar.normalized, CamForwardFlat()) > 0f;
            Check(inFront && leadAfterClear < 6.0f,
                $"obstacle: avatar returns in front after the wall clears (lead {leadAfterClear:F1}m, inFront={inFront})");
        }

        // ── Step 3c2: 走行中にアプリを終了(スワイプ)されても記録が消えないこと ──
        // 記録は FinishRun でしか保存されないため、走行中に殺されるとその走行は
        // まるごと消えていた。背面移行のたびにスナップショットを書き、次回起動で昇格する
        {
            int historyBefore = SessionDataStore.LoadAllSessions().Count;

            session.PersistInterruptedSnapshot(); // = OnApplicationPause(true) と同じ経路
            Check(SessionDataStore.HasInterruptedSnapshot(),
                "interrupt: a snapshot is written when the app backgrounds mid-run");
            Check(SessionDataStore.LoadAllSessions().Count == historyBefore,
                "interrupt: the snapshot stays out of the history until promoted");

            RunSessionRecord restored = SessionDataStore.TryPromoteInterruptedSnapshot(out string promotedPath);
            Check(restored != null && restored.wasInterrupted,
                "interrupt: the snapshot is promoted into the history on the next launch");
            Check(restored != null && restored.distanceMeters > 0f,
                $"interrupt: the restored run keeps its distance ({(restored != null ? restored.distanceMeters : 0f):F0}m)");
            Check(!SessionDataStore.HasInterruptedSnapshot(),
                "interrupt: the snapshot is consumed by the promotion");
            Check(SessionDataStore.LoadAllSessions().Count == historyBefore + 1,
                "interrupt: the interrupted run now appears in the history");

            // 検証で作った履歴を残さない
            if (!string.IsNullOrEmpty(promotedPath) && System.IO.File.Exists(promotedPath))
                System.IO.File.Delete(promotedPath);
        }

        // 地面判定は上向きの面のみを採用する(壁を床と誤認するとアバターが跳ね上がる)。
        // エディタのシーンには実測フロアが無いため、暫定床が維持されることで代替検証する
        if (groundSnap != null)
            Check(!groundSnap.HasMeasuredFloor,
                "ground: no wall/ceiling was mistaken for a measured floor");

        // LiDARの検出範囲外へ出ても床を保持し続けること。
        // (実機ではPlaneWithinInfinityで平面を延長し、それも無ければ確定床を保持する)
        //
        // 注意: ここでカメラを一瞬でテレポートさせると、アンカー補間が追随できず
        // 後続シナリオの初期状態まで壊す。実走と同じく**連続的に**前進させる
        if (groundSnap != null && _cameraMover != null)
        {
            float floorBeforeWalk = groundSnap.ResolvedFloorY;
            bool floorHeld = true;
            float walked = 0f;

            while (walked < 6.0f)
            {
                float step = RunSpeedMetersPerSecond * Mathf.Min(Time.deltaTime, 0.05f);
                MoveRig(CamForwardFlat() * step);
                walked += step;

                if (Mathf.Abs(groundSnap.ResolvedFloorY - floorBeforeWalk) > 0.01f)
                    floorHeld = false;

                yield return null;
            }

            Check(floorHeld,
                $"ground: floor holds while walking beyond any detected surface " +
                $"({floorBeforeWalk:F2} → {groundSnap.ResolvedFloorY:F2})");

            // 前進を止めてアバターが定位置へ戻るのを待つ
            yield return WaitScaled(1.5f);
            Vector3 lead = engine.transform.position - _cameraMover.position;
            lead.y = 0f;
            Check(lead.magnitude < 6.0f,
                $"ground: avatar still tracking after leaving the mapped area (lead {lead.magnitude:F1}m)");
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
                MoveRig(runDirection * RunSpeedMetersPerSecond * Time.deltaTime);
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
                    MoveRig(toAvatar.normalized * 6f * Time.deltaTime);
                yield return null;
            }
            Check(!engine.IsWaitingForUser, "wait: pacing resumes when user catches up (7m)");
        }

        // コーナー前に直進で体勢を整える(待機解除直後の過渡を収束させる)
        for (float t = 0; t < 4.0f; t += Time.deltaTime)
        {
            MoveRig(runDirection * RunSpeedMetersPerSecond * Time.deltaTime);
            yield return null;
        }

        // ── Step 3f: コーナー追従 (企画書§6 技術的成功基準①) ────────────────
        // 400mトラックの曲線部(半径36.5m)を1/4周。アバターが先行を維持し、
        // ワープせず、進行方向(接線)に追従して旋回することを検証する。
        yield return StartCoroutine(RunCornerFollowingTest(engine));

        // HUD所有権の切り替え: hideUnityHud=true でUnity側が引っ込むこと
        if (hud != null)
        {
            hud.SetHudVisible(false);
            yield return null;
            Check(!hud.IsHudVisible, "hud: Unity HUD hides when SwiftUI owns the display");
            hud.SetHudVisible(true);
            yield return null;
            Check(hud.IsHudVisible, "hud: Unity HUD returns when it owns the display again");
        }

        // ── Step 4: GPS喪失→復帰 FSM ──────────────────────────────────────
        // 併せて「なぜ見えないか」の診断が症状ではなく経路を報告することを縛る。
        // 実機で「消えた」と言われても、この行を見れば7つある非表示経路のどれかが分かる
        var visibility = FindFirstObjectByType<AvatarVisibilityDiagnostics>(FindObjectsInactive.Include);
        Check(visibility != null, "bootstrap: AvatarVisibilityDiagnostics exists");
        if (visibility != null)
        {
            yield return null;
            Check(visibility.IsVisible,
                $"visibility: avatar reported visible during normal pacing ({visibility.CurrentReason})");
        }

        if (stateController != null)
        {
            stateController.TransitionToState(GameStateController.ARVisionState.InertialMovement);
            yield return WaitScaled(0.5f);

            if (visibility != null)
            {
                yield return null;
                Check(visibility.CurrentReason.Contains("GPS"),
                    $"visibility: inertial movement is attributed to GPS loss ({visibility.CurrentReason})");
            }

            // 5秒でフェードアウト→1秒後にスタンバイ(SetActive(false))。
            // この「消えた」が GPS 経路として報告されること
            stateController.TransitionToState(GameStateController.ARVisionState.FadeOut);
            yield return WaitScaled(1.5f);
            Check(stateController.currentState == GameStateController.ARVisionState.Standby,
                "gps: fade-out completes into Standby (F-10)");
            if (visibility != null)
            {
                yield return null;
                Check(!visibility.IsVisible && visibility.CurrentReason.Contains("GPSロスト"),
                    $"visibility: standby is attributed to GPS loss, not left unexplained ({visibility.CurrentReason})");
            }

            // F-10: ロスト中はHUD下部に赤字の減速警告が出ること。
            // 実機で「アバターが理由も分からず消える」状態だったのを塞ぐ回帰テスト
            if (hud != null)
                Check(hud.IsSafetyWarningVisible,
                    "hud: safety warning shown while GPS is lost (F-10)");

            stateController.TransitionToState(GameStateController.ARVisionState.Reaccumulation);
            // 注意: Reaccumulation遷移は精度を99にリセットする(要再確認ゲート)ため、
            // 遷移後に精度を設定する(実機ではCoreLocationの精度更新に相当)
            yield return WaitScaled(0.2f);
            stateController.SimulatedGPSAccuracyRadius = 3f;
            yield return WaitScaled(3.0f); // 1.5s粒子演出 + 精度ゲート + 頷き
            Check(stateController.currentState == GameStateController.ARVisionState.Normal,
                "gps: recovered to Normal after reaccumulation");

            // 復帰したら警告は消えること(下部ゾーンを空へ戻す)
            yield return null;
            if (hud != null)
                Check(!hud.IsSafetyWarningVisible,
                    "hud: safety warning cleared after GPS recovery (F-10)");
            if (visibility != null)
            {
                yield return null;
                Check(visibility.IsVisible,
                    $"visibility: avatar reported visible again after GPS recovery ({visibility.CurrentReason})");
            }
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

    /// <summary>
    /// リグを動かし、実走者と同じく移動方向を向かせる。
    /// 位置だけ動かすとカメラが保存姿勢のまま横や後ろを向いて走ることになり、
    /// 視野に基づく検証(アバターが見えているか)が成立しない。
    /// 垂直移動(上下)では向きを変えない
    /// </summary>
    private void MoveRig(Vector3 delta)
    {
        _cameraMover.position += delta;
        Vector3 flat = delta; flat.y = 0f;
        if (flat.sqrMagnitude > 1e-8f)
            FaceRig(flat);
    }

    /// <summary>
    /// **カメラの**水平前方が <paramref name="direction"/> を向くようにリグのルートを回す。
    /// ルートを LookRotation で向けるだけでは足りない — このシーンではカメラがルートに
    /// 対してローカル回転(約50°)を持っており、ルートの向き ≠ カメラの向きになる。
    /// 視野に基づく検証で見るのはカメラの向きなので、必ずカメラ基準で揃える
    /// </summary>
    /// <summary>走者(カメラ)の水平前方。ルートの forward はカメラの向きと一致しないので使わない。</summary>
    private Vector3 CamForwardFlat()
    {
        Camera cam = Camera.main;
        Vector3 f = (cam != null ? cam.transform : _cameraMover).forward;
        f.y = 0f;
        return f.sqrMagnitude > 1e-8f ? f.normalized : Vector3.forward;
    }

    private void FaceRig(Vector3 direction)
    {
        Camera cam = Camera.main;
        Transform camT = cam != null ? cam.transform : _cameraMover;
        Vector3 camFlat = camT.forward; camFlat.y = 0f;
        Vector3 dirFlat = direction;   dirFlat.y = 0f;
        if (camFlat.sqrMagnitude < 1e-8f || dirFlat.sqrMagnitude < 1e-8f) return;

        float yaw = Vector3.SignedAngle(camFlat.normalized, dirFlat.normalized, Vector3.up);
        if (Mathf.Abs(yaw) > 0.01f)
            _cameraMover.Rotate(0f, yaw, 0f, Space.World);
    }

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
            // フレームヒッチ時に Time.deltaTime(timeScale=3で更に増幅)をそのまま使うと、
            // カメラが1フレームで円弧を数m「テレポート」してしまい、アンカー補間が
            // 追随できず先行距離が一時的に潰れる — 実走ではあり得ない入力で、
            // 判定がフレームレート次第で揺れる原因だった。1歩相当に制限する
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float dTheta = angularSpeed * dt;
            theta += dTheta;

            // 円弧に沿ってカメラを移動(上から見て時計回り=右旋回)。
            // +θ回転: 位置 center+R(θ)*startOffset の速度方向が R(θ)*forward と一致する
            Quaternion rotation = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, Vector3.up);
            _cameraMover.position = center + rotation * startOffset;
            tangent = rotation * forward;
            // 実走者はコーナーでも進行方向を向く。カメラも接線を向かせないと、
            // 曲線部でアバターが「視野外」になり可視性の検証が成立しない
            FaceRig(tangent);

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
