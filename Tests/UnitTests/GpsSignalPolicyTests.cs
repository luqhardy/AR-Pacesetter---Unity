using NUnit.Framework;
using Phase = GpsSignalPolicy.Phase;
using Action = GpsSignalPolicy.Action;

/// <summary>
/// F-09/F-10 GPSロストの判定と復帰遷移(AGENTS.md §5)。
///
/// <para>本丸は「5秒を超えるロストの後もアバターが戻ること」。以前の監視は
/// InertialMovement からしか復帰させず、FadeOut / Standby からの復帰はエディタのRキーにしか
/// 無かったため、実機では一度スタンバイに入るとその走行中ずっとアバターが消えていた。</para>
/// </summary>
[TestFixture]
public class GpsSignalPolicyTests
{
    private const float Stale = 1.5f, LostAt = 10f, RecoveredAt = 5f;

    private static bool Lost(float since, float acc, bool goodFix = true, bool requireFix = true)
        => GpsSignalPolicy.IsLost(goodFix, requireFix, since, Stale, acc, LostAt);

    private static bool Recovered(float since, float acc)
        => GpsSignalPolicy.IsRecovered(since, Stale, acc, RecoveredAt);

    [Test]
    public void 精度10m以上または1_5秒途絶でロスト()
    {
        Assert.IsTrue(Lost(0.1f, 10f));
        Assert.IsTrue(Lost(1.5f, 3f));
        Assert.IsFalse(Lost(1.4f, 9.9f));
    }

    [Test]
    public void 良好な測位を一度も得ていなければロストにしない()
    {
        Assert.IsFalse(Lost(5f, 25f, goodFix: false));
        Assert.IsTrue(Lost(5f, 25f, goodFix: false, requireFix: false));
    }

    [Test]
    public void 復帰は新鮮かつ精度5m以内のときだけ()
    {
        Assert.IsTrue(Recovered(0.2f, 5f));
        Assert.IsFalse(Recovered(0.2f, 7f), "5〜10mはロストでも復帰でもない(ヒステリシス)");
        Assert.IsFalse(Recovered(1.5f, 3f), "途絶中の古い良い精度では復帰しない");
        Assert.IsFalse(Recovered(0.2f, -1f), "無効サンプル");
    }

    [Test]
    public void 境界の精度ではロストと復帰が同時に成立しない()
    {
        for (float acc = 0f; acc <= 15f; acc += 0.5f)
            Assert.IsFalse(Lost(0.1f, acc) && Recovered(0.1f, acc), $"acc={acc}");
    }

    [Test]
    public void 通常追従中のロストで慣性移動へ()
    {
        Assert.AreEqual(Action.EnterInertialMovement, GpsSignalPolicy.Decide(Phase.Normal, lost: true, recovered: false));
        Assert.AreEqual(Action.None, GpsSignalPolicy.Decide(Phase.Normal, lost: false, recovered: true));
    }

    [Test]
    public void 慣性移動中とフェード中の復帰は通常追従へ直接戻る()
    {
        Assert.AreEqual(Action.ReturnToNormal, GpsSignalPolicy.Decide(Phase.InertialMovement, false, true));
        Assert.AreEqual(Action.ReturnToNormal, GpsSignalPolicy.Decide(Phase.FadeOut, false, true),
            "§5: フェード完了(1秒)前の復帰は Normal へ — 以前は無視されスタンバイまで進んでいた");
    }

    [Test]
    public void GPSロストのスタンバイは復帰で再集積へ進む()
    {
        Assert.AreEqual(Action.BeginReaccumulation, GpsSignalPolicy.Decide(Phase.StandbyAfterGpsLoss, false, true),
            "以前はこの経路が実機に無く、アバターが走行終了まで戻らなかった");
        Assert.AreEqual(Action.None, GpsSignalPolicy.Decide(Phase.StandbyAfterGpsLoss, true, false));
    }

    [Test]
    public void グラス切断などのスタンバイはGPS復帰で戻さない()
    {
        // §8.3: 再接続だけではアバターを出さず、準備画面からの再スタートを待つ
        Assert.AreEqual(Action.None, GpsSignalPolicy.Decide(Phase.Other, false, true));
        Assert.AreEqual(Action.None, GpsSignalPolicy.Decide(Phase.Other, true, false));
    }

    [Test]
    public void 復帰待ちの間ロストが続けば何もしない()
    {
        Assert.AreEqual(Action.None, GpsSignalPolicy.Decide(Phase.InertialMovement, true, false));
        Assert.AreEqual(Action.None, GpsSignalPolicy.Decide(Phase.FadeOut, true, false));
    }
}
