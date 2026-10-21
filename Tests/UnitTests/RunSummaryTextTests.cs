using NUnit.Framework;

/// <summary>
/// グラスの走行終了カードの文言(タスク表 ARグラス「ランニング終了」)。
/// 誇張しない・桁が増えても崩れない・日本語フォントが無ければ英語の見出し
/// </summary>
[TestFixture]
public class RunSummaryTextTests
{
    [Test]
    public void ランクは大文字で空なら横棒()
    {
        Assert.AreEqual("PERFECT", RunSummaryText.Rank("Perfect"));
        Assert.AreEqual("TRY AGAIN", RunSummaryText.Rank(" try again "));
        Assert.AreEqual("-", RunSummaryText.Rank(""));
        Assert.AreEqual("-", RunSummaryText.Rank(null));
    }

    [Test]
    public void シンクロ率と距離とタイムを並べる()
    {
        Assert.AreEqual("シンクロ率 87%\n距離 5.02 km\nタイム 26:50",
            RunSummaryText.Body(87.4f, 5020f, 1610f, japanese: true));
        Assert.AreEqual("SYNC 87%\nDISTANCE 5.02 km\nTIME 26:50",
            RunSummaryText.Body(87.4f, 5020f, 1610f, japanese: false));
    }

    [Test]
    public void 一時間を超えたら時分秒()
    {
        Assert.AreEqual("1:01:01", RunSummaryText.Duration(3661f));
        Assert.AreEqual("100:00:00", RunSummaryText.Duration(360000f));
    }

    [Test]
    public void 数でない値は横棒でシンクロ率は0から100に収める()
    {
        Assert.AreEqual("SYNC -\nDISTANCE -\nTIME -",
            RunSummaryText.Body(float.NaN, float.NaN, -1f, japanese: false));
        StringAssert.StartsWith("SYNC 100%", RunSummaryText.Body(140f, 0f, 0f, japanese: false));
    }
}
