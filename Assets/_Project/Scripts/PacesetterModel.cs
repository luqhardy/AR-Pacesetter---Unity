using System;

/// <summary>ペースセッターの状態(Docs/PACESETTER_BEHAVIOR_SPEC.md §5)</summary>
public enum PacesetterState { Start, Run, Wait, Overtaken }

/// <summary>
/// ペースセッターの挙動(Docs/PACESETTER_BEHAVIOR_SPEC.md v0.6)の純ロジック。自走モードの本体。
/// トラックに沿った距離(m)だけで考え、世界座標には触らない — AvatarEngine が <see cref="Gap"/> を前方距離にする。
///
/// <para>アバターは <c>set_speed</c> で自分で走る(<see cref="AvatarPos"/>)。<see cref="TargetLine"/> は別物で、
/// 待っている間も進み続ける。走り出したアバターは <see cref="TargetLine"/> へ追いつかない(R-12)。</para>
///
/// <para>値に「仮」と書いたものは仕様の UNDECIDED(§9)。実機で決まったらここを直す。</para>
/// </summary>
public sealed class PacesetterModel
{
    /// <summary>C-01 初期ギャップ</summary>
    public const double InitialGapMeters = 3.0;
    /// <summary>C-02 待ち開始距離</summary>
    public const double WaitEnterMeters = 10.0;
    /// <summary>C-03 走り出し距離</summary>
    public const double WaitExitMeters = 3.0;
    /// <summary>U-01(仮): 走り出しの歩数(仕様の仮 3〜5歩の中央)</summary>
    public const double AccelSteps = 4.0;
    /// <summary>U-02(仮): 補正をなじませる秒数(仕様の仮 2〜3秒の中央)</summary>
    public const double CorrectionSeconds = 2.5;
    /// <summary>R-21(仮): これより小さい差は補正しない — GPS の誤差を拾って毎周ずらさない</summary>
    public const double MinCorrectionMeters = 3.0;
    /// <summary>T-01(仮): これだけ進んだら「走り出した」とする。手ブレや GPS の揺れでは始めない</summary>
    public const double StartMoveMeters = 0.5;

    /// <summary>壁時計の空きがこれ以上あれば、数えられなかった時間とみなして足す(フレームの揺れは足さない)</summary>
    public const double MissedThresholdSeconds = 1.0;

    public PacesetterState State { get; private set; } = PacesetterState.Start;
    /// <summary>user_pos(チェックポイントの補正を含む)</summary>
    public double UserPos { get; private set; }
    /// <summary>avatar_pos</summary>
    public double AvatarPos { get; private set; }
    /// <summary>target_line。T-01 から進む</summary>
    public double TargetLine { get; private set; }
    /// <summary>今のアバターの速さ。加速中は <c>set_speed</c> より遅く、WAIT では 0</summary>
    public double CurrentSpeedMetersPerSecond { get; private set; }

    /// <summary>gap = avatar_pos − user_pos。正 = アバターが前</summary>
    public double Gap => AvatarPos - UserPos;
    /// <summary>lag = target_line − user_pos</summary>
    public double LagMeters => TargetLine - UserPos;
    /// <summary>lag ÷ set_speed。ゴール時の「◯秒遅れ」の元(R-13)。表示形式は U-06 で未決</summary>
    public double LagSeconds => _setSpeed > 0.0 ? LagMeters / _setSpeed : 0.0;

    private double _setSpeed;
    private double _startUserPos;
    private double _correctionMeters;     // user_pos に足している補正の累計
    private double _pendingCorrection;    // まだ足していない補正
    private double _correctionRate;       // m/s
    private double _accelElapsed;
    private bool _accelerating;

    public void Begin(double setSpeed, double userPos, double initialGap)
    {
        _setSpeed = IsUsable(setSpeed) && setSpeed > 0.0 ? setSpeed : 0.0;
        _startUserPos = IsUsable(userPos) ? userPos : 0.0;
        double gap = IsUsable(initialGap) ? initialGap : InitialGapMeters;

        State = PacesetterState.Start;
        UserPos = _startUserPos;
        AvatarPos = _startUserPos + gap;
        TargetLine = AvatarPos;
        CurrentSpeedMetersPerSecond = 0.0;
        _correctionMeters = 0.0;
        _pendingCorrection = 0.0;
        _correctionRate = 0.0;
        _accelElapsed = 0.0;
        _accelerating = false;
    }

