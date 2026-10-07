using NUnit.Framework;

/// <summary>
/// 展示ブースの「立ったまま体験」台本(BoothDemoScript)の検証。
/// 台本の値は本番の判定ロジック(色・オーラ・HUDペース)に見せ場を
/// 起こさせるためのもの — その閾値と噛み合っていることをここで縛る。
/// アバターは体験中に一度も消さない(2026-10-07 チーム判断)。
/// </summary>
[TestFixture]
public class BoothDemoScriptTests
{
    // 本番の既定値(AvatarEngine / AvatarVisualsAndActions / AvatarAuraEffect)
    private const float TargetLead = 3.0f;
    private const float JustTolerance = 1.5f;
    private const float OverSpan = 1.5f;
    private const float BehindSpan = 3.0f;
    private const float AuraActivation = 5.0f;
    private const float AuraFull = 12.0f;
    private const float WaitForUserEnterMeters = 10.0f;
    private const float SpeedMps = 12f / 3.6f; // 12km/h

    [Test]
    public void Beats_FollowTheStoryOrder()
    {
        Assert.AreEqual(BoothDemoScript.Beat.OnPace, BoothDemoScript.BeatAt(0f));
        Assert.AreEqual(BoothDemoScript.Beat.FallingBehind, BoothDemoScript.BeatAt(BoothDemoScript.FallingBehindStart));
        Assert.AreEqual(BoothDemoScript.Beat.CatchingUp, BoothDemoScript.BeatAt(BoothDemoScript.CatchingUpStart));
        Assert.AreEqual(BoothDemoScript.Beat.Overtaking, BoothDemoScript.BeatAt(BoothDemoScript.OvertakingStart));
        Assert.AreEqual(BoothDemoScript.Beat.Settling, BoothDemoScript.BeatAt(BoothDemoScript.SettlingStart));
        Assert.AreEqual(BoothDemoScript.Beat.FinalStretch, BoothDemoScript.BeatAt(BoothDemoScript.FinalStretchStart));
        Assert.AreEqual(BoothDemoScript.Beat.Finished, BoothDemoScript.BeatAt(BoothDemoScript.TotalSeconds));
    }

    [Test]
    public void LeadOffset_IsContinuous_SoTheAvatarNeverJumps()
    {
        // 10msごとの変化量が小さいこと(段差があればアバターが瞬間移動して見える)
        const float step = 0.01f;
        float previous = BoothDemoScript.LeadOffsetAt(0f);
        for (float t = step; t <= BoothDemoScript.TotalSeconds + 1f; t += step)
        {
            float current = BoothDemoScript.LeadOffsetAt(t);
            Assert.Less(System.Math.Abs(current - previous), 0.05f, $"t={t:F2}s で {previous:F2}→{current:F2}m の段差");
            previous = current;
        }
    }

    [Test]
    public void LeadOffset_IsZero_OutsideTheShowcaseBeats()
    {
        Assert.AreEqual(0f, BoothDemoScript.LeadOffsetAt(5f), 1e-5f);
        Assert.AreEqual(0f, BoothDemoScript.LeadOffsetAt(45f), 1e-5f, "最後の直線はジャストで走る");
        Assert.AreEqual(0f, BoothDemoScript.LeadOffsetAt(60f), 1e-5f, "ゴール後もずらさない");
    }

    [Test]
    public void FallingBehind_Peak_TurnsTheAvatarRed_AndRaisesTheAura()
    {
        float lead = TargetLead + BoothDemoScript.LeadOffsetAt(18f);

        var state = AvatarPaceColor.Evaluate(lead, TargetLead, JustTolerance, OverSpan, BehindSpan, out float t);
        Assert.AreEqual(AvatarPaceColor.PaceState.Behind, state);
        Assert.AreEqual(1f, t, 1e-4f, "遅れの頂点では赤一色まで振る");

        Assert.IsTrue(AuraFeedback.TryEvaluate(lead - TargetLead, AuraActivation, AuraFull, out _),
            "§7.2 のオーラ(5m以上の遅れ)が出る");
    }

    [Test]
    public void FallingBehind_StaysShortOfTheWaitForUserDistance()
    {
        // 10m離れると「離隔待機(手招き)」に変わってしまう
        float maxLead = TargetLead + BoothDemoScript.BehindLeadOffsetMeters;
        Assert.Less(maxLead, WaitForUserEnterMeters - 0.5f);
    }

