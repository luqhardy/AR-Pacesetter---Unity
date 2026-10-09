using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

// 走行ログCSVの解析(実地テスト T2 / T4 と F-11 の記録品質)。
//   dotnet run --project tools/analyze-run-log -- <Log_*.csv | フォルダ>... [--json]
// フォルダを渡すと中の Log_*.csv をすべて読む。複数ファイルなら全体のM2Pもまとめて出す。
// 終了コード: 0 = 全ファイルを読めた / 1 = 読めないファイルがあった / 2 = 使い方の誤り

bool json = args.Contains("--json");
var inputs = args.Where(a => a != "--json").ToList();
if (inputs.Count == 0 || inputs.Contains("-h") || inputs.Contains("--help"))
{
    Console.Error.WriteLine("usage: dotnet run --project tools/analyze-run-log -- <Log_*.csv | folder>... [--json]");
    return 2;
}

var files = new List<string>();
foreach (string input in inputs)
{
    if (Directory.Exists(input))
        files.AddRange(Directory.GetFiles(input, "Log_*.csv").OrderBy(f => f, StringComparer.Ordinal));
    else if (File.Exists(input))
        files.Add(input);
    else
    {
        Console.Error.WriteLine($"not found: {input}");
        return 2;
    }
}
if (files.Count == 0)
{
    Console.Error.WriteLine("no Log_*.csv files found");
    return 2;
}

var ci = CultureInfo.InvariantCulture;
var results = new List<(string Name, RunLogAnalysis.Report Report)>();
var allRows = new List<RunLogAnalysis.Row>();
bool anyError = false;

foreach (string file in files)
{
    RunLogAnalysis.ParseResult parsed;
    using (var reader = new StreamReader(file))
        parsed = RunLogAnalysis.Parse(reader);

    if (parsed.Error != null)
    {
        Console.Error.WriteLine($"{Path.GetFileName(file)}: {parsed.Error}");
        anyError = true;
        continue;
    }
    results.Add((Path.GetFileName(file), RunLogAnalysis.Analyze(parsed.Rows, parsed.MalformedLineCount)));
    allRows.AddRange(parsed.Rows);
}

// 全体: M2Pはファイルをまたいで集計する(T2は走行全体の95%で判定)。
// タイムラインはファイル間の切れ目を欠落と数えないよう、ファイル毎の値を合算する
RunLogAnalysis.Report combined = results.Count > 1 ? RunLogAnalysis.Analyze(allRows) : null;

if (json)
{
    var payload = new
    {
        files = results.Select(r => new { file = r.Name, report = r.Report }),
        combinedLatency = combined == null ? null : new
        {
            measuredRows = combined.LatencyMeasuredCount,
            p50Ms = combined.LatencyP50Ms,
            p95Ms = combined.LatencyP95Ms,
            p99Ms = combined.LatencyP99Ms,
            maxMs = combined.LatencyMaxMs,
            overBudgetShare = combined.LatencyOverBudgetShare,
            t2 = combined.T2Verdict,
        },
    };
    Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
    {
        WriteIndented = true,
        IncludeFields = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    }));
    return anyError ? 1 : 0;
}

foreach (var (name, r) in results)
{
    Console.WriteLine(name);
    PrintReport(r);
    Console.WriteLine();
}

if (combined != null)
{
    double totalSeconds = results.Sum(x => x.Report.DurationSeconds);
    double totalGps = results.Sum(x => x.Report.GpsDistanceMeters);
    Console.WriteLine($"ALL {results.Count} FILES");
    Console.WriteLine(string.Format(ci, "  Duration   {0:F1} min in total · GPS distance {1:F1} m", totalSeconds / 60, totalGps));
    PrintLatency(combined);
    Console.WriteLine();
}

