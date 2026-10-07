using System.Collections;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 展示ブース(Kobe Calling)の体験モード。来場者は実物のARグラスを掛け、走らずに
/// 「走っているときの見え方」を体験する。実機ビルドにも含める(Swiftの体験モード画面から起動)。
///
/// <list type="bullet">
/// <item><b>Standing(立ったまま)</b>: <see cref="BoothDemoScript"/> の台本どおりのペース・距離を
/// 本番と同じブリッジ経路(StartSession / UpdateMetrics)へ流す。遅れ・追い抜きはアバターの
/// 追従位置をずらして見せる。約52秒(+カウントダウン)でゴールし自動終了する。</item>
/// <item><b>Walking(数歩あるく)</b>: 来場者自身の移動(ARKit)でアバターが3m前方を追従する。
/// 合成値は流さない。<see cref="walkingDurationSeconds"/> で終了。</item>
/// </list>
///
/// <para><b>アバターは体験中に一度も消さない</b>(2026-10-07 チーム判断): どちらのモードも
/// GPSロスト判定(F-09/F-10)を止め、アバターを来場者の正面に保つ
/// (<see cref="AvatarEngine.SetPresentationFollowsView"/> — 立ったまま向きを変えても視野から外れない)。
/// 走行本番の F-10 は仕様どおり。</para>
///
/// デモの走行は履歴・CSV・HealthKit に残さない(<see cref="ARSessionManagerBridge.IsPresentationSession"/>)。
/// 終了は BoothDemoEnded で Swift へ通知する(SessionEnded は送らない)。
/// </summary>
[DisallowMultipleComponent]
public sealed class BoothDemoController : MonoBehaviour
{
    public enum Mode { Standing, Walking }

    private const double MetersPerLatitudeDegree = 111_111.0;

    [Header("Standing (立ったまま)")]
    [Tooltip("台本の目標ペース(km/h)。StartBoothDemo の targetPaceKmH が優先")]
    [SerializeField, Range(6f, 20f)] private float standingPaceKmH = 12f;

    [Header("Walking (数歩あるく)")]
    [Tooltip("アバターの目標ペース(km/h)。歩く来場者の3m前をゆっくり走らせる")]
    [SerializeField, Range(3f, 12f)] private float walkingPaceKmH = 6f;
    [Tooltip("この秒数(START以降)で自動終了する")]
    [SerializeField, Range(15f, 300f)] private float walkingDurationSeconds = 60f;

    [Header("Common")]
    [Tooltip("アバターの前方距離(m)。仕様は3.0。全身をグラスの画角に入れるには約3.7m必要" +
             "(AGENTS.md §7-1 — チーム判断待ちのため既定は仕様どおり)")]
    [SerializeField, Range(2f, 6f)] private float leadDistanceMeters = 3.0f;
    [SerializeField, Range(0.05f, 1f)] private float metricIntervalSeconds = 0.2f;
    [Tooltip("合成の測位として送る水平精度(m)。良好(5m以内)にしておく")]
    [SerializeField] private float goodAccuracyMeters = 3f;
    [Tooltip("カウントダウン完了(START)を待つ上限(秒)")]
    [SerializeField] private float startTimeoutSeconds = 8f;
    [SerializeField] private double originLatitude = 34.690083;   // 神戸(合成座標の原点。表示はされない)
    [SerializeField] private double originLongitude = 135.195511;

    [Header("References (auto-found if empty)")]
    [SerializeField] private ARSessionManagerBridge bridge;
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private GpsSignalMonitor gpsMonitor;
    [SerializeField] private PeripheralHUDManager hudManager;
    [SerializeField] private GameStateController stateController;

    private Coroutine _routine;
    private Mode _mode;
    private bool _gpsHandlingSaved;
    private bool _savedGpsHandling;

