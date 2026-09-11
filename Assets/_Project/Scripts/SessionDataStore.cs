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

    /// <summary>アプリ終了(スワイプ等)で中断された記録か。通常終了は false。</summary>
    public bool wasInterrupted;
    public List<SafetyEventLogger.SafetyEvent> safetyEvents = new List<SafetyEventLogger.SafetyEvent>();

    // ゴースト機能 (企画書§3): 5秒毎の累積距離サンプル。過去の自分の速度
    // プロファイルを再生するために使う。旧データは空リスト(平均ペースで代替)
    // (PaceSample は PaceSample.cs に定義 — Unity非依存でユニットテスト可能)
    public List<PaceSample> paceTimeline = new List<PaceSample>();
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

    // ════════════════════════════════════════════════════════════════════════
    // 中断スナップショット — 走行中にアプリを終了(スワイプ)されても記録を失わない
    //
    // 通常の記録は FinishRun でしか書かれないため、走行中にアプリを殺されると
    // その走行は**まるごと消えていた**。バックグラウンド移行のたびにここへ
    // 上書き保存しておき、次回起動時に履歴へ昇格させる。
    //
    // 履歴一覧は run_*.json だけを読むので、スナップショットが残っていても
    // 履歴を汚さない(昇格して初めて run_*.json になる)。
    // 通常終了した場合は FinishRun がスナップショットを消すため昇格されない。
    // ════════════════════════════════════════════════════════════════════════

    private static string InterruptedSnapshotPath =>
        Path.Combine(SessionDirectory, "interrupted.json");

    /// <summary>走行中の現在値をスナップショットとして上書き保存する。</summary>
    public static void SaveInterruptedSnapshot(RunSessionRecord record)
    {
        if (record == null) return;
        try
        {
            Directory.CreateDirectory(SessionDirectory);
            record.wasInterrupted = true;
            File.WriteAllText(InterruptedSnapshotPath, JsonUtility.ToJson(record, prettyPrint: true));
            Debug.Log($"[DATA STORE] 中断スナップショットを保存: {record.distanceMeters:F0}m / {record.elapsedSeconds:F0}s");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DATA STORE] 中断スナップショットの保存に失敗: {e.Message}");
        }
    }

    public static bool HasInterruptedSnapshot() => File.Exists(InterruptedSnapshotPath);

    /// <summary>通常終了時など、中断扱いにする必要が無くなったら消す。</summary>
    public static void ClearInterruptedSnapshot()
    {
        try
        {
            if (File.Exists(InterruptedSnapshotPath))
            {
                File.Delete(InterruptedSnapshotPath);
                Debug.Log("[DATA STORE] 中断スナップショットを破棄(通常終了)。");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DATA STORE] 中断スナップショットの破棄に失敗: {e.Message}");
        }
    }

    /// <summary>
    /// 残っている中断スナップショットを履歴(run_*.json)へ昇格させる。起動時に1回呼ぶ。
    /// </summary>
    /// <param name="savedPath">昇格後のファイルパス(昇格しなかった場合は null)</param>
    /// <returns>昇格した記録。無ければ null</returns>
    public static RunSessionRecord TryPromoteInterruptedSnapshot(out string savedPath)
    {
        savedPath = null;
        if (!HasInterruptedSnapshot()) return null;

        try
        {
            var record = JsonUtility.FromJson<RunSessionRecord>(File.ReadAllText(InterruptedSnapshotPath));
            File.Delete(InterruptedSnapshotPath);

            if (record == null) return null;

            record.wasInterrupted = true;
            savedPath = SaveSession(record);
            Debug.Log($"[DATA STORE] 前回の走行が中断されていたため履歴へ復元: {record.distanceMeters:F0}m");
            return record;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DATA STORE] 中断スナップショットの復元に失敗: {e.Message}");
            return null;
        }
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
