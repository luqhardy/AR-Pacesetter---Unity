using NUnit.Framework;

public class RunnerTrackingMathTests
{
    [Test]
    public void NorthwardCoordinateChangeProducesNorthOffsetAndZeroBearing()
    {
        bool valid = RunnerTrackingMath.TryLocalOffsetMeters(
            35.0, 139.0, 35.00001, 139.0,
            out double east, out double north);

        Assert.IsTrue(valid);
        Assert.That(east, Is.EqualTo(0.0).Within(0.01));
        Assert.That(north, Is.GreaterThan(1.0));
        Assert.That(RunnerTrackingMath.BearingDegrees(east, north),
            Is.EqualTo(0.0).Within(0.01));
    }

    [Test]
    public void EastwardCoordinateChangeProducesNinetyDegreeBearing()
    {
        RunnerTrackingMath.TryLocalOffsetMeters(
            35.0, 139.0, 35.0, 139.00001,
            out double east, out double north);

        Assert.That(east, Is.GreaterThan(0.8));
        Assert.That(north, Is.EqualTo(0.0).Within(0.01));
        Assert.That(RunnerTrackingMath.BearingDegrees(east, north),
            Is.EqualTo(90.0).Within(0.01));
    }

    [Test]
    public void DateLineCrossingUsesShortOffset()
    {
        RunnerTrackingMath.TryLocalOffsetMeters(
            0.0, 179.99999, 0.0, -179.99999,
            out double east, out double north);

        Assert.That(RunnerTrackingMath.DistanceMeters(east, north),
            Is.LessThan(3.0));
        Assert.That(RunnerTrackingMath.BearingDegrees(east, north),
            Is.EqualTo(90.0).Within(0.01));
    }

    [TestCase(91.0, 0.0)]
    [TestCase(0.0, 181.0)]
    [TestCase(double.NaN, 0.0)]
    public void InvalidCoordinateIsRejected(double latitude, double longitude)
    {
        Assert.IsFalse(RunnerTrackingMath.IsValidCoordinate(latitude, longitude));
    }
}
