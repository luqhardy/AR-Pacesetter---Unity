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

    public static void SendLatency(double milliseconds)
        => SendRaw(string.Format(CultureInfo.InvariantCulture,
            "{{\"event\":\"LatencyReport\",\"ms\":{0:F1}}}", milliseconds));

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
            "\"averageSync\":{2:F1},\"distanceKm\":{3:F3},\"elapsedSeconds\":{4:F0}}}",
            record.grade, record.rankLabel, record.averageSyncRate,
            record.distanceMeters / 1000f, record.elapsedSeconds);
        SendRaw(json);
    }
}
