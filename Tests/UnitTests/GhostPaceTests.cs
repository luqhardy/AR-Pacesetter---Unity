using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// PaceMath.SampleGhostPace の純ロジック検証。
/// 区間速度→ペース(分/km)、フォールバック、クランプ([3,12])。
/// </summary>
[TestFixture]
public class GhostPaceTests
{
    private const float Avg = 6.0f;
    private const float MinPace = 3.0f;
    private const float MaxPace = 12.0f;

    private static List<PaceSample> Timeline(params (float t, float m)[] pts)
    {
        var list = new List<PaceSample>();
        foreach (var (t, m) in pts) list.Add(new PaceSample { t = t, meters = m });
        return list;
    }

    private static float Sample(List<PaceSample> tl, float t)
        => PaceMath.SampleGhostPace(tl, t, Avg, MinPace, MaxPace);

    [Test]
    public void NullTimeline_ReturnsAverage()
    {
        Assert.AreEqual(Avg, Sample(null, 10f), 0.001f);
    }

    [Test]
    public void WithinSegment_ReturnsSegmentPace()
    {
        // 5秒で15m → 3 m/s → 1000/3/60 = 5.556 分/km
        Assert.AreEqual(1000f / 3f / 60f, Sample(Timeline((0, 0), (5, 15)), 3f), 0.01f);
    }

    [Test]
    public void StationarySegment_FallsBackToAverage()
    {
        // 距離が動かない区間(dm < 0.1)は平均で代替
        Assert.AreEqual(Avg, Sample(Timeline((0, 0), (5, 0)), 3f), 0.001f);
    }

    [Test]
    public void BeyondTimelineEnd_FallsBackToAverage()
    {
        Assert.AreEqual(Avg, Sample(Timeline((0, 0), (5, 15)), 10f), 0.001f);
    }

    [Test]
    public void VeryFastSegment_ClampsToMinPace()
    {
        // 1秒で20m → 20 m/s → 0.83 分/km → 下限3.0へクランプ
        Assert.AreEqual(MinPace, Sample(Timeline((0, 0), (1, 20)), 0.5f), 0.001f);
    }

    [Test]
    public void VerySlowSegment_ClampsToMaxPace()
    {
        // 60秒で10m → 0.167 m/s → 100 分/km → 上限12.0へクランプ
        Assert.AreEqual(MaxPace, Sample(Timeline((0, 0), (60, 10)), 30f), 0.001f);
    }
}
