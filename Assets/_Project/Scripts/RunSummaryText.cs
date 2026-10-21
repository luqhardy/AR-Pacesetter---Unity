using System;
using System.Globalization;

/// <summary>
/// グラスの走行終了カード(<see cref="GlassRunSummaryCard"/>)の文言。依存ゼロの純ロジック(Tests/UnitTests でテストする)。
///
/// <para>タスク表 ARグラス「ランニング終了」: 視界中央に「シンクロ率」「走行距離」「タイム」「獲得ランク」を、
/// 誇張しすぎないシンプルなカードで数秒。詳しい結果はスマホ側(S7)で見る。</para>
///
/// <para>日本語フォントが端末に無いと日本語は描かれない(□にもならず消える)ので、そのときは英語の見出しにする。</para>
/// </summary>
public static class RunSummaryText
{
    /// <summary>カードの1行目: 獲得ランク(PERFECT〜TRY AGAIN)。空なら「-」</summary>
    public static string Rank(string rankLabel)
        => string.IsNullOrWhiteSpace(rankLabel) ? "-" : rankLabel.Trim().ToUpperInvariant();

    /// <summary>カードの2行目以降: シンクロ率・距離・タイム(改行区切り)</summary>
    public static string Body(float averageSyncPercent, float distanceMeters, float elapsedSeconds, bool japanese)
    {
        string sync = IsUsable(averageSyncPercent)
            ? Math.Round(Clamp(averageSyncPercent, 0f, 100f)).ToString("0", CultureInfo.InvariantCulture) + "%"
            : "-";
        string distance = IsUsable(distanceMeters) && distanceMeters >= 0f
            ? (distanceMeters / 1000f).ToString("0.00", CultureInfo.InvariantCulture) + " km"
            : "-";
        string time = Duration(elapsedSeconds);

        return japanese
            ? $"シンクロ率 {sync}\n距離 {distance}\nタイム {time}"
            : $"SYNC {sync}\nDISTANCE {distance}\nTIME {time}";
    }

    /// <summary>1時間未満は「分:秒」、それ以上は「時:分:秒」。数でない・負なら「-」</summary>
    public static string Duration(float elapsedSeconds)
    {
        if (!IsUsable(elapsedSeconds) || elapsedSeconds < 0f) return "-";
        int total = (int)Math.Floor(elapsedSeconds);
        int h = total / 3600, m = total % 3600 / 60, s = total % 60;
        return h > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", h, m, s)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", m, s);
    }

    private static bool IsUsable(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
}
