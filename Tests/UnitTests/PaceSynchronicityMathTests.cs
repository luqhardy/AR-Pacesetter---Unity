using NUnit.Framework;

public class PaceSynchronicityMathTests
{
    [Test]
    public void MatchingPaces_WithNoDistanceGap_AreFullySynchronized()
    {
        float sync = PaceSynchronicityMath.CalculateSyncPercentFromPaces(5f, 5f, 0f);

        Assert.AreEqual(100f, sync, 0.001f);
    }

    [TestCase(5f, 6f, 83.333f)]
    [TestCase(5f, 4f, 80f)]
    public void DifferentPaces_UseSpeedRatioRatherThanAvatarSeparation(
        float targetPace, float actualPace, float expected)
    {
        float sync = PaceSynchronicityMath.CalculateSyncPercentFromPaces(
            targetPace, actualPace, 0f);

        Assert.AreEqual(expected, sync, 0.01f);
    }

    [TestCase(10f)]
    [TestCase(-10f)]
    [TestCase(25f)]
    public void TenMeterOrGreaterAccumulatedGap_IsZero(float distanceGap)
    {
        float sync = PaceSynchronicityMath.CalculateSyncPercentFromPaces(
            5f, 5f, distanceGap);

        Assert.AreEqual(0f, sync, 0.001f);
    }

    [Test]
    public void DistanceGapPenalizesEvenWhenInstantaneousPaceHasRecovered()
    {
        float sync = PaceSynchronicityMath.CalculateSyncPercentFromPaces(
            5f, 5f, 5f);

        Assert.AreEqual(50f, sync, 0.001f);
    }

    [Test]
    public void AccumulatedGap_IsSignedAndCanRecover()
    {
        float targetSpeed = PaceSynchronicityMath.PaceToMetersPerSecond(5f);
        float slowerSpeed = targetSpeed - 0.5f;
        float fasterSpeed = targetSpeed + 0.5f;

        float gap = PaceSynchronicityMath.AccumulateDistanceDeviation(
            0f, targetSpeed, slowerSpeed, 20f);
        Assert.AreEqual(-10f, gap, 0.001f);

        gap = PaceSynchronicityMath.AccumulateDistanceDeviation(
            gap, targetSpeed, fasterSpeed, 20f);
        Assert.AreEqual(0f, gap, 0.001f);
    }

    [Test]
    public void StoppedRunner_IsZeroSync()
    {
        float targetSpeed = PaceSynchronicityMath.PaceToMetersPerSecond(5f);

        Assert.AreEqual(
            0f,
            PaceSynchronicityMath.CalculateSyncPercent(targetSpeed, 0f, 0f),
            0.001f);
    }

    [Test]
    public void InvalidSamples_DoNotPoisonAccumulatorOrScore()
    {
        float gap = PaceSynchronicityMath.AccumulateDistanceDeviation(
            3f, 3f, float.NaN, 1f);

        Assert.AreEqual(3f, gap, 0.001f);
        Assert.AreEqual(
            0f,
            PaceSynchronicityMath.CalculateSyncPercent(3f, float.PositiveInfinity, gap),
            0.001f);
    }
}