    [Test]
    public void Overtaking_ShiftsTheColourTowardBlue_ButKeepsTheAvatarInFront()
    {
        float lead = TargetLead + BoothDemoScript.LeadOffsetAt(32f);

        var state = AvatarPaceColor.Evaluate(lead, TargetLead, JustTolerance, OverSpan, BehindSpan, out _);
        Assert.AreEqual(AvatarPaceColor.PaceState.OverPace, state);
        Assert.Greater(lead, 0.8f, "アバターがランナーより後ろへ回るとグラスの視野から消える");
    }

    [Test]
    public void HudPace_IsRedOnlyWhileFallingBehind()
    {
        float targetPace = 60f / 12f;
        foreach (float t in new[] { 5f, 18f, 25f, 32f, 38f, 60f })
        {
            float currentPace = targetPace / BoothDemoScript.PaceRatioAt(t);
            var hud = PaceHudDisplay.Evaluate(currentPace, targetPace, PaceHudDisplay.DefaultBehindTolerance);
            var expected = BoothDemoScript.BeatAt(t) == BoothDemoScript.Beat.FallingBehind
                ? PaceHudDisplay.PaceState.Behind
                : PaceHudDisplay.PaceState.Maintaining;
            Assert.AreEqual(expected, hud, $"t={t}s ({BoothDemoScript.BeatAt(t)})");
        }
    }

    [Test]
    public void Script_HasNoBeatThatHidesTheAvatar()
    {
        // 来場者には「消えた=壊れた」に見える。GPSロスト(F-09/F-10)の区間を戻さないこと
        foreach (string name in System.Enum.GetNames(typeof(BoothDemoScript.Beat)))
            StringAssert.DoesNotContain("Gps", name);
    }

    [Test]
    public void LeadOffset_StaysInsideTheVisibleRange_ForTheWholeScript()
    {
        // 0.8m より近いとグラスの視野から外れ、10m で離隔待機(手招き)に変わる
        for (float t = 0f; t <= BoothDemoScript.TotalSeconds + 1f; t += 0.05f)
        {
            float lead = TargetLead + BoothDemoScript.LeadOffsetAt(t);
            Assert.GreaterOrEqual(lead, 0.8f, $"t={t:F2}s");
            Assert.Less(lead, WaitForUserEnterMeters - 0.5f, $"t={t:F2}s");
        }
    }

    [Test]
    public void Distance_IsMonotonic_AndMatchesThePiecewisePace()
    {
        float previous = 0f;
        for (float t = 0f; t <= BoothDemoScript.TotalSeconds + 5f; t += 0.25f)
        {
            float d = BoothDemoScript.DistanceAt(t, SpeedMps);
            Assert.GreaterOrEqual(d, previous - 1e-4f, $"t={t}s で距離が減った");
            previous = d;
        }

        Assert.AreEqual(10f * SpeedMps, BoothDemoScript.DistanceAt(10f, SpeedMps), 1e-3f);
        Assert.AreEqual((10f + 2f * BoothDemoScript.BehindPaceRatio) * SpeedMps,
            BoothDemoScript.DistanceAt(12f, SpeedMps), 1e-3f);
        Assert.AreEqual(BoothDemoScript.RouteDistanceMeters(SpeedMps),
            BoothDemoScript.DistanceAt(BoothDemoScript.TotalSeconds + 30f, SpeedMps), 1e-4f, "台本の後は伸びない");
    }

    [Test]
    public void FinalStretch_IsLongEnoughToSeeTheGoalLine()
    {
        const double revealMeters = 25.0; // GoalLineController の既定
        double route = BoothDemoScript.RouteDistanceMeters(SpeedMps);
        double atFinalStretch = BoothDemoScript.DistanceAt(BoothDemoScript.FinalStretchStart, SpeedMps);
        Assert.IsFalse(GoalLineMath.ShouldShow(route, atFinalStretch, revealMeters),
            "最後の直線に入る前からゴールが出ていると見せ場が重なる");
        Assert.IsTrue(GoalLineMath.ShouldShow(route, BoothDemoScript.DistanceAt(BoothDemoScript.TotalSeconds - 2f, SpeedMps), revealMeters));
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    public void Distance_IsZero_ForNonPositiveSpeed(float speed)
    {
        Assert.AreEqual(0f, BoothDemoScript.DistanceAt(30f, speed));
    }
}
