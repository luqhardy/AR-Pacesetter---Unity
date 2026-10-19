using NUnit.Framework;

[TestFixture]
public class TargetSpeedMotionTests
{
    [TestCase(1d)]
    [TestCase(4d)]
    [TestCase(5d)]
    [TestCase(20d)]
    [TestCase(30d)]
    public void SelectedSpeedIsNotSilentlyClamped(double kmh)
    {
        Assert.IsTrue(PaceMath.TrySpeedToPace(kmh, out float pace));
        Assert.AreEqual(kmh / 3.6, PaceSynchronicityMath.PaceToMetersPerSecond(pace), 0.0001);
    }

    [TestCase(0d)]
    [TestCase(31d)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidSpeedIsRejected(double kmh)
        => Assert.IsFalse(PaceMath.TrySpeedToPace(kmh, out _));

    [Test]
    public void WaitingUsesDistanceHysteresisAndAllowsPassing()
    {
        Assert.IsTrue(PaceMath.ShouldWaitForRunner(false, 10f, 10f));
        Assert.IsTrue(PaceMath.ShouldWaitForRunner(true, 9f, 9f));
        Assert.IsTrue(PaceMath.ShouldWaitForRunner(true, 10f, 6f)); // sideways does not count as catching up
        Assert.IsFalse(PaceMath.ShouldWaitForRunner(true, 7f, 7f));
        Assert.IsFalse(PaceMath.ShouldWaitForRunner(false, 8f, 8f));
        Assert.IsFalse(PaceMath.ShouldWaitForRunner(false, 15f, -15f));
        Assert.IsFalse(PaceMath.ShouldWaitForRunner(true, 10f, -1f));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(120)]
    public void FiveMinutePaceTravelsTwoHundredMetersInOneMinute(int fps)
    {
        float distance = 0f;
        float speed = PaceSynchronicityMath.PaceToMetersPerSecond(5f);
        for (int frame = 0; frame < 60 * fps; frame++)
            distance += PaceMath.TravelMeters(speed, 1f / fps);
        Assert.AreEqual(200f, distance, 0.03f);
    }

    [Test]
    public void SpeedChangePreservesAccumulatedPosition()
    {
        float distance = PaceMath.TravelMeters(3f, 10f);
        distance += PaceMath.TravelMeters(2f, 10f);
        Assert.AreEqual(50f, distance);
    }

    [Test]
    public void RelativeGapCrossesZeroInBothDirections()
    {
        float gap = 3f;
        gap += PaceMath.TravelMeters(3f, 4f) - PaceMath.TravelMeters(4f, 4f);
        Assert.Less(gap, 0f);
        gap += PaceMath.TravelMeters(3f, 2f) - PaceMath.TravelMeters(2f, 2f);
        Assert.Greater(gap, 0f);
    }

    [TestCase(float.NaN, 1f)]
    [TestCase(float.PositiveInfinity, 1f)]
    [TestCase(-1f, 1f)]
    [TestCase(3f, float.NaN)]
    [TestCase(3f, float.PositiveInfinity)]
    [TestCase(3f, -1f)]
    [TestCase(3f, 0f)]
    public void InvalidInputDoesNotMove(float speed, float dt)
    {
        Assert.AreEqual(0f, PaceMath.TravelMeters(speed, dt));
    }
}
