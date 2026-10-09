using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 開発者モード用の状態スナップショットと、F-11走行ログCSVの一覧を作る。
///
/// <para><b>なぜ要るか</b>: 第1期の成果物は「技術限界データのCSV」そのものなのに、
/// 書き出し先は <c>persistentDataPath/RunLogs/</c>(アプリのサンドボックス内)で、
/// アプリからは存在すら見えていなかった。取り出すにはMacのXcodeで
/// コンテナをダウンロードするしかなく、トラックで走った直後に確認する手段が無い。</para>
///
/// <para>ここで一覧とパスをSwiftへ渡し、Swift側の開発者モードから共有シート経由で
/// そのまま書き出せるようにする。併せて「今この端末で何が効いているか」
/// (M2Pの実測有無・IMUの供給元・グラスの画角・GPS判定の状態)も1画面へ出す。</para>
/// </summary>
public static class DevDiagnostics
{
    /// <summary>一覧に載せるログファイルの最大件数(新しい順)。</summary>
    public const int MaxListedLogFiles = 30;

    /// <summary>走行ログCSVの保存ディレクトリ。<see cref="RunTelemetryLogger"/> と同じ場所。</summary>
    public static string LogDirectory => Path.Combine(Application.persistentDataPath, "RunLogs");

    // ════════════════════════════════════════════════════════════════════
    // 診断スナップショット
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 現在の状態を1つのJSONイベントへまとめる。値が取れない項目は素直に空/-1にし、
    /// **もっともらしい値で埋めない**(この画面で嘘をつくと実機の判断を誤らせる)。
    /// </summary>
    public static string BuildSnapshotJson()
    {
        var rows = new List<KeyValuePair<string, string>>();

        // ── M2P (§10) ────────────────────────────────────────────────
        var timing = UnityEngine.Object.FindFirstObjectByType<SensorTimingBridge>(FindObjectsInactive.Include);
        var bridge = UnityEngine.Object.FindFirstObjectByType<ARSessionManagerBridge>(FindObjectsInactive.Include);

        Add(rows, "m2p.native", SensorTimingBridge.NativeAvailable ? "利用可(iOS実機)" : "利用不可(エディタ)");
        Add(rows, "m2p.measuring", timing != null && timing.IsMeasuring ? "計測中" : "停止");
        Add(rows, "m2p.lastMs", bridge != null ? Num(bridge.LastReportedMotionToPhotonMs) : "-1");
        if (timing != null)
        {
            Add(rows, "m2p.samples", timing.Stats.SampleCount.ToString(CultureInfo.InvariantCulture));
            Add(rows, "m2p.avgMs", Num(timing.Stats.AverageMs));
            Add(rows, "m2p.maxMs", Num(timing.Stats.MaxMs));
            Add(rows, "m2p.overBudget", Num(timing.Stats.OverBudgetRatio));
            Add(rows, "m2p.imuStreaming", timing.IsImuStreaming ? "100Hz ネイティブ" : "なし");
        }

        // ── F-11 テレメトリ ──────────────────────────────────────────
        var telemetry = UnityEngine.Object.FindFirstObjectByType<RunTelemetryLogger>(FindObjectsInactive.Include);
        if (telemetry != null)
        {
            Add(rows, "csv.logging", telemetry.IsLogging ? "記録中" : "停止");
            Add(rows, "csv.imuSource", telemetry.ImuSource);
            Add(rows, "csv.nativeImuRows", telemetry.NativeImuRowCount.ToString(CultureInfo.InvariantCulture));
            Add(rows, "csv.droppedRows", telemetry.DroppedRowCount.ToString(CultureInfo.InvariantCulture));
            Add(rows, "csv.timeline", telemetry.TimelineSource);
            Add(rows, "csv.loggedSpanSec", Num(telemetry.LoggedSpanSeconds));
            Add(rows, "csv.wallClockSpanSec", Num(telemetry.WallClockSpanSeconds));
            Add(rows, "csv.timelineDriftSec", Num(telemetry.TimelineDriftSeconds));
            Add(rows, "csv.path", telemetry.CurrentFilePath ?? "");
        }

        // ── GPS (F-09/F-10) ──────────────────────────────────────────
        var gps = UnityEngine.Object.FindFirstObjectByType<GpsSignalMonitor>(FindObjectsInactive.Include);
        if (gps != null)
        {
            Add(rows, "gps.autoLostHandling", gps.AutoLostHandlingEnabled ? "ON(仕様どおり)" : "OFF(手動で無効化中)");
            Add(rows, "gps.accuracyM", Num(gps.LastAccuracyMeters));
            Add(rows, "gps.hasHadGoodFix", gps.HasHadGoodFix ? "取得済み" : "未取得(屋内など)");
            Add(rows, "gps.signalLost", gps.IsSignalLost ? "ロスト中" : "正常");
        }

        // ── ARグラス出力 ─────────────────────────────────────────────
        var glass = UnityEngine.Object.FindFirstObjectByType<GlassViewRig>(FindObjectsInactive.Include);
        if (glass != null)
        {
            Add(rows, "glass.output", glass.IsGlassOutputActive ? "グラスへ出力中" : "iPhone画面");
            Add(rows, "glass.profile", glass.ActiveProfile != null ? glass.ActiveProfile.ToString() : "");
            Add(rows, "glass.orientation", glass.OrientationSource.ToString());
            Add(rows, "glass.downPitchDeg", Num(glass.AppliedDownPitchDegrees));
            Add(rows, "glass.viewportAspect", Num(glass.ViewportAspect));
            Add(rows, "glass.fillsScreen", glass.IsGlassOutputActive
                ? (glass.ViewportMatchesGlass ? "一致(画面を埋めている)" : "不一致(帯が出る)")
                : "-");
            if (!string.IsNullOrEmpty(glass.LastFitReport))
                Add(rows, "glass.fit", glass.LastFitReport);
        }

        // ── 屋外の画像分類 ───────────────────────────────────────────
        var semantics = UnityEngine.Object.FindFirstObjectByType<OutdoorSemanticClassifier>(FindObjectsInactive.Include);
        if (semantics != null)
        {
            Add(rows, "semantics.source", semantics.Source);
            Add(rows, "semantics.samples", semantics.SampleCount.ToString(CultureInfo.InvariantCulture));
        }

        // ── §10 位置誤差・連続稼働 ───────────────────────────────────
        var nfr = UnityEngine.Object.FindFirstObjectByType<NonFunctionalRequirementsMonitor>(FindObjectsInactive.Include);
        if (nfr != null)
        {
            Add(rows, "nfr.position", nfr.Position.Summarize());
            Add(rows, "nfr.endurance", nfr.SummarizeEndurance());
        }

        // ── 状態と描画 ───────────────────────────────────────────────
        var state = UnityEngine.Object.FindFirstObjectByType<GameStateController>(FindObjectsInactive.Include);
        if (state != null) Add(rows, "fsm.state", state.currentState.ToString());

        var visibility = UnityEngine.Object.FindFirstObjectByType<AvatarVisibilityDiagnostics>(FindObjectsInactive.Include);
        if (visibility != null)
            Add(rows, "avatar.visibility", visibility.IsVisible ? "表示中" : visibility.CurrentReason);

        Add(rows, "render.screenPx", $"{Screen.width}x{Screen.height}");
        Add(rows, "render.targetFps", Application.targetFrameRate.ToString(CultureInfo.InvariantCulture));
        Add(rows, "render.currentFps", Num(Time.smoothDeltaTime > 0.0001f ? 1f / Time.smoothDeltaTime : -1f));

        // 平均fpsはカクつきを隠すので、最悪値と「平滑化が飽和するほど長いフレーム」の数も出す
        var engine = UnityEngine.Object.FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (engine != null)
        {
            Add(rows, "render.maxFrameMs", Num(engine.MaxObservedDeltaSeconds * 1000f));
            Add(rows, "render.longFrames", engine.LongFrameCount.ToString(CultureInfo.InvariantCulture));

            // 実機でメッシュから足裏の点を取れているか(FBXのRead/Writeがビルドに反映されたか)
            var planting = engine.GetComponent<FootPlanting>();
            if (planting != null)
                Add(rows, "avatar.footPlanting", planting.UsesMeshSolePoints
                    ? $"メッシュ {planting.SolePointCount}点 / 持ち上げ {Num(planting.CurrentLiftMeters)}m"
                    : "骨からの概算(メッシュを読めない — FBXのRead/Writeを確認)");
        }
        Add(rows, "app.persistentDataPath", Application.persistentDataPath);

        var sb = new StringBuilder();
        sb.Append("{\"event\":\"Diagnostics\",\"rows\":[");
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"key\":\"").Append(Escape(rows[i].Key))
              .Append("\",\"value\":\"").Append(Escape(rows[i].Value)).Append("\"}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    // ════════════════════════════════════════════════════════════════════
    // 走行ログCSVの一覧
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>RunLogs/</c> のCSVを新しい順に列挙する。ディレクトリが無い場合は空配列
    /// (まだ一度も走っていないだけなので、エラーにはしない)。
    /// </summary>
    public static string BuildLogFilesJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"event\":\"LogFiles\",\"directory\":\"").Append(Escape(LogDirectory)).Append("\",\"files\":[");

