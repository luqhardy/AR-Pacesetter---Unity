using UnityEngine;

/// <summary>
/// 非機能要件 (基本設計書 §10) のうち、走行中にしか測れないものを実測する。
///
/// <list type="bullet">
/// <item><b>位置誤差 1.0m以内</b> — 目標リード距離(3.0m)と実際の水平距離の差。</item>
/// <item><b>60分連続稼働でバッテリー30%以上</b> — 消費率を測り60分後へ外挿する。</item>
/// </list>
///
/// <para>M2P(§10)は <see cref="SensorTimingBridge"/> が担当する。ここはその姉妹で、
/// 「要求が満たせているか」を走行のたびにログへ1行残すことを目的にする —
/// 実証(§11.2)でCSVを解析するまで分からない、という状態を避けるため。</para>
///
/// <para><b>正常追従中だけ</b>を測る。障害物停止・離隔待機・サイレント復帰・
/// 追い抜かれ中は、アバターが意図的に3.0m前方から外れるため、
/// これらを混ぜると「位置精度」ではなく「演出の量」を測ることになる。</para>
/// </summary>
public class NonFunctionalRequirementsMonitor : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private Transform userCamera;

    [Tooltip("バッテリー残量のサンプル間隔(秒)。頻繁に読んでも意味が無い")]
    [SerializeField] private float batterySampleIntervalSeconds = 10f;

    /// <summary>位置誤差(リード距離の誤差)の集計。</summary>
    public PositionAccuracyStats Position { get; } = new PositionAccuracyStats();

    /// <summary>バッテリーのサンプル数。0なら残量が取れない端末/エディタ。</summary>
    public int BatterySampleCount { get; private set; }

    /// <summary>直近の走行の計測時間(秒)。</summary>
    public float MeasuredSeconds { get; private set; }

    private bool _running;
    private float _nextBatterySampleTime;
    private float _startLevel01 = -1f;
    private float _lastLevel01 = -1f;
    private float _startTime;

    private void Awake()
    {
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (userCamera == null && Camera.main != null)
            userCamera = Camera.main.transform;
    }

    private void Update()
    {
        if (avatarEngine == null) return;

        bool shouldMeasure = avatarEngine.IsRunMotionActive;

        if (shouldMeasure && !_running) BeginMeasurement();
        else if (!shouldMeasure && _running) EndMeasurement();
        if (!_running) return;

        MeasuredSeconds = Time.time - _startTime;

        SamplePositionAccuracy();
        SampleBattery();
    }

    private void BeginMeasurement()
    {
        _running = true;
        _startTime = Time.time;
        MeasuredSeconds = 0f;
        Position.Reset();
        BatterySampleCount = 0;
        _startLevel01 = ReadBatteryLevel();
        _lastLevel01 = _startLevel01;
        _nextBatterySampleTime = Time.time + batterySampleIntervalSeconds;
        if (_startLevel01 >= 0f) BatterySampleCount = 1;
    }

    private void EndMeasurement()
    {
        _running = false;
        Debug.Log($"[§10] {Position.Summarize()}");
        Debug.Log($"[§10] {SummarizeEndurance()}");
    }

    /// <summary>
    /// 正常追従中のみ、目標リード距離との水平距離の差を1件加える。
    /// </summary>
    private void SamplePositionAccuracy()
    {
        if (userCamera == null) return;

        // 意図的に3.0mから外れる状態は除外する(これらは演出であって誤差ではない)
        if (avatarEngine.IsHalted || avatarEngine.IsWaitingForUser
            || avatarEngine.IsOverriddenByRecovery || avatarEngine.IsSessionEnded)
            return;

        Vector3 toAvatar = avatarEngine.transform.position - userCamera.position;
        toAvatar.y = 0f;

        float error = Mathf.Abs(toAvatar.magnitude - avatarEngine.LeadDistanceMeters);
        Position.Add(error);
    }

    private void SampleBattery()
    {
        if (Time.time < _nextBatterySampleTime) return;
        _nextBatterySampleTime = Time.time + batterySampleIntervalSeconds;

        float level = ReadBatteryLevel();
        if (level < 0f) return; // 残量が取れない環境

        if (_startLevel01 < 0f)
        {
            _startLevel01 = level;
            _startTime = Time.time; // 取れ始めた時点を起点にする
        }

        _lastLevel01 = level;
        BatterySampleCount++;
    }

    /// <summary>
    /// バッテリー残量(0〜1)。取得できない環境(エディタ・未対応端末)では -1。
    /// <c>SystemInfo.batteryLevel</c> は不明時に -1 を返す仕様。
    /// </summary>
    private static float ReadBatteryLevel()
    {
        float level = SystemInfo.batteryLevel;
        return (level < 0f || level > 1f) ? -1f : level;
    }

    /// <summary>
    /// §10 の連続稼働要求に対する現時点の見立て。測定時間が短ければ「判定不能」と言う。
    /// </summary>
    public string SummarizeEndurance()
    {
        if (BatterySampleCount == 0 || _startLevel01 < 0f)
            return "連続稼働: バッテリー残量を取得できない環境のため判定不能";

        if (!BatteryEnduranceMath.TryComputeDrainPerHour(
                _startLevel01, _lastLevel01, MeasuredSeconds, out float drainPerHour))
            return "連続稼働: 消費率を算出できず判定不能";

        if (!BatteryEnduranceMath.IsReliableSampleDuration(MeasuredSeconds))
            return $"連続稼働: 計測{MeasuredSeconds:F0}秒は外挿に短すぎるため判定不能 " +
                   $"(暫定 {drainPerHour * 100f:F1}%/時)";

        float after60 = BatteryEnduranceMath.ProjectLevelAfterMinutes(1.0f, drainPerHour, 60f);
        bool meets = BatteryEnduranceMath.MeetsEnduranceRequirement(drainPerHour);

        return $"連続稼働: {drainPerHour * 100f:F1}%/時 → 満充電から60分後 {after60 * 100f:F0}% " +
               $"({(meets ? "§10達成" : "§10未達: 30%を下回る")})";
    }
}
