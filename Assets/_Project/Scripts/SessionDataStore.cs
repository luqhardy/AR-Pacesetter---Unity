using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// デュアル・データ保存 (企画書 §4):
///  1) アプリ内DB — persistentDataPath 配下に1セッション1JSONで永続化
///  2) Apple HealthKit — iOSビルドではネイティブブリッジ経由で自動同期
///     （ブリッジ実装は Plugins/iOS 側のTODO。ここでは同期キュー通知まで）
/// </summary>
[Serializable]
public class RunSessionRecord
{
    public string dateIso;
    public float distanceMeters;
    public float elapsedSeconds;
    public float averageSyncRate;
    public string grade;           // S / A / B / C / D
    public string rankLabel;       // Perfect / Great / Good / Try Again
    public float fatigueIndex;
    public float targetPaceMinutesPerKm;
    public float calories; // 推定消費カロリー(体重×距離km×1.05、オンボーディング体重使用)
    public string avatarComment;
    public List<SafetyEventLogger.SafetyEvent> safetyEvents = new List<SafetyEventLogger.SafetyEvent>();

    // ゴースト機能 (企画書§3): 5秒毎の累積距離サンプル。過去の自分の速度
    // プロファイルを再生するために使う。旧データは空リスト(平均ペースで代替)
    public List<PaceSample> paceTimeline = new List<PaceSample>();
}

[Serializable]
public class PaceSample
{
    public float t;      // 走行開始からの秒数
    public float meters; // その時点の累積距離
}

public static class SessionDataStore
{
    private static string SessionDirectory =>
        Path.Combine(Application.persistentDataPath, "RunSessions");

    /// <summary>Persists the record as JSON and queues HealthKit sync. Returns the file path.</summary>
    public static string SaveSession(RunSessionRecord record)
    {
        Directory.CreateDirectory(SessionDirectory);

        string fileName = $"run_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        string fullPath = Path.Combine(SessionDirectory, fileName);

        File.WriteAllText(fullPath, JsonUtility.ToJson(record, prettyPrint: true));
        Debug.Log($"[DATA STORE] Session saved: {fullPath}");

        QueueHealthKitSync(record);
        return fullPath;
    }

    public static List<RunSessionRecord> LoadAllSessions()
    {
        var records = new List<RunSessionRecord>();
        if (!Directory.Exists(SessionDirectory)) return records;

        foreach (string file in Directory.GetFiles(SessionDirectory, "run_*.json"))
        {
            try
            {
                records.Add(JsonUtility.FromJson<RunSessionRecord>(File.ReadAllText(file)));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DATA STORE] Failed to read {file}: {e.Message}");
            }
        }
        return records;
    }

    /// <summary>Most recent session, or null. Used to prefill the next run's target settings.</summary>
    public static RunSessionRecord LoadLatestSession()
    {
        if (!Directory.Exists(SessionDirectory)) return null;

        string[] files = Directory.GetFiles(SessionDirectory, "run_*.json");
        if (files.Length == 0) return null;

        Array.Sort(files); // timestamped names sort chronologically
        try
        {
            return JsonUtility.FromJson<RunSessionRecord>(File.ReadAllText(files[files.Length - 1]));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>ゴースト競走用: dateIso が一致するセッションを返す(なければ null)。</summary>
    public static RunSessionRecord LoadSessionByDateIso(string dateIso)
    {
        if (string.IsNullOrEmpty(dateIso)) return null;
        foreach (RunSessionRecord record in LoadAllSessions())
        {
            if (record != null && record.dateIso == dateIso)
                return record;
        }
        return null;
    }

    private static void QueueHealthKitSync(RunSessionRecord record)
    {
#if UNITY_IOS && !UNITY_EDITOR
        // HealthKitへの実書き込みはSwift側が担当する:
        // SessionEnded イベント受信時に HealthKitWorkoutSaver.swift が
        // HKWorkout(ランニング・距離・カロリー)として保存する。
        // Unity側はアプリ内JSON DBへの保存(一次記録)のみ持つ。
        Debug.Log($"[HEALTHKIT] Workout sync delegated to Swift (SessionEnded): {record.distanceMeters:F0}m / {record.elapsedSeconds:F0}s");
#else
        Debug.Log("[HEALTHKIT] Editor build — HealthKit sync skipped (handled by Swift on device).");
#endif
    }
}
