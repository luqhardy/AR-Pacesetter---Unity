/// <summary>
/// ARKit/AR Foundation に依存しない環境面の意味分類。
/// 実機の分類が取れない屋外路面は Unknown のまま幾何判定へ流し、
/// Ceiling/Table/Seat など明確に床でないものだけを確実に除外する。
/// </summary>
public enum SurfaceSemantic
{
    Unknown = 0,
    Other = 1,
    Floor = 2,
    Ceiling = 3,
    Wall = 4,
    Table = 5,
    Seat = 6,
    Window = 7,
    Door = 8,
}

public static class SurfaceSemanticMath
{
    /// <summary>
    /// 床候補の優先度。Floor は Unknown/Other より優先し、家具・構造物は拒否する。
    /// Unknown を許可するのは、ARKit に「road」が無く屋外路面が未分類になり得るため。
    /// </summary>
    public static int GroundPriority(SurfaceSemantic semantic)
    {
        switch (semantic)
        {
            case SurfaceSemantic.Floor:
                return 2;
            case SurfaceSemantic.Unknown:
            case SurfaceSemantic.Other:
                return 1;
            default:
                return 0;
        }
    }

    public static bool CanBeGround(SurfaceSemantic semantic)
        => GroundPriority(semantic) > 0;

    /// <summary>進行空間を塞ぐ分類。Ceiling/Floor は水平形状判定へ委ねる。</summary>
    public static bool IsExplicitObstacle(SurfaceSemantic semantic)
    {
        switch (semantic)
        {
            case SurfaceSemantic.Wall:
            case SurfaceSemantic.Table:
            case SurfaceSemantic.Seat:
            case SurfaceSemantic.Window:
            case SurfaceSemantic.Door:
                return true;
            default:
                return false;
        }
    }
}
