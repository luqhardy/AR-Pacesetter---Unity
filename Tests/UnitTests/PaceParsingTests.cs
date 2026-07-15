using NUnit.Framework;

/// <summary>
/// PaceMath.TryParsePace の純ロジック検証。
/// 有効ペース範囲は 3.5〜7.0 分/km(境界含む)。
/// </summary>
[TestFixture]
public class PaceParsingTests
{
    private const float Min = 3.5f;
    private const float Max = 7.0f;

    [TestCase("5:00", 5.0f)]
    [TestCase("5:30", 5.5f)]
    [TestCase("6:15", 6.25f)]
    [TestCase("3:30", 3.5f)]   // 下限境界(inclusive)
    [TestCase("7:00", 7.0f)]   // 上限境界(inclusive)
    [TestCase("5.5", 5.5f)]    // 小数入力
    [TestCase("5:00/km", 5.0f)] // 単位サフィックス除去
    [TestCase(" 5:00 ", 5.0f)]  // 前後空白トリム
    public void TryParsePace_ValidInputs_ReturnsTrueWithValue(string input, float expected)
    {
        bool ok = PaceMath.TryParsePace(input, out float pace, Min, Max);
        Assert.IsTrue(ok, $"'{input}' は有効なはず");
        Assert.AreEqual(expected, pace, 0.001f);
    }

    [TestCase("3:00")]   // 下限未満(3.0 < 3.5)
    [TestCase("7:30")]   // 上限超過(7.5 > 7.0)
    [TestCase("5:60")]   // 秒が60以上
    [TestCase("5:99")]
    [TestCase("abc")]    // 非数値
    [TestCase("")]       // 空文字
    [TestCase("   ")]    // 空白のみ
    [TestCase("5:0:0")]  // コロン過多
    public void TryParsePace_InvalidInputs_ReturnsFalse(string input)
    {
        bool ok = PaceMath.TryParsePace(input, out _, Min, Max);
        Assert.IsFalse(ok, $"'{input}' は無効なはず");
    }

    [Test]
    public void TryParsePace_Null_ReturnsFalse()
    {
        Assert.IsFalse(PaceMath.TryParsePace(null, out _, Min, Max));
    }
}
