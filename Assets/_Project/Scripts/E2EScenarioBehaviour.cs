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

        // ── Step 1: StartSession (目標60m — ゴール自動終了を早く踏むため) ──
        bridge.OnSwiftCommand(
            "{\"command\":\"StartSession\",\"targetPaceKmH\":13.0,\"distanceKm\":0.06," +
            "\"avatarHeightCm\":175,\"forwardOffsetM\":3.0}");
        yield return WaitScaled(0.5f);
        Check(engine.HasStarted, "start: engine.HasStarted after StartSession");

        // ── Step 2: 走行シミュレーション(カメラを前進させる) ────────────────
        float elapsed = 0f;
        bool syncObserved = false;
        Vector3 runDirection = Vector3.forward;
        while (!engine.IsSessionEnded && elapsed < StepTimeoutSeconds)
        {
            _cameraMover.position += runDirection * RunSpeedMetersPerSecond * Time.deltaTime;
            elapsed += Time.deltaTime;

            if (!syncObserved && analytics != null && analytics.GetLiveSyncRate() > 30f)
                syncObserved = true;

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

        // ── Step 5: 履歴取得 ────────────────────────────────────────────────
        bridge.OnSwiftCommand("{\"command\":\"RequestHistory\"}");
        yield return WaitScaled(0.3f);
        Check(SessionDataStore.LoadAllSessions().Count > 0, "history: at least one session persisted");

        Finish();
    }

    private bool _metricsSent = false;

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
