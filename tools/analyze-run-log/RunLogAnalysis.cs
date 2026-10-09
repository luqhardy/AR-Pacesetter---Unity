using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

/// <summary>
/// 走行ログCSV(F-11 / 基本設計書 §5.2)の解析。Unity非依存の純ロジック。
///
/// <para>実地テスト(Docs/FIELD_TEST_PLAN.md)の数値を手計算させないためのもの:
/// T2 の M2P 95パーセンタイル、100Hz記録の実レートと欠落、GPSの途絶と距離(T4)。</para>
///
/// <para>CSVの列: <c>timestamp, gps_latitude, gps_longitude, imu_accel_x/y/z, avatar_pos_x, avatar_pos_z, latency_m2p</c>。
/// timestamp はエポックms。latency_m2p は -1 = 未計測(エディタ・計測開始前)。
/// GPS は測位が無い間 0,0。</para>
/// </summary>
public static class RunLogAnalysis
{
    public const string ExpectedHeader =
        "timestamp,gps_latitude,gps_longitude,imu_accel_x,imu_accel_y,imu_accel_z," +
        "avatar_pos_x,avatar_pos_z,latency_m2p";

    /// <summary>記録の目標レート(Hz)。</summary>
    public const double TargetRateHz = 100.0;

    /// <summary>実レートがこれ以上なら「100Hz記録」とみなす(目標の95%)。</summary>
    public const double MinAcceptableRateHz = 95.0;

    /// <summary>これより長いサンプル間隔を「欠落」と数える(100Hzで5サンプル分)。</summary>
    public const double GapThresholdMs = 50.0;

    /// <summary>GPSロスト判定の途絶時間(F-09: 更新1.5秒途絶)。</summary>
    public const double GpsStaleSeconds = 1.5;

    private const double EarthRadiusMeters = 6371008.8;

    // ── 行 ─────────────────────────────────────────────────────────────────

    public struct Row
    {
        public long TimestampMs;
        public double Latitude, Longitude;
        public double ImuX, ImuY, ImuZ;
        public double AvatarX, AvatarZ;
        public double LatencyMs;

        public bool HasGpsFix => !(Latitude == 0.0 && Longitude == 0.0);
    }

    public sealed class ParseResult
    {
        public readonly List<Row> Rows = new List<Row>();
        public int MalformedLineCount;
        /// <summary>致命的な問題(ヘッダー不一致等)。null なら読めた。</summary>
        public string Error;
    }