    public bool IsRunning => _routine != null;
    public Mode CurrentMode => _mode;
    /// <summary>START からの経過秒(台本の時刻)。開始前は負。</summary>
    public float ElapsedSeconds { get; private set; } = -1f;
    /// <summary>現在の台本の区間(歩行モードでは OnPace のまま)。</summary>
    public BoothDemoScript.Beat CurrentBeat { get; private set; }
    /// <summary>直近に終了したデモが最後まで進んだか(E2E検証用)。</summary>
    public bool LastRunCompleted { get; private set; }

    public static Mode ParseMode(string mode)
        => string.Equals(mode, "walking", System.StringComparison.OrdinalIgnoreCase) ? Mode.Walking : Mode.Standing;

    private static string ModeName(Mode mode) => mode == Mode.Walking ? "walking" : "standing";

    /// <summary>
    /// デモを開始する。実行中なら中断してやり直す(次の来場者へすぐ回せるように)。
    /// </summary>
    /// <param name="targetPaceKmH">0以下ならモードの既定ペース</param>
    public void Begin(Mode mode, float targetPaceKmH = 0f)
    {
        ResolveReferences();
        if (bridge == null || avatarEngine == null)
        {
            Debug.LogError("[BOOTH DEMO] ARSessionManagerBridge / AvatarEngine が見つかりません。");
            SwiftMessageSender.SendBoothDemoEnded(ModeName(mode), false, "システムが初期化されていません");
            return;
        }

        if (IsRunning)
            Stop();

        // 利用者本人の走行を体験モードで上書きしない
        if (avatarEngine.HasStarted && !avatarEngine.IsSessionEnded && !bridge.IsPresentationSession)
        {
            Debug.LogWarning("[BOOTH DEMO] 走行中のため体験モードを開始しません。");
            SwiftMessageSender.SendBoothDemoEnded(ModeName(mode), false, "走行中は開始できません");
            return;
        }

        _mode = mode;
        float pace = targetPaceKmH > 0.1f ? targetPaceKmH : (mode == Mode.Walking ? walkingPaceKmH : standingPaceKmH);
        _routine = StartCoroutine(mode == Mode.Walking ? RunWalking(pace) : RunStanding(pace));
    }

