using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Unity → Swift 送信 (AR-runner リポジトリの UnityBridge.swift 契約):
/// iOSビルドでは Plugins/iOS/UnitySwiftBridge.mm 経由で
/// NSNotification "UnityToSwiftMessage" を発行し、Swift側の
/// UnityBridge.onUnityMessage(_:) がJSONを受け取る。エディタではログ出力のみ。
///
/// 送信イベント(Swift側のswitchに対応):
///   {"event":"SyncRateUpdated","value":87}
///   {"event":"AvatarStateChanged","state":"Run"}   // Idle/Run/Slow/Fast/Goal/Lost
///   {"event":"GPSLost"} / {"event":"GPSRecovered"}
///   {"event":"LatencyReport","ms":16.4}
/// </summary>
public static class SwiftMessageSender
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void UnitySendMessageToSwift(string json);
#endif

    public static void SendRaw(string json)
    {
#if UNITY_IOS && !UNITY_EDITOR
        UnitySendMessageToSwift(json);
#else
        Debug.Log($"[Unity → Swift] {json}");
#endif
    }

    public static void SendSyncRate(int percent)
        => SendRaw($"{{\"event\":\"SyncRateUpdated\",\"value\":{percent}}}");

    public static void SendAvatarState(string state)
        => SendRaw($"{{\"event\":\"AvatarStateChanged\",\"state\":\"{state}\"}}");

    public static void SendGpsLost()
        => SendRaw("{\"event\":\"GPSLost\"}");

    public static void SendGpsRecovered()
        => SendRaw("{\"event\":\"GPSRecovered\"}");

    /// <summary>
    /// アバターの可視状態と、見えていない場合の経路(理由)。変化時のみ送られる。
    /// Swift側は走行画面にバナーで出す — 実機で「消えた」が「なぜ消えたか」になる
    /// </summary>
    public static void SendAvatarVisibility(bool visible, string reason)
    {
        string escaped = (reason ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        SendRaw($"{{\"event\":\"AvatarVisibility\",\"visible\":{(visible ? "true" : "false")},\"reason\":\"{escaped}\"}}");
    }

    /// <summary>
    /// M2P実測の報告 (§10 / FIELD_TEST_PLAN T2)。<c>ms</c> が -1 なら未計測。
    ///
    /// <para><paramref name="stats"/> を渡すと区間の最大値・超過率・サンプル数も載せる。
    /// 1Hzの瞬時値だけではサンプルの谷間で起きた超過を取りこぼすため、
    /// p95評価が実態を外さないよう併せて送る。統計が無ければ従来どおり <c>ms</c> のみ。</para>
    /// </summary>
    public static void SendLatency(double milliseconds, MotionToPhotonStats stats = null)
    {
        if (stats == null || stats.SampleCount == 0)
        {
            SendRaw(string.Format(CultureInfo.InvariantCulture,
                "{{\"event\":\"LatencyReport\",\"ms\":{0:F1}}}", milliseconds));
            return;
        }

        SendRaw(string.Format(CultureInfo.InvariantCulture,
            "{{\"event\":\"LatencyReport\",\"ms\":{0:F1},\"maxMs\":{1:F1}," +
            "\"overBudgetRatio\":{2:F3},\"sampleCount\":{3}}}",
            milliseconds, stats.MaxMs, stats.OverBudgetRatio, stats.SampleCount));
    }

    /// <summary>
    /// 音声警告 (企画書4.3 — 対象は赤信号/交差点のみ、Swift側でTTC優先制御)。
    /// kind: "Signal" | "Intersection"
    /// </summary>
    public static void SendVoiceAlert(string kind, double ttcSeconds)
        => SendRaw(string.Format(CultureInfo.InvariantCulture,
            "{{\"event\":\"VoiceAlert\",\"kind\":\"{0}\",\"ttc\":{1:F1}}}", kind, ttcSeconds));

    /// <summary>
    /// 差し替えアバターの取り込み結果。<b>断った理由を必ず添える</b> —
    /// VRChat向けアバターは三角形数の上限に掛かることが多く、
    /// 「失敗しました」だけでは利用者が直しようがない。
    /// </summary>
    public static void SendVrmImportResult(bool accepted, string name, string report, string reason)
        => SendRaw(string.Format(CultureInfo.InvariantCulture,
            "{{\"event\":\"VrmImportResult\",\"accepted\":{0},\"name\":\"{1}\"," +
            "\"report\":\"{2}\",\"reason\":\"{3}\"}}",
            accepted ? "true" : "false", Escape(name), Escape(report), Escape(reason)));

    /// <summary>選べるアバターの一覧(同梱 + 取り込み)。</summary>
    public static void SendVrmAvatarList(System.Collections.Generic.List<string> paths, string current)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"event\":\"VrmAvatarList\",\"current\":\"").Append(Escape(current)).Append("\",\"avatars\":[");
        for (int i = 0; i < paths.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"name\":\"").Append(Escape(VrmAvatarCatalog.DisplayName(paths[i])))
              .Append("\",\"path\":\"").Append(Escape(paths[i])).Append("\"}");
        }
        sb.Append("]}");
        SendRaw(sb.ToString());
    }

    /// <summary>
    /// JSON文字列値のエスケープ。パスや説明文に区切り文字・改行が入るため必須。
    /// 実装は <see cref="DevDiagnostics.Escape"/> と同一のものを使い、二重に持たない。
    /// </summary>
    private static string Escape(string s) => DevDiagnostics.Escape(s);

    /// <summary>走行履歴一覧 (Swift側 "HistoryData" ケース、HistoryViewが表示)。</summary>
    public static void SendHistory(System.Collections.Generic.List<RunSessionRecord> records)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"event\":\"HistoryData\",\"sessions\":[");
        for (int i = 0; i < records.Count; i++)
        {
            RunSessionRecord r = records[i];
            if (r == null) continue;
            if (i > 0) sb.Append(',');
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "{{\"dateIso\":\"{0}\",\"distanceKm\":{1:F3},\"elapsedSeconds\":{2:F0}," +
                "\"averageSync\":{3:F1},\"grade\":\"{4}\"}}",
                r.dateIso, r.distanceMeters / 1000f, r.elapsedSeconds,
                r.averageSyncRate, r.grade);
        }
        sb.Append("]}");
        SendRaw(sb.ToString());
    }

    /// <summary>走行終了時の結果サマリー(Swift側は将来 "SessionEnded" ケースを追加して受信)。</summary>
    public static void SendSessionResult(RunSessionRecord record)
    {
        if (record == null) return;
        string json = string.Format(CultureInfo.InvariantCulture,
            "{{\"event\":\"SessionEnded\",\"grade\":\"{0}\",\"rank\":\"{1}\"," +
            "\"averageSync\":{2:F1},\"distanceKm\":{3:F3},\"elapsedSeconds\":{4:F0},\"calories\":{5:F0}}}",
            record.grade, record.rankLabel, record.averageSyncRate,
            record.distanceMeters / 1000f, record.elapsedSeconds, record.calories);
        SendRaw(json);
    }
}
