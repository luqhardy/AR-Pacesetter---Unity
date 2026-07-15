using System.Collections.Generic;

/// <summary>
/// ペース関連の純ロジック(Unity非依存)。MonoBehaviourから分離することで
/// dotnet test で高速にユニットテストできる。挙動は抽出元と同一。
///  - TryParsePace: "M:SS" / 小数 / "/km"サフィックスの解析と範囲検証
///  - SampleGhostPace: ゴーストの区間速度→ペース(分/km)算出とクランプ
/// </summary>
public static class PaceMath
{
    /// <summary>
    /// ペース文字列を分/kmへ解析する。"5:30"・"5.5"・"5:00/km"・前後空白を許容。
    /// [minMinPerKm, maxMinPerKm] の範囲外、秒>=60、不正形式は false。
    /// </summary>
    public static bool TryParsePace(string input, out float minutesPerKm,
        float minMinPerKm, float maxMinPerKm)
    {
        minutesPerKm = 5f;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        input = input.Trim().ToLowerInvariant();
        input = input.Replace("/km", string.Empty).Trim();

        if (input.Contains(":"))
        {
            string[] parts = input.Split(':');
            if (parts.Length != 2)
                return false;
            if (!int.TryParse(parts[0], out int minutes))
                return false;
            if (!int.TryParse(parts[1], out int seconds))
                return false;
            if (seconds < 0 || seconds >= 60)
                return false;

            minutesPerKm = minutes + seconds / 60f;
            return minutesPerKm >= minMinPerKm && minutesPerKm <= maxMinPerKm;
        }

        if (!float.TryParse(input, out minutesPerKm))
            return false;

        return minutesPerKm >= minMinPerKm && minutesPerKm <= maxMinPerKm;
    }

    /// <summary>
    /// タイムラインの区間速度から t 時点のペース(分/km)を求める。
    /// null/短い・静止区間(dt/dm極小)・終端超過は averagePace へフォールバック。
    /// 結果は [minPace, maxPace] にクランプ。
    /// </summary>
    public static float SampleGhostPace(IReadOnlyList<PaceSample> timeline,
        float elapsedSeconds, float averagePace, float minPace, float maxPace)
    {
        if (timeline == null)
            return averagePace;

        for (int i = 1; i < timeline.Count; i++)
        {
            if (timeline[i].t >= elapsedSeconds)
            {
                float dt = timeline[i].t - timeline[i - 1].t;
                float dm = timeline[i].meters - timeline[i - 1].meters;
                if (dt < 0.1f || dm < 0.1f)
                    return averagePace; // 静止区間は平均で代替

                float segmentSpeed = dm / dt; // m/s
                float pace = 1000f / segmentSpeed / 60f;
                return pace < minPace ? minPace : (pace > maxPace ? maxPace : pace);
            }
        }

        return averagePace; // 終端を越えたら平均で巡航
    }
}
