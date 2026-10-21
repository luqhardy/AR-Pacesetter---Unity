using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 選んだルートからの逸脱(HANDOVER §5「ルート同期」・企画書 4.3 サイレントルート復帰)。
/// GPS の座標だけで測り、トラックの測位誤差では反応しないこと
/// </summary>
[TestFixture]
public class RouteDeviationTests
{
    // 神戸付近。緯度1度 ≒ 111.2km、この緯度で経度1度 ≒ 91.4km
    private const double Lat0 = 34.69;
    private const double Lon0 = 135.19;
    private const double MetersPerDegLat = 111194.9;

    private static double NorthDeg(double meters) => meters / MetersPerDegLat;
    private static double EastDeg(double meters) => meters / (MetersPerDegLat * Math.Cos(Lat0 * Math.PI / 180.0));

    /// <summary>南から北へまっすぐ200m(10m間隔 — 記録した経路の間引きと同じ)</summary>
    private static List<(double lat, double lon)> NorthLine()
    {
        var route = new List<(double lat, double lon)>();
        for (int i = 0; i <= 20; i++) route.Add((Lat0 + NorthDeg(i * 10), Lon0));
        return route;
    }

    [Test]
    public void 経路の上なら距離はほぼ0()
    {
        Assert.IsTrue(RouteDeviation.TryCrossTrack(NorthLine(), Lat0 + NorthDeg(55), Lon0,
            out double d, out _, out _, out int seg));
        Assert.AreEqual(0.0, d, 0.05);
        Assert.AreEqual(5, seg);
    }

    [Test]
    public void 横へ30m外れたら30mで経路は西にある()
    {
        Assert.IsTrue(RouteDeviation.TryCrossTrack(NorthLine(), Lat0 + NorthDeg(100), Lon0 + EastDeg(30),
            out double d, out double east, out double north, out _));
        Assert.AreEqual(30.0, d, 0.2);
        Assert.AreEqual(-30.0, east, 0.2, "経路へ向かうには西へ");
        Assert.AreEqual(0.0, north, 0.2);
    }

    [Test]
    public void 経路の端より先は端の点までの距離()
    {
        Assert.IsTrue(RouteDeviation.TryCrossTrack(NorthLine(), Lat0 + NorthDeg(240), Lon0,
            out double d, out _, out double north, out _));
        Assert.AreEqual(40.0, d, 0.2);
        Assert.AreEqual(-40.0, north, 0.2);
    }

    [Test]
    public void 平らな配列を点の列にし壊れた点は飛ばす()
    {
        var points = RouteDeviation.ParseFlatLatLon(new[] { Lat0, Lon0, double.NaN, 1.0, Lat0 + 0.001, Lon0, 95.0, 0.0 });
        Assert.AreEqual(2, points.Count);
        Assert.AreEqual(0, RouteDeviation.ParseFlatLatLon(new[] { Lat0, Lon0 }).Count, "1点では線にならない");
        Assert.AreEqual(0, RouteDeviation.ParseFlatLatLon(null).Count);
        Assert.IsFalse(RouteDeviation.TryCrossTrack(new List<(double, double)>(), Lat0, Lon0, out _, out _, out _, out _));
    }

    // ── 判定(GPS の誤差を見込む) ─────────────────────────────────────

    [Test]
    public void しきい値は最低15mで精度が悪ければ広がる()
    {
        Assert.AreEqual(15.0, RouteDeviationGate.EnterThreshold(5.0), 1e-9);
        Assert.AreEqual(24.0, RouteDeviationGate.EnterThreshold(12.0), 1e-9);
        Assert.AreEqual(10.0, RouteDeviationGate.ExitThreshold(5.0), 1e-9);
    }

    [Test]
    public void トラックの測位誤差では逸脱にならない()
    {
        // 精度5mの測位が経路から最大12mまで揺れる(記録した経路の誤差+今の測位の誤差)
        var gate = new RouteDeviationGate();
        var rnd = new Random(7);
        for (int i = 0; i < 600; i++)
            gate.Update(rnd.NextDouble() * 12.0, 5.0, 1.0);
        Assert.IsFalse(gate.IsDeviated);
    }

    [Test]
    public void 一度の飛びでは反応せず続いたら逸脱()
    {
        var gate = new RouteDeviationGate();
        gate.Update(3, 5, 0);
        gate.Update(40, 5, 1);                   // 飛び1回
        gate.Update(3, 5, 1);
        Assert.IsFalse(gate.IsDeviated);
        for (int i = 0; i < 4; i++) gate.Update(30, 5, 1);
        Assert.IsFalse(gate.IsDeviated, "4秒ではまだ");
        Assert.IsTrue(gate.Update(30, 5, 1), "5秒続いたら逸脱(状態が変わった)");
        Assert.IsTrue(gate.IsDeviated);
    }

    [Test]
    public void 戻るのは内側に2秒いたとき_しきい値付近では行き来しない()
    {
        var gate = new RouteDeviationGate();
        gate.Update(3, 5, 0);
        for (int i = 0; i < 5; i++) gate.Update(30, 5, 1);
        Assert.IsTrue(gate.IsDeviated);
        // 入るしきい値(15m)と戻るしきい値(10m)の間 — 逸脱のまま
        for (int i = 0; i < 10; i++) gate.Update(12, 5, 1);
        Assert.IsTrue(gate.IsDeviated);
        gate.Update(4, 5, 1);
        Assert.IsTrue(gate.IsDeviated, "1秒ではまだ");
        gate.Update(4, 5, 1);
        Assert.IsFalse(gate.IsDeviated);
    }

    [Test]
    public void 精度の悪い測位は判定に使わない()
    {
        var gate = new RouteDeviationGate();
        gate.Update(3, 5, 0);
        for (int i = 0; i < 20; i++) gate.Update(60, 35, 1);   // 精度35m
        Assert.IsFalse(gate.IsDeviated);
        Assert.AreEqual(3.0, gate.LastDistanceMeters, 1e-9);
    }

    [Test]
    public void GPSが途切れた後の1回でいきなり逸脱にしない()
    {
        var gate = new RouteDeviationGate();
        gate.Update(3, 5, 0);
        gate.Update(30, 5, 60);   // 60秒ぶりの測位でも2秒ぶんしか数えない
        Assert.IsFalse(gate.IsDeviated);
    }

    [Test]
    public void ルートに近づくまでは判定しない()
    {
        var gate = new RouteDeviationGate();
        for (int i = 0; i < 20; i++) gate.Update(300, 5, 1);   // スタート地点へ移動中・別の場所の記録
        Assert.IsTrue(gate.IsRouteFarAway);
        Assert.IsFalse(gate.IsDeviated);
        gate.Update(8, 5, 1);                                   // 着いた
        Assert.IsFalse(gate.IsRouteFarAway);
        for (int i = 0; i < 6; i++) gate.Update(150, 5, 1);     // 着いた後に離れたら逸脱
        Assert.IsTrue(gate.IsDeviated);
    }

    [Test]
    public void リセットで最初からやり直す()
    {
        var gate = new RouteDeviationGate();
        gate.Update(3, 5, 0);
        for (int i = 0; i < 6; i++) gate.Update(30, 5, 1);
        gate.Reset();
        Assert.IsFalse(gate.IsDeviated);
        Assert.IsFalse(gate.IsRouteFarAway);
        Assert.Less(gate.LastDistanceMeters, 0.0);
    }
}
