using NUnit.Framework;

public class GoalLineMathTests
{
    [TestCase(1000.0, 975.0, 25.0)]
    [TestCase(1000.0, 1005.0, 0.0)]
    [TestCase(1000.0, -10.0, 1000.0)]
    public void RemainingMeters_IsClampedToValidCourseRange(
        double target, double current, double expected)
    {
        Assert.AreEqual(expected, GoalLineMath.RemainingMeters(target, current), 0.001);
    }

    [Test]
    public void ShouldShow_IncludesRevealBoundary()
    {
        Assert.IsTrue(GoalLineMath.ShouldShow(1000.0, 975.0, 25.0));
        Assert.IsFalse(GoalLineMath.ShouldShow(1000.0, 974.9, 25.0));
    }

    [Test]
    public void ShouldLock_IncludesLockBoundary()
    {
        Assert.IsTrue(GoalLineMath.ShouldLock(1000.0, 992.0, 8.0));
        Assert.IsFalse(GoalLineMath.ShouldLock(1000.0, 991.9, 8.0));
    }

    [Test]
    public void InvalidGoal_NeverShowsOrLocks()
    {
        Assert.IsFalse(GoalLineMath.ShouldShow(0.0, 0.0, 25.0));
        Assert.IsFalse(GoalLineMath.ShouldLock(0.0, 0.0, 8.0));
    }
}