    /// <summary>CSVを読む。壊れた行は数えて読み飛ばす(走行途中の強制終了で最終行が欠けることがある)。</summary>
    public static ParseResult Parse(TextReader reader)
    {
        var result = new ParseResult();
        string header = reader.ReadLine();
        if (header == null)
        {
            result.Error = "empty file";
            return result;
        }
        header = header.Trim().TrimStart('﻿');
        if (header != ExpectedHeader)
        {
            result.Error = "unexpected header (not a run log CSV?): " + header;
            return result;
        }

        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            if (TryParseRow(line, out Row row)) result.Rows.Add(row);
            else result.MalformedLineCount++;
        }
        return result;
    }

    public static bool TryParseRow(string line, out Row row)
    {
        row = default(Row);
        string[] f = line.Split(',');
        if (f.Length != 9) return false;

        var ci = CultureInfo.InvariantCulture;
        const NumberStyles num = NumberStyles.Float;
        return long.TryParse(f[0], NumberStyles.Integer, ci, out row.TimestampMs)
            && double.TryParse(f[1], num, ci, out row.Latitude)
            && double.TryParse(f[2], num, ci, out row.Longitude)
            && double.TryParse(f[3], num, ci, out row.ImuX)
            && double.TryParse(f[4], num, ci, out row.ImuY)
            && double.TryParse(f[5], num, ci, out row.ImuZ)
            && double.TryParse(f[6], num, ci, out row.AvatarX)
            && double.TryParse(f[7], num, ci, out row.AvatarZ)
            && double.TryParse(f[8], num, ci, out row.LatencyMs);
    }

    // ── 集計 ───────────────────────────────────────────────────────────────

    public sealed class Report
    {
        public int RowCount;
        public int MalformedLineCount;

        // 記録レート(F-11 100Hz)
        public double DurationSeconds;
        public double EffectiveRateHz;
        public bool MeetsTargetRate;
        public double IntervalMedianMs, IntervalP95Ms, IntervalMaxMs;
        public int GapCount;
        public double MissingSeconds;
        public int OutOfOrderCount;

        // M2P(§10 / T2)
        public int LatencyMeasuredCount, LatencyUnmeasuredCount, LatencyImplausibleCount;
        public double LatencyMeasuredShare;
        public double LatencyP50Ms, LatencyP95Ms, LatencyP99Ms, LatencyMaxMs, LatencyMeanMs;
        public double LatencyOverBudgetShare;      // > 20ms
        public double LatencyOverMaxAllowedShare;  // > 30ms
        /// <summary>"PASS" / "FAIL" / "NOT MEASURED"(実機以外のログ)。</summary>
        public string T2Verdict;

        // GPS
        public double GpsFixShare;
        public int GpsPositionChangeCount;
        public double GpsLongestGapSeconds;
        public int GpsGapsAtLeastStaleCount;
        public double GpsDistanceMeters;

        // IMU
        public double ImuNonZeroShare;
        public double ImuMeanMagnitude, ImuMaxMagnitude;

        // アバター
        public double AvatarPathMeters;
        public double AvatarAverageSpeedKmh;
    }

    public static Report Analyze(IReadOnlyList<Row> rows, int malformedLineCount = 0)
    {
        var r = new Report { RowCount = rows.Count, MalformedLineCount = malformedLineCount, T2Verdict = "NOT MEASURED" };
        if (rows.Count == 0) return r;

        AnalyzeTimeline(rows, r);
        AnalyzeLatency(rows, r);
        AnalyzeGps(rows, r);
        AnalyzeImuAndAvatar(rows, r);
        return r;
    }

    private static void AnalyzeTimeline(IReadOnlyList<Row> rows, Report r)
    {
        var intervals = new List<double>(Math.Max(0, rows.Count - 1));
        for (int i = 1; i < rows.Count; i++)
        {
            long dt = rows[i].TimestampMs - rows[i - 1].TimestampMs;
            if (dt <= 0) { r.OutOfOrderCount++; continue; }
            intervals.Add(dt);
            if (dt > GapThresholdMs)
            {
                r.GapCount++;
                r.MissingSeconds += (dt - 1000.0 / TargetRateHz) / 1000.0;
            }
        }

        r.DurationSeconds = (rows[rows.Count - 1].TimestampMs - rows[0].TimestampMs) / 1000.0;
        if (r.DurationSeconds > 0)
            r.EffectiveRateHz = (rows.Count - 1) / r.DurationSeconds;
        r.MeetsTargetRate = r.EffectiveRateHz >= MinAcceptableRateHz;

        if (intervals.Count > 0)
        {
            intervals.Sort();
            r.IntervalMedianMs = Percentile(intervals, 50);
            r.IntervalP95Ms = Percentile(intervals, 95);
            r.IntervalMaxMs = intervals[intervals.Count - 1];
        }
    }

    private static void AnalyzeLatency(IReadOnlyList<Row> rows, Report r)
    {
        var measured = new List<double>();
        foreach (Row row in rows)
        {
            double ms = row.LatencyMs;
            if (double.IsNaN(ms) || ms < 0) r.LatencyUnmeasuredCount++;
            else if (ms > MotionToPhotonMath.ImplausibleMs) r.LatencyImplausibleCount++;
            else measured.Add(ms);
        }

        r.LatencyMeasuredCount = measured.Count;
        r.LatencyMeasuredShare = (double)measured.Count / rows.Count;
        if (measured.Count == 0) return;

        measured.Sort();
        double sum = 0; int over = 0, overMax = 0;
        foreach (double ms in measured)
        {
            sum += ms;
            if (!MotionToPhotonMath.MeetsBudget(ms)) over++;
            if (!MotionToPhotonMath.WithinMaxAllowed(ms)) overMax++;
        }

        r.LatencyP50Ms = Percentile(measured, 50);
        r.LatencyP95Ms = Percentile(measured, 95);
        r.LatencyP99Ms = Percentile(measured, 99);
        r.LatencyMaxMs = measured[measured.Count - 1];
        r.LatencyMeanMs = sum / measured.Count;
        r.LatencyOverBudgetShare = (double)over / measured.Count;
        r.LatencyOverMaxAllowedShare = (double)overMax / measured.Count;
        r.T2Verdict = MotionToPhotonMath.MeetsBudget(r.LatencyP95Ms) ? "PASS" : "FAIL";
    }

    private static void AnalyzeGps(IReadOnlyList<Row> rows, Report r)
    {
        int withFix = 0;
        bool havePrev = false;
        double prevLat = 0, prevLon = 0;
        long prevChangeMs = 0;

        foreach (Row row in rows)
        {
            if (!row.HasGpsFix) continue;
            withFix++;

            if (!havePrev)
            {
                havePrev = true;
                prevLat = row.Latitude; prevLon = row.Longitude;
                prevChangeMs = row.TimestampMs;
                continue;
            }
            if (row.Latitude == prevLat && row.Longitude == prevLon) continue;

            r.GpsPositionChangeCount++;
            r.GpsDistanceMeters += HaversineMeters(prevLat, prevLon, row.Latitude, row.Longitude);

            double gap = (row.TimestampMs - prevChangeMs) / 1000.0;
            if (gap > r.GpsLongestGapSeconds) r.GpsLongestGapSeconds = gap;
            if (gap >= GpsStaleSeconds) r.GpsGapsAtLeastStaleCount++;

            prevLat = row.Latitude; prevLon = row.Longitude;
            prevChangeMs = row.TimestampMs;
        }
        r.GpsFixShare = (double)withFix / rows.Count;
    }

    private static void AnalyzeImuAndAvatar(IReadOnlyList<Row> rows, Report r)
    {
        int nonZero = 0;
        double sumMag = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            Row row = rows[i];
            double mag = Math.Sqrt(row.ImuX * row.ImuX + row.ImuY * row.ImuY + row.ImuZ * row.ImuZ);
            if (mag > 0)
            {
                nonZero++;
                sumMag += mag;
                if (mag > r.ImuMaxMagnitude) r.ImuMaxMagnitude = mag;
            }

            if (i > 0)
            {
                double dx = row.AvatarX - rows[i - 1].AvatarX;
                double dz = row.AvatarZ - rows[i - 1].AvatarZ;
                r.AvatarPathMeters += Math.Sqrt(dx * dx + dz * dz);
            }
        }

        r.ImuNonZeroShare = (double)nonZero / rows.Count;
        r.ImuMeanMagnitude = nonZero > 0 ? sumMag / nonZero : 0;
        if (r.DurationSeconds > 0)
            r.AvatarAverageSpeedKmh = r.AvatarPathMeters / r.DurationSeconds * 3.6;
    }

    // ── 補助 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 最近順位法のパーセンタイル(ソート済みの値に対して)。補間せず、実際に観測された値を返す —
    /// 「95%のフレームがこの値以下」をそのまま言える。
    /// </summary>
    public static double Percentile(IReadOnlyList<double> sortedAscending, double percent)
    {
        if (sortedAscending == null || sortedAscending.Count == 0) return double.NaN;
        if (percent <= 0) return sortedAscending[0];
        if (percent >= 100) return sortedAscending[sortedAscending.Count - 1];
        int rank = (int)Math.Ceiling(percent / 100.0 * sortedAscending.Count);
        return sortedAscending[Math.Max(1, rank) - 1];
    }

    /// <summary>2点間の大円距離(m)。</summary>
    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        double toRad = Math.PI / 180.0;
        double dLat = (lat2 - lat1) * toRad;
        double dLon = (lon2 - lon1) * toRad;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * toRad) * Math.Cos(lat2 * toRad) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>km/h を「分:秒 /km」に。0以下は "--"。</summary>
    public static string FormatPace(double kmh)
    {
        if (!(kmh > 0.1)) return "--";
        double minPerKm = 60.0 / kmh;
        int totalSeconds = (int)Math.Round(minPerKm * 60.0);
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00} /km", totalSeconds / 60, totalSeconds % 60);
    }
}
