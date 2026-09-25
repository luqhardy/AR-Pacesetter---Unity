using NUnit.Framework;

[TestFixture]
public class SurfaceSemanticMathTests
{
    [TestCase(SurfaceSemantic.Floor, 2)]
    [TestCase(SurfaceSemantic.Unknown, 1)]
    [TestCase(SurfaceSemantic.Other, 1)]
    [TestCase(SurfaceSemantic.Ceiling, 0)]
    [TestCase(SurfaceSemantic.Wall, 0)]
    [TestCase(SurfaceSemantic.Table, 0)]
    [TestCase(SurfaceSemantic.Seat, 0)]
    public void GroundPriorityRejectsKnownNonGroundSurfaces(SurfaceSemantic semantic, int expected)
    {
        Assert.AreEqual(expected, SurfaceSemanticMath.GroundPriority(semantic));
    }

    [Test]
    public void UnknownSurfaceRemainsEligibleForOutdoorRoads()
    {
        Assert.IsTrue(SurfaceSemanticMath.CanBeGround(SurfaceSemantic.Unknown));
    }

    [TestCase(SurfaceSemantic.Wall)]
    [TestCase(SurfaceSemantic.Table)]
    [TestCase(SurfaceSemantic.Seat)]
    [TestCase(SurfaceSemantic.Window)]
    [TestCase(SurfaceSemantic.Door)]
    public void StructuralAndFurnitureClassesAreExplicitObstacles(SurfaceSemantic semantic)
    {
        Assert.IsTrue(SurfaceSemanticMath.IsExplicitObstacle(semantic));
    }

    [TestCase(SurfaceSemantic.Floor)]
    [TestCase(SurfaceSemantic.Ceiling)]
    [TestCase(SurfaceSemantic.Unknown)]
    public void HorizontalOrUnknownClassesNeedGeometryBeforeBecomingObstacles(SurfaceSemantic semantic)
    {
        Assert.IsFalse(SurfaceSemanticMath.IsExplicitObstacle(semantic));
    }
}
