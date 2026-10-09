using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

/// <summary>
/// 走行ログCSV解析(tools/analyze-run-log)の検証。
/// 本丸は T2 の判定 — M2P の95パーセンタイルが「未計測(-1)」や異常値に汚されず、
/// 20ms の基準で正しく合否を出すこと。
/// </summary>
[TestFixture]
public class RunLogAnalysisTests
{
    private static RunLogAnalysis.Row Row(long ms, double latency = -1, double lat = 0, double lon = 0,
                                        double ax = 0, double avatarZ = 0)
    {
        return new RunLogAnalysis.Row
        {
            TimestampMs = ms, LatencyMs = latency, Latitude = lat, Longitude = lon,
            ImuX = ax, AvatarZ = avatarZ,
        };
    }

    // ── 読み込み ────────────────────────────────────────────────────────

    [Test]
    public void ロガーが書く形式をそのまま読める()
    {
        string csv = RunLogAnalysis.ExpectedHeader + "\n" +
                     "1790312004001,35.6812362,139.7671248,0.0100,-0.0200,-0.0734,-0.0189,63.2581,14.25\n" +
                     "1790312004011,35.6812362,139.7671248,0.0000,0.0000,-0.0734,-0.0189,63.2581,-1.00\n";

        var parsed = RunLogAnalysis.Parse(new StringReader(csv));

        Assert.IsNull(parsed.Error);
        Assert.AreEqual(2, parsed.Rows.Count);
        Assert.AreEqual(1790312004001L, parsed.Rows[0].TimestampMs);
        Assert.AreEqual(35.6812362, parsed.Rows[0].Latitude, 1e-9);
        Assert.AreEqual(14.25, parsed.Rows[0].LatencyMs, 1e-9);
    }

    [Test]
    public void 走行ログ以外のCSVは読まない()
    {
        var parsed = RunLogAnalysis.Parse(new StringReader("a,b,c\n1,2,3\n"));
        Assert.IsNotNull(parsed.Error);
        Assert.IsNotNull(RunLogAnalysis.Parse(new StringReader("")).Error);
    }

    [Test]
    public void 強制終了で欠けた最終行は数えて読み飛ばす()
    {
        string csv = RunLogAnalysis.ExpectedHeader + "\n" +
                     "1000,0,0,0,0,0,0,0,-1\n" +
                     "1010,0,0,0,0";

        var parsed = RunLogAnalysis.Parse(new StringReader(csv));

        Assert.IsNull(parsed.Error);
        Assert.AreEqual(1, parsed.Rows.Count);
        Assert.AreEqual(1, parsed.MalformedLineCount);
    }

    [Test]
    public void 先頭のBOMは無視する()
    {
        var parsed = RunLogAnalysis.Parse(new StringReader("﻿" + RunLogAnalysis.ExpectedHeader + "\n1000,0,0,0,0,0,0,0,-1\n"));
        Assert.IsNull(parsed.Error);
        Assert.AreEqual(1, parsed.Rows.Count);
    }

    // ── 記録レート ──────────────────────────────────────────────────────

    [Test]
    public void 途切れない100Hz記録は合格()
    {
        var rows = new List<RunLogAnalysis.Row>();
        for (int i = 0; i <= 1000; i++) rows.Add(Row(1000 + i * 10));

        var r = RunLogAnalysis.Analyze(rows);

        Assert.AreEqual(10.0, r.DurationSeconds, 1e-9);
        Assert.AreEqual(100.0, r.EffectiveRateHz, 1e-6);
        Assert.IsTrue(r.MeetsTargetRate);
        Assert.AreEqual(0, r.GapCount);
    }

    [Test]
    public void 欠落は件数と失われた時間で数える()
    {
        var rows = new List<RunLogAnalysis.Row> { Row(0), Row(10), Row(510), Row(520) }; // 500msの穴

        var r = RunLogAnalysis.Analyze(rows);

        Assert.AreEqual(1, r.GapCount);
        Assert.AreEqual(0.49, r.MissingSeconds, 1e-9);
        Assert.AreEqual(500.0, r.IntervalMaxMs, 1e-9);
        Assert.IsFalse(r.MeetsTargetRate);
    }

    [Test]
    public void 時刻の逆行を数える()
    {
        var r = RunLogAnalysis.Analyze(new List<RunLogAnalysis.Row> { Row(0), Row(10), Row(5), Row(20) });
        Assert.AreEqual(1, r.OutOfOrderCount);
    }

    // ── M2P / T2 ────────────────────────────────────────────────────────

    [Test]
    public void 未計測の行はパーセンタイルに入れない()
    {
        // 実測は10msだけ。-1 を 0ms として混ぜるとp95が不当に良くなる
        var rows = new List<RunLogAnalysis.Row>();
        for (int i = 0; i < 90; i++) rows.Add(Row(i * 10, latency: -1));
        for (int i = 0; i < 10; i++) rows.Add(Row(900 + i * 10, latency: 25));

        var r = RunLogAnalysis.Analyze(rows);

        Assert.AreEqual(10, r.LatencyMeasuredCount);
        Assert.AreEqual(90, r.LatencyUnmeasuredCount);
        Assert.AreEqual(25.0, r.LatencyP95Ms, 1e-9);
        Assert.AreEqual("FAIL", r.T2Verdict);
    }

