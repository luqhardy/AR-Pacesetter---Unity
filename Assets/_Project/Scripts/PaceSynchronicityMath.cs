using System;

/// <summary>
/// Dependency-free pace synchronicity calculations.
///
/// Synchronicity compares the runner's measured pace with the configured target
/// pace. It deliberately does not use the world-space gap between the camera and
/// the pacer avatar because that avatar is designed to remain several metres in
/// front of the runner.
/// </summary>
public static class PaceSynchronicityMath
{
    public const float ZeroSyncDeviationMeters = 10f;

    /// <summary>Converts minutes per kilometre to metres per second.</summary>
    public static float PaceToMetersPerSecond(float minutesPerKilometer)
    {
        if (!IsFinite(minutesPerKilometer) || minutesPerKilometer <= 0f)
            return 0f;

        return 1000f / (minutesPerKilometer * 60f);
    }

    /// <summary>
    /// Advances the signed distance gap produced by the difference between the
    /// runner speed and target speed. Negative means behind target; positive
    /// means ahead. A runner can therefore recover the gap by changing pace.
    /// </summary>
    public static float AccumulateDistanceDeviation(
        float currentDeviationMeters,
        float targetSpeedMetersPerSecond,
        float actualSpeedMetersPerSecond,
        float deltaSeconds)
    {
        float current = IsFinite(currentDeviationMeters) ? currentDeviationMeters : 0f;

        if (!IsNonNegativeFinite(targetSpeedMetersPerSecond)
            || !IsNonNegativeFinite(actualSpeedMetersPerSecond)
            || !IsFinite(deltaSeconds)
            || deltaSeconds <= 0f)
        {
            return current;
        }

        double next = current
                    + ((double)actualSpeedMetersPerSecond - targetSpeedMetersPerSecond)
                    * deltaSeconds;

        // A corrupt or extremely long-running input must never poison all future
        // samples with NaN/Infinity. One million metres is already far beyond any
        // useful synchronicity range while preserving normal recovery semantics.
        if (double.IsNaN(next))
            return current;
        if (next > 1_000_000d)
            return 1_000_000f;
        if (next < -1_000_000d)
            return -1_000_000f;
        return (float)next;
    }

    /// <summary>
    /// Convenience overload for callers that hold pace values in min/km.
    /// Invalid pace samples leave the accumulated gap unchanged.
    /// </summary>
    public static float AccumulateDistanceDeviationFromPaces(
        float currentDeviationMeters,
        float targetPaceMinutesPerKilometer,
        float actualPaceMinutesPerKilometer,
        float deltaSeconds)
    {
        float targetSpeed = PaceToMetersPerSecond(targetPaceMinutesPerKilometer);
        float actualSpeed = PaceToMetersPerSecond(actualPaceMinutesPerKilometer);
        if (targetSpeed <= 0f || actualSpeed <= 0f)
            return IsFinite(currentDeviationMeters) ? currentDeviationMeters : 0f;

        return AccumulateDistanceDeviation(
            currentDeviationMeters, targetSpeed, actualSpeed, deltaSeconds);
    }

    /// <summary>
    /// Returns 0..100 using the worse of:
    ///  - instantaneous target/actual speed similarity, and
    ///  - the accumulated target-distance gap.
    /// At an absolute accumulated gap of 10m (or the supplied threshold), sync
    /// is exactly zero as required by the product specification.
    /// </summary>
    public static float CalculateSyncPercent(
        float targetSpeedMetersPerSecond,
        float actualSpeedMetersPerSecond,
        float accumulatedDeviationMeters,
        float zeroSyncDeviationMeters = ZeroSyncDeviationMeters)
    {
        if (!IsFinite(targetSpeedMetersPerSecond)
            || targetSpeedMetersPerSecond <= 0f
            || !IsNonNegativeFinite(actualSpeedMetersPerSecond)
            || !IsFinite(accumulatedDeviationMeters)
            || !IsFinite(zeroSyncDeviationMeters)
            || zeroSyncDeviationMeters <= 0f)
        {
            return 0f;
        }

        float absoluteDeviation = Math.Abs(accumulatedDeviationMeters);
        if (absoluteDeviation >= zeroSyncDeviationMeters || actualSpeedMetersPerSecond <= 0f)
            return 0f;

        float slower = Math.Min(targetSpeedMetersPerSecond, actualSpeedMetersPerSecond);
        float faster = Math.Max(targetSpeedMetersPerSecond, actualSpeedMetersPerSecond);
        float paceSimilarity = faster > 0f ? slower / faster : 0f;
        float distanceSimilarity = 1f - absoluteDeviation / zeroSyncDeviationMeters;
        float sync = 100f * Math.Min(paceSimilarity, distanceSimilarity);

        if (sync <= 0f) return 0f;
        if (sync >= 100f) return 100f;
        return sync;
    }

    /// <summary>Convenience overload for pace values in min/km.</summary>
    public static float CalculateSyncPercentFromPaces(
        float targetPaceMinutesPerKilometer,
        float actualPaceMinutesPerKilometer,
        float accumulatedDeviationMeters,
        float zeroSyncDeviationMeters = ZeroSyncDeviationMeters)
    {
        return CalculateSyncPercent(
            PaceToMetersPerSecond(targetPaceMinutesPerKilometer),
            PaceToMetersPerSecond(actualPaceMinutesPerKilometer),
            accumulatedDeviationMeters,
            zeroSyncDeviationMeters);
    }

    private static bool IsNonNegativeFinite(float value)
        => IsFinite(value) && value >= 0f;

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);
}