    /// <summary>スタッフによる中断。走行を終了し、BoothDemoEnded(completed=false)を送る。</summary>
    public void Stop()
    {
        if (!IsRunning) return;
        StopCoroutine(_routine);
        _routine = null;
        EndSessionIfActive();
        Finish(completed: false, reason: "stopped");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 立ったまま: 台本を流す
    // ════════════════════════════════════════════════════════════════════════
    private IEnumerator RunStanding(float paceKmH)
    {
        float speed = paceKmH / 3.6f;
        float routeMeters = BoothDemoScript.RouteDistanceMeters(speed);

        // 体験中はアバターを消さない。合成GPSは常に良好だが、念のため判定自体を止める
        OverrideGpsHandling(false);
        StartSession(paceKmH, routeMeters / 1000f);

        // カウントダウン中もペース・距離0を送り、HUDに目標ペースを出しておく
        float nextMetricAt = 0f;
        float waitUntil = Time.time + startTimeoutSeconds;
        while (!avatarEngine.IsRunMotionActive)
        {
            if (Time.time >= waitUntil)
            {
                Debug.LogError("[BOOTH DEMO] 3-2-1-START を待ちきれませんでした。");
                EndSessionIfActive();
                _routine = null;
                Finish(false, "カウントダウンが完了しませんでした");
                yield break;
            }
            if (Time.time >= nextMetricAt)
            {
                nextMetricAt = Time.time + metricIntervalSeconds;
                SendMetrics(0f, paceKmH);
            }
            yield return null;
        }

        float startTime = Time.time;
        CurrentBeat = BoothDemoScript.Beat.OnPace;
        SwiftMessageSender.SendBoothDemoProgress(ModeName(_mode), CurrentBeat.ToString(), 0f, BoothDemoScript.TotalSeconds);
        Debug.Log($"[BOOTH DEMO] 立ったまま体験を開始 — {paceKmH:F1}km/h, ゴール {routeMeters:F0}m, {BoothDemoScript.TotalSeconds:F0}秒");

        nextMetricAt = 0f;
        while (true)
        {
            float t = Time.time - startTime;
            ElapsedSeconds = t;

            BoothDemoScript.Beat beat = BoothDemoScript.BeatAt(t);
            if (beat == BoothDemoScript.Beat.Finished) break;
            if (beat != CurrentBeat)
            {
                CurrentBeat = beat;
                Debug.Log($"[BOOTH DEMO] t={t:F1}s → {beat}");
                SwiftMessageSender.SendBoothDemoProgress(ModeName(_mode), beat.ToString(), t, BoothDemoScript.TotalSeconds);
            }

            avatarEngine.SetPresentationLeadOffset(BoothDemoScript.LeadOffsetAt(t));

            if (Time.time >= nextMetricAt)
            {
                nextMetricAt = Time.time + metricIntervalSeconds;
                SendMetrics(BoothDemoScript.DistanceAt(t, speed), paceKmH * BoothDemoScript.PaceRatioAt(t));
            }
            yield return null;
        }

        // ゴール: 正確な最終距離を1回送ればブリッジが目標到達を検知して終了する(PovRunnerDemoと同じ)
        avatarEngine.SetPresentationLeadOffset(0f);
        ElapsedSeconds = BoothDemoScript.TotalSeconds;
        CurrentBeat = BoothDemoScript.Beat.Finished;
        SendMetrics(routeMeters, paceKmH);
        if (!avatarEngine.IsSessionEnded)
        {
            // 届かなかった場合も体験は締める
            Debug.LogWarning("[BOOTH DEMO] 目標到達で終了しなかったため EndSession を送ります。");
            EndSessionIfActive();
        }

        _routine = null;
        Finish(true, "");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 数歩あるく: 来場者の移動そのものでアバターを動かす
    // ════════════════════════════════════════════════════════════════════════
    private IEnumerator RunWalking(float paceKmH)
    {
        // 屋内ではGPSが届かない。Swiftの実測もブリッジが無視するが、念のため判定自体を止める
        OverrideGpsHandling(false);
        StartSession(paceKmH, 0f); // ゴールなし(時間で終了)

        float waitUntil = Time.time + startTimeoutSeconds;
        while (!avatarEngine.IsRunMotionActive)
        {
            if (Time.time >= waitUntil)
            {
                EndSessionIfActive();
                _routine = null;
                Finish(false, "カウントダウンが完了しませんでした");
                yield break;
            }
            yield return null;
        }

        float startTime = Time.time;
        SwiftMessageSender.SendBoothDemoProgress(ModeName(_mode), "Walking", 0f, walkingDurationSeconds);
        Debug.Log($"[BOOTH DEMO] 歩行体験を開始 — アバター {paceKmH:F1}km/h, {walkingDurationSeconds:F0}秒");

        while ((ElapsedSeconds = Time.time - startTime) < walkingDurationSeconds)
            yield return null;

        EndSessionIfActive();
        _routine = null;
        Finish(true, "");
    }

    // ── 共通 ────────────────────────────────────────────────────────────────

    private void StartSession(float paceKmH, float goalKm)
    {
        LastRunCompleted = false;
        ElapsedSeconds = -1f;
        CurrentBeat = BoothDemoScript.Beat.OnPace;

        // 前の状態が通常追従以外(スタンバイ=アバター非表示 等)でも、
        // 新しい走行はアバターが見える通常状態から始める
        if (stateController != null && stateController.currentState != GameStateController.ARVisionState.Normal)
            stateController.TransitionToState(GameStateController.ARVisionState.Normal);

        // 閉じ括弧は String.Format の外に置く(PovRunnerDemoController と同じ理由)
        string json = string.Format(CultureInfo.InvariantCulture,
            "{{\"command\":\"StartSession\",\"targetPaceKmH\":{0:F3},\"distanceKm\":{1:F6}," +
            "\"forwardOffsetM\":{2:F2},\"hideUnityHud\":false",
            paceKmH, goalKm, leadDistanceMeters) + "}";
        bridge.OnPresentationCommand(json);

        // StartSession は前の走行をリセットするので、正面追従はその後に立てる
        avatarEngine.SetPresentationFollowsView(true);

        if (hudManager != null && _mode == Mode.Standing)
            hudManager.PresentationDistanceMeters = 0f;
    }

    private void SendMetrics(float distanceMeters, float paceKmH)
    {
        if (hudManager != null)
            hudManager.PresentationDistanceMeters = distanceMeters;

        // 合成座標は真北へ一直線(CSVは書かないので表示にもログにも出ない)
        double latitude = originLatitude + distanceMeters / MetersPerLatitudeDegree;
        string json = string.Format(CultureInfo.InvariantCulture,
            "{{\"command\":\"UpdateMetrics\",\"paceKmH\":{0:F3},\"heartRate\":0," +
            "\"distanceKm\":{1:F6},\"gpsLatitude\":{2:F7},\"gpsLongitude\":{3:F7}," +
            "\"gpsAccuracy\":{4:F2},\"locationSampleFresh\":{5},\"speedSampleValid\":{5}",
            paceKmH, distanceMeters / 1000.0, latitude, originLongitude,
            goodAccuracyMeters, "true") + "}";
        bridge.OnPresentationCommand(json);
    }

    private void EndSessionIfActive()
    {
        if (bridge != null && avatarEngine != null && avatarEngine.HasStarted && !avatarEngine.IsSessionEnded)
            bridge.OnPresentationCommand("{\"command\":\"EndSession\"}");
    }

    private void Finish(bool completed, string reason)
    {
        LastRunCompleted = completed;
        if (avatarEngine != null)
        {
            avatarEngine.SetPresentationLeadOffset(0f);
            avatarEngine.SetPresentationFollowsView(false);
        }

        // 通常追従以外で終わっていても、次の来場者を待つ間はアバターを見える状態に戻す
        if (stateController != null && stateController.currentState != GameStateController.ARVisionState.Normal)
            stateController.TransitionToState(GameStateController.ARVisionState.Normal);

        RestoreGpsHandling();
        Debug.Log($"[BOOTH DEMO] 終了 — {ModeName(_mode)} completed={completed} {reason}");
        SwiftMessageSender.SendBoothDemoEnded(ModeName(_mode), completed, reason);
    }

    private void OverrideGpsHandling(bool enabled)
    {
        if (gpsMonitor == null) return;
        if (!_gpsHandlingSaved)
        {
            _savedGpsHandling = gpsMonitor.AutoLostHandlingEnabled;
            _gpsHandlingSaved = true;
        }
        gpsMonitor.AutoLostHandlingEnabled = enabled;
    }

    private void RestoreGpsHandling()
    {
        if (gpsMonitor == null || !_gpsHandlingSaved) return;
        gpsMonitor.AutoLostHandlingEnabled = _savedGpsHandling;
        _gpsHandlingSaved = false;
    }

    private void ResolveReferences()
    {
        if (bridge == null) bridge = FindFirstObjectByType<ARSessionManagerBridge>(FindObjectsInactive.Include);
        if (avatarEngine == null) avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (gpsMonitor == null) gpsMonitor = FindFirstObjectByType<GpsSignalMonitor>(FindObjectsInactive.Include);
        if (hudManager == null) hudManager = FindFirstObjectByType<PeripheralHUDManager>(FindObjectsInactive.Include);
        if (stateController == null) stateController = FindFirstObjectByType<GameStateController>(FindObjectsInactive.Include);
    }

    private void OnDisable()
    {
        // 再コンパイル・破棄で黙って走行を残さない。設定(ロスト判定)だけは必ず戻す
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
        if (avatarEngine != null)
        {
            avatarEngine.SetPresentationLeadOffset(0f);
            avatarEngine.SetPresentationFollowsView(false);
        }
        RestoreGpsHandling();
    }
}