    [Test]
    public void p95が20ms以下なら合格_超過率も出す()
    {
        var rows = new List<RunLogAnalysis.Row>();
        for (int i = 0; i < 95; i++) rows.Add(Row(i * 10, latency: 15));
        for (int i = 0; i < 5; i++) rows.Add(Row(950 + i * 10, latency: 40));

        var r = RunLogAnalysis.Analyze(rows);

        Assert.AreEqual(15.0, r.LatencyP95Ms, 1e-9);
        Assert.AreEqual("PASS", r.T2Verdict);
        Assert.AreEqual(0.05, r.LatencyOverBudgetShare, 1e-9);
        Assert.AreEqual(0.05, r.LatencyOverMaxAllowedShare, 1e-9);
        Assert.AreEqual(40.0, r.LatencyMaxMs, 1e-9);
    }

    [Test]
    public void 一秒超の異常値は実測として扱わない()
    {
        var rows = new List<RunLogAnalysis.Row> { Row(0, latency: 12), Row(10, latency: 5000) };

        var r = RunLogAnalysis.Analyze(rows);

        Assert.AreEqual(1, r.LatencyMeasuredCount);
        Assert.AreEqual(1, r.LatencyImplausibleCount);
        Assert.AreEqual(12.0, r.LatencyMaxMs, 1e-9);
    }

    [Test]
    public void エディタのログは未計測と判定する()
    {
        var r = RunLogAnalysis.Analyze(new List<RunLogAnalysis.Row> { Row(0), Row(10) });
        Assert.AreEqual("NOT MEASURED", r.T2Verdict);
        Assert.AreEqual(0, r.LatencyMeasuredCount);
    }

    [Test]
    public void パーセンタイルは補間せず観測値を返す()
    {
        var sorted = new List<double> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Assert.AreEqual(5.0, RunLogAnalysis.Percentile(sorted, 50));
        Assert.AreEqual(10.0, RunLogAnalysis.Percentile(sorted, 95));
        Assert.AreEqual(1.0, RunLogAnalysis.Percentile(sorted, 0));
        Assert.IsNaN(RunLogAnalysis.Percentile(new List<double>(), 95));
    }

    // ── GPS ─────────────────────────────────────────────────────────────

    [Test]
    public void GPS距離は大円距離で積算する()
    {
        // 緯度0.001度 ≒ 111.2m
        Assert.AreEqual(111.2, RunLogAnalysis.HaversineMeters(35.0, 139.0, 35.001, 139.0), 0.1);

        var rows = new List<RunLogAnalysis.Row>
        {
            Row(0, lat: 35.0, lon: 139.0),
            Row(1000, lat: 35.0, lon: 139.0),     // 同じ位置 = 更新ではない
            Row(2000, lat: 35.001, lon: 139.0),
            Row(3000, lat: 35.002, lon: 139.0),
        };
        var r = RunLogAnalysis.Analyze(rows);

        Assert.AreEqual(2, r.GpsPositionChangeCount);
        Assert.AreEqual(222.4, r.GpsDistanceMeters, 0.2);
        Assert.AreEqual(1.0, r.GpsFixShare, 1e-9);
    }

    [Test]
    public void GPSの途絶は1点5秒以上を数える()
    {
        var rows = new List<RunLogAnalysis.Row>
        {
            Row(0, lat: 35.0, lon: 139.0),
            Row(1000, lat: 35.0001, lon: 139.0),
            Row(3000, lat: 35.0002, lon: 139.0),   // 2秒の途絶
            Row(4000, lat: 0, lon: 0),             // 測位なしの行
        };
        var r = RunLogAnalysis.Analyze(rows);

        Assert.AreEqual(1, r.GpsGapsAtLeastStaleCount);
        Assert.AreEqual(2.0, r.GpsLongestGapSeconds, 1e-9);
        Assert.AreEqual(0.75, r.GpsFixShare, 1e-9);
    }

    // ── IMU・アバター ───────────────────────────────────────────────────

    [Test]
    public void アバターの移動距離と平均ペースを出す()
    {
        var rows = new List<RunLogAnalysis.Row>();
        for (int i = 0; i <= 100; i++) rows.Add(Row(i * 1000, avatarZ: i * 3.0, ax: 1.0)); // 3m/s を100秒

        var r = RunLogAnalysis.Analyze(rows);

        Assert.AreEqual(300.0, r.AvatarPathMeters, 1e-9);
        Assert.AreEqual(10.8, r.AvatarAverageSpeedKmh, 1e-9);
        Assert.AreEqual("5:33 /km", RunLogAnalysis.FormatPace(r.AvatarAverageSpeedKmh));
        Assert.AreEqual(1.0, r.ImuNonZeroShare, 1e-9);
        Assert.AreEqual("--", RunLogAnalysis.FormatPace(0));
    }
}