        try
        {
            if (Directory.Exists(LogDirectory))
            {
                var files = new List<FileInfo>();
                foreach (string path in Directory.GetFiles(LogDirectory, "*.csv"))
                    files.Add(new FileInfo(path));

                files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

                int count = Math.Min(files.Count, MaxListedLogFiles);
                for (int i = 0; i < count; i++)
                {
                    FileInfo f = files[i];
                    if (i > 0) sb.Append(',');
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "{{\"name\":\"{0}\",\"path\":\"{1}\",\"bytes\":{2},\"modifiedIso\":\"{3}\"}}",
                        Escape(f.Name), Escape(f.FullName), f.Length,
                        f.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture));
                }
            }
        }
        catch (Exception e)
        {
            // 一覧が作れないこと自体は走行を妨げない。空配列で返して理由だけ残す
            Debug.LogWarning($"[DEV] 走行ログの一覧に失敗: {e.Message}");
        }

        sb.Append("]}");
        return sb.ToString();
    }

    // ════════════════════════════════════════════════════════════════════

    private static void Add(List<KeyValuePair<string, string>> rows, string key, string value)
        => rows.Add(new KeyValuePair<string, string>(key, value ?? ""));

    private static string Num(double v)
        => v.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>JSON文字列値のエスケープ(パスに含まれる区切りと改行を潰す)。</summary>
    public static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Replace("\t", " ");
    }
}