RunLogAnalysis.Report forTemplate = combined ?? (results.Count == 1 ? results[0].Report : null);
if (forTemplate != null)
{
    Console.WriteLine("Field-test template (Docs/FIELD_TEST_PLAN.md §3):");
    Console.WriteLine(forTemplate.LatencyMeasuredCount > 0
        ? string.Format(ci, "T2 p95レイテンシ: {0:F1} ms (サンプル数: {1}) ※CSVから算出 → {2}",
                        forTemplate.LatencyP95Ms, forTemplate.LatencyMeasuredCount, forTemplate.T2Verdict)
        : "T2 p95レイテンシ: 未計測 (latency_m2p が全行 -1 — エディタのログか、計測が始まっていない)");
}

return anyError ? 1 : 0;

void PrintReport(RunLogAnalysis.Report r)
{
    Console.WriteLine(string.Format(ci,
        "  Timeline   {0:N0} rows over {1:F1} s → {2:F1} Hz (target {3:F0} Hz: {4}){5}",
        r.RowCount, r.DurationSeconds, r.EffectiveRateHz, RunLogAnalysis.TargetRateHz,
        r.MeetsTargetRate ? "OK" : "LOW",
        r.MalformedLineCount > 0 ? $" · {r.MalformedLineCount} unreadable lines skipped" : ""));
    Console.WriteLine(string.Format(ci,
        "             interval median {0:F1} / p95 {1:F1} / max {2:F1} ms · {3} gap(s) >{4:F0} ms ({5:F2} s missing) · {6} out of order",
        r.IntervalMedianMs, r.IntervalP95Ms, r.IntervalMaxMs, r.GapCount, RunLogAnalysis.GapThresholdMs,
        r.MissingSeconds, r.OutOfOrderCount));

    PrintLatency(r);

    if (r.GpsFixShare > 0)
    {
        Console.WriteLine(string.Format(ci,
            "  GPS        fix on {0:P1} of rows · {1} position updates · longest gap {2:F1} s · {3} gap(s) ≥{4:F1} s (GPS-lost threshold)",
            r.GpsFixShare, r.GpsPositionChangeCount, r.GpsLongestGapSeconds, r.GpsGapsAtLeastStaleCount,
            RunLogAnalysis.GpsStaleSeconds));
        Console.WriteLine(string.Format(ci, "             GPS distance {0:F1} m (compare with the known track distance for T4)",
            r.GpsDistanceMeters));
    }
    else
    {
        Console.WriteLine("  GPS        no fix in this log (0,0 on every row)");
    }

    Console.WriteLine(r.ImuNonZeroShare > 0
        ? string.Format(ci, "  IMU        non-zero on {0:P1} of rows · mean |a| {1:F2} m/s² · max {2:F2} m/s²",
                        r.ImuNonZeroShare, r.ImuMeanMagnitude, r.ImuMaxMagnitude)
        : "  IMU        all zero");
    Console.WriteLine(string.Format(ci, "  Avatar     path {0:F1} m · average {1:F1} km/h ({2})",
        r.AvatarPathMeters, r.AvatarAverageSpeedKmh, RunLogAnalysis.FormatPace(r.AvatarAverageSpeedKmh)));
}

void PrintLatency(RunLogAnalysis.Report r)
{
    if (r.LatencyMeasuredCount == 0)
    {
        Console.WriteLine("  M2P        not measured (latency_m2p is -1 on every row — editor log, or measurement never started)");
        return;
    }
    Console.WriteLine(string.Format(ci,
        "  M2P        measured on {0:N0} rows ({1:P1}) · p50 {2:F1} · p95 {3:F1} · p99 {4:F1} · max {5:F1} ms",
        r.LatencyMeasuredCount, r.LatencyMeasuredShare, r.LatencyP50Ms, r.LatencyP95Ms, r.LatencyP99Ms, r.LatencyMaxMs));
    Console.WriteLine(string.Format(ci,
        "             over 20 ms: {0:P1} · over 30 ms: {1:P1}{2} → T2 {3} (p95 ≤ 20 ms)",
        r.LatencyOverBudgetShare, r.LatencyOverMaxAllowedShare,
        r.LatencyImplausibleCount > 0 ? $" · {r.LatencyImplausibleCount} implausible (>1 s) rows ignored" : "",
        r.T2Verdict));
}