    /// <summary>
    /// 毎フレーム呼ぶ。<paramref name="seconds"/> は長くてもよい(スタンバイで数えられなかった時間)。
    /// </summary>
    /// <param name="seconds">経過秒</param>
    /// <param name="measuredUserPos">補正前の user_pos</param>
    /// <param name="setSpeed">set_speed。ゴーストでは走行中に変わる</param>
    public void Tick(double seconds, double measuredUserPos, double setSpeed)
    {
        if (!IsUsable(seconds) || seconds <= 0.0 || !IsUsable(measuredUserPos)) return;
        if (IsUsable(setSpeed) && setSpeed > 0.0) _setSpeed = setSpeed;

        AdvanceCorrection(seconds);
        UserPos = measuredUserPos + _correctionMeters;

        if (State == PacesetterState.Start)
        {
            if (UserPos - _startUserPos < StartMoveMeters) return;
            EnterRun(); // T-01
        }

        TargetLine += _setSpeed * seconds;

        if (State != PacesetterState.Wait)
        {
            UpdateSpeed(seconds);
            AvatarPos += CurrentSpeedMetersPerSecond * seconds;
            // 長い時間を1回で進めても、10m で止まったはずの位置を越えない
            if (State == PacesetterState.Run) AvatarPos = Math.Min(AvatarPos, UserPos + WaitEnterMeters);
        }

        ApplyTransitions();
    }

    /// <summary>
    /// R-20: チェックポイントで分かった差(真の user_pos − 今の user_pos)を、数秒かけて足す(R-24)。
    /// </summary>
    /// <returns>補正を受けたか(R-21: 細かい差は受けない)</returns>
    public bool ApplyCheckpointCorrection(double deltaMeters)
    {
        if (!IsUsable(deltaMeters) || Math.Abs(deltaMeters) < MinCorrectionMeters) return false;
        _pendingCorrection += deltaMeters;
        _correctionRate = _pendingCorrection / CorrectionSeconds;
        return true;
    }

    /// <summary>
    /// 前のフレームからの壁時計の空き(秒)のうち、このフレームの経過時間で数えられなかった分(ゲーム内の秒)。
    /// スタンバイでアバターが非アクティブだった・アプリが止まっていた時間。普段のフレームでは 0
    /// </summary>
    /// <param name="realGapSeconds">前に数えたときからの壁時計の経過秒</param>
    /// <param name="frameDeltaSeconds">このフレームの Time.deltaTime</param>
    /// <param name="timeScale">Time.timeScale(実機は1。E2E は時間を早回しする)</param>
    public static double MissedSeconds(double realGapSeconds, double frameDeltaSeconds, double timeScale)
    {
        if (double.IsNaN(realGapSeconds) || realGapSeconds <= 0.0) return 0.0;
        double scale = double.IsNaN(timeScale) || timeScale <= 0.0 ? 1.0 : timeScale;
        double delta = double.IsNaN(frameDeltaSeconds) || frameDeltaSeconds < 0.0 ? 0.0 : frameDeltaSeconds;
        double missed = realGapSeconds * scale - delta;
        return missed > MissedThresholdSeconds ? missed : 0.0;
    }

    /// <summary>
    /// 走者の横を通る(§6「足音は横を通り過ぎて前へ抜ける」)ための横のずれ(m)。
    /// 1m 以内で最大、3m で 0。同じ線の上を通ると体を突き抜け、音も左右へ動かない
    /// </summary>
    public static double PassingLateralMeters(double gap, double maxLateralMeters)
    {
        double distance = Math.Abs(gap);
        if (double.IsNaN(distance)) return 0.0;
        double t = (WaitExitMeters - distance) / (WaitExitMeters - 1.0);
        return maxLateralMeters * Math.Max(0.0, Math.Min(1.0, t));
    }

