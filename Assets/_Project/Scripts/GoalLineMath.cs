using System;

/// <summary>
/// ARゴールラインの表示判断に使うUnity非依存の純ロジック。
/// 距離の入力単位はRunSessionControllerと同じメートルに統一する。
/// </summary>
public static class GoalLineMath
{
    public static double RemainingMeters(double targetDistanceMeters, double currentDistanceMeters)
    {
        if (targetDistanceMeters <= 0)
            return double.PositiveInfinity;

        return Math.Max(0.0, targetDistanceMeters - Math.Max(0.0, currentDistanceMeters));
    }

    public static bool ShouldShow(double targetDistanceMeters, double currentDistanceMeters,
                                  double revealDistanceMeters)
    {
        if (targetDistanceMeters <= 0 || revealDistanceMeters <= 0)
            return false;

        return RemainingMeters(targetDistanceMeters, currentDistanceMeters) <= revealDistanceMeters;
    }

    public static bool ShouldLock(double targetDistanceMeters, double currentDistanceMeters,
                                  double lockDistanceMeters)
    {
        if (targetDistanceMeters <= 0 || lockDistanceMeters < 0)
            return false;

        return RemainingMeters(targetDistanceMeters, currentDistanceMeters) <= lockDistanceMeters;
    }
}
