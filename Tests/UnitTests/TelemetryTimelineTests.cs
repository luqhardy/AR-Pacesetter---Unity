using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// F-11 走行ログCSV(§5.2)の timestamp 列が単調増加であること。
///
/// <para>以前の不具合: 実機でネイティブIMUのサンプルが0件のフレームに合成行へ落ちると、
/// 合成行の時刻は「ログ開始 + 合成行の連番×10ms」だったため、走行の途中に
/// 開始直後の時刻の行が混ざった。解析ツールはその次の行を「数十分の欠落」と数える。</para>
/// </summary>
[TestFixture]
public class TelemetryTimelineTests
{
    private const long Start = 1_700_000_000_000;

    [Test]
    public void 合成行は開始から10ms刻み()
    {
        var t = new TelemetryTimeline();
        t.Reset(Start);
        Assert.AreEqual(Start, t.NextSynthetic());
        Assert.AreEqual(Start + 10, t.NextSynthetic());
        Assert.AreEqual(Start + 20, t.NextSynthetic());
    }

    [Test]
    public void ネイティブ行の後の合成行は開始時刻へ巻き戻らない()
    {
        var t = new TelemetryTimeline();
        t.Reset(Start);
        // 走行開始から10分、ネイティブ100Hzで書いた
        for (long ms = 0; ms < 600_000; ms += 10)
            Assert.IsTrue(t.TryAcceptNative(Start + ms));

        // ネイティブが途絶えて合成行に落ちても、直前の行の後ろへ続く
        long first = t.NextSynthetic();
        Assert.AreEqual(Start + 599_990 + 10, first);
        Assert.AreEqual(first + 10, t.NextSynthetic());
    }

    [Test]
    public void 前の行より古いネイティブサンプルは書かずに数える()
    {
        var t = new TelemetryTimeline();
        t.Reset(Start);
        t.NextSynthetic(); t.NextSynthetic(); t.NextSynthetic(); // Start .. Start+20

        Assert.IsFalse(t.TryAcceptNative(Start + 15), "合成行と重なる時間帯");
        Assert.IsFalse(t.TryAcceptNative(Start + 20), "同一時刻も不可");
        Assert.IsTrue(t.TryAcceptNative(Start + 25));
        Assert.AreEqual(2, t.RejectedNativeCount);
    }

    [Test]
    public void 混在しても時刻列は狭義単調増加()
    {
        var t = new TelemetryTimeline();
        t.Reset(Start);
        var written = new List<long>();
        var rng = new System.Random(20261005);
        long native = Start + 40; // ネイティブは開始より少し遅れて届き始める

        for (int frame = 0; frame < 5000; frame++)
        {
            if (rng.NextDouble() < 0.1)
            {
                written.Add(t.NextSynthetic()); // 途絶フレーム
            }
            else
            {
                int samples = rng.Next(0, 3);
                for (int i = 0; i < samples; i++, native += 10)
                    if (t.TryAcceptNative(native)) written.Add(native);
            }
        }

        for (int i = 1; i < written.Count; i++)
            Assert.Greater(written[i], written[i - 1], $"row {i}");
        Assert.AreEqual(written[written.Count - 1], t.LastWrittenMs);
    }

    [Test]
    public void Resetで次のログの開始時刻から数え直す()
    {
        var t = new TelemetryTimeline();
        t.Reset(Start);
        t.TryAcceptNative(Start + 5000);
        t.Reset(Start + 10_000);
        Assert.AreEqual(Start + 10_000, t.NextSynthetic());
        Assert.AreEqual(0, t.RejectedNativeCount);
    }
}