    private void AdvanceCorrection(double seconds)
    {
        if (_pendingCorrection == 0.0) return;
        double step = _correctionRate * seconds;
        if (Math.Abs(step) >= Math.Abs(_pendingCorrection)) step = _pendingCorrection;
        _correctionMeters += step;
        _pendingCorrection -= step;
    }

    private void UpdateSpeed(double seconds)
    {
        if (!_accelerating)
        {
            CurrentSpeedMetersPerSecond = _setSpeed;
            return;
        }

        _accelElapsed += seconds;
        double accelSeconds = AccelSteps / PacesetterSound.StepsPerSecond((float)_setSpeed);
        if (_accelElapsed >= accelSeconds)
        {
            _accelerating = false;
            CurrentSpeedMetersPerSecond = _setSpeed;
            return;
        }
        CurrentSpeedMetersPerSecond = _setSpeed * (_accelElapsed / accelSeconds);
    }

    private void ApplyTransitions()
    {
        switch (State)
        {
            case PacesetterState.Run:
                if (Gap < 0.0)
                {
                    State = PacesetterState.Overtaken; // T-04(加速中も同じ)
                }
                else if (Gap >= WaitEnterMeters)
                {
                    State = PacesetterState.Wait;      // T-02
                    CurrentSpeedMetersPerSecond = 0.0;
                    _accelerating = false;
                }
                break;

            case PacesetterState.Wait:
                if (Gap <= WaitExitMeters) EnterRun(); // T-03
                break;

            case PacesetterState.Overtaken:
                if (Gap >= 0.0) State = PacesetterState.Run; // T-05
                break;
        }
    }

    private void EnterRun()
    {
        State = PacesetterState.Run;
        CurrentSpeedMetersPerSecond = 0.0;
        _accelerating = true;
        _accelElapsed = 0.0;
    }

    private static bool IsUsable(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>
/// user_pos の元になる距離(m)。正規の距離(<c>AuthoritativeDistanceMeters</c>)は実機では GPS の1秒ごとの段差で、
/// そのまま gap にすると 3.6m/s で約3.6mの鋸歯になり、アバターが前後に脈打つ。
/// フレームごとの移動で連続に進め、正規の距離が届いたらその差だけを滑らかに足す — 正規の距離は単一ソースのまま。
/// </summary>
public sealed class RunnerDistanceEstimator
{
    /// <summary>正規の距離との差を埋める時定数(秒)</summary>
    public const double BlendSeconds = 1.5;
    /// <summary>これより速い移動は人間ではない(AR の再ローカライズ等)</summary>
    public const double MaxSpeedMetersPerSecond = 15.0;

    public double Position { get; private set; }

    private double _lastAuthoritative;
    private double _pending;

    public void Reset(double authoritativeMeters)
    {
        Position = double.IsNaN(authoritativeMeters) || double.IsInfinity(authoritativeMeters) ? 0.0 : authoritativeMeters;
        _lastAuthoritative = Position;
        _pending = 0.0;
    }

    /// <param name="seconds">経過秒</param>
    /// <param name="frameTravelMeters">このフレームに進行方向へ進んだ距離(m)</param>
    /// <param name="authoritativeMeters">正規の距離(m)</param>
    public double Update(double seconds, double frameTravelMeters, double authoritativeMeters)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0.0) return Position;

        if (!double.IsNaN(frameTravelMeters) && !double.IsInfinity(frameTravelMeters)
            && Math.Abs(frameTravelMeters) <= MaxSpeedMetersPerSecond * seconds)
        {
            Position += frameTravelMeters;
        }

        if (!double.IsNaN(authoritativeMeters) && !double.IsInfinity(authoritativeMeters)
            && authoritativeMeters != _lastAuthoritative)
        {
            _lastAuthoritative = authoritativeMeters;
            _pending = authoritativeMeters - Position;
        }

        double step = _pending * (1.0 - Math.Exp(-seconds / BlendSeconds));
        Position += step;
        _pending -= step;
        return Position;
    }
}
