using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ゴースト機能 (企画書 §3 ゴースト機能への拡張):
/// 過去セッションのペース推移(paceTimeline)を再生し、アバターを
/// 「過去の自分」と同じ速度プロファイルで走らせる。
/// タイムラインが無い旧データは平均ペースで代替する。
/// </summary>
public class GhostPaceDriver : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private PeripheralHUDManager hudManager;

    private const float MinPaceMinutesPerKm = 3.0f;
    private const float MaxPaceMinutesPerKm = 12.0f;
    private const float PaceUpdateIntervalSeconds = 1.0f;

    private List<PaceSample> _timeline;
    private float _ghostAveragePaceMinPerKm = 6.0f;
    private string _ghostDateIso = "";
    private bool _isActive = false;
    private float _nextPaceUpdate = 0f;

    public bool IsActive => _isActive;
    public string GhostDateIso => _ghostDateIso;

    void Awake()
    {
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (hudManager == null)
            hudManager = FindFirstObjectByType<PeripheralHUDManager>(FindObjectsInactive.Include);
    }

    /// <summary>過去セッションをゴーストとして設定する。走行開始前に呼ぶ。</summary>
    public void Activate(RunSessionRecord ghostRecord)
    {
        if (ghostRecord == null)
        {
            Deactivate();
            return;
        }

        _ghostDateIso = ghostRecord.dateIso;
        _timeline = ghostRecord.paceTimeline != null && ghostRecord.paceTimeline.Count >= 2
            ? ghostRecord.paceTimeline
            : null;

        // 平均ペース(タイムライン欠損時のフォールバック兼、タイムライン終端以降の速度)
        if (ghostRecord.distanceMeters > 1f && ghostRecord.elapsedSeconds > 1f)
        {
            float avgSpeed = ghostRecord.distanceMeters / ghostRecord.elapsedSeconds; // m/s
            _ghostAveragePaceMinPerKm = Mathf.Clamp(
                1000f / avgSpeed / 60f, MinPaceMinutesPerKm, MaxPaceMinutesPerKm);
        }

        _isActive = true;
        _nextPaceUpdate = 0f;
        Debug.Log($"[GHOST] Activated — {ghostRecord.dateIso}, " +
                  $"{(_timeline != null ? $"{_timeline.Count} samples" : "average-pace fallback")} " +
                  $"(avg {_ghostAveragePaceMinPerKm:F2} min/km).");
    }

    public void Deactivate()
    {
        _isActive = false;
        _timeline = null;
        _ghostDateIso = "";
    }

    void Update()
    {
        if (!_isActive || avatarEngine == null) return;
        if (!avatarEngine.HasStarted || avatarEngine.IsSessionEnded) return;

        if (Time.time < _nextPaceUpdate) return;
        _nextPaceUpdate = Time.time + PaceUpdateIntervalSeconds;

        float elapsed = hudManager != null ? hudManager.ElapsedTimeSeconds : 0f;
        float paceMinPerKm = SamplePaceAt(elapsed);
        avatarEngine.UpdateTargetPace(paceMinPerKm);
    }

    /// <summary>経過時間 t 時点のゴーストのペース(分/km)を区間速度から求める。</summary>
    private float SamplePaceAt(float elapsedSeconds)
    {
        if (_timeline == null)
            return _ghostAveragePaceMinPerKm;

        // t を挟むサンプル区間を探す(サンプルは時刻昇順)
        for (int i = 1; i < _timeline.Count; i++)
        {
            if (_timeline[i].t >= elapsedSeconds)
            {
                float dt = _timeline[i].t - _timeline[i - 1].t;
                float dm = _timeline[i].meters - _timeline[i - 1].meters;
                if (dt < 0.1f || dm < 0.1f)
                    return _ghostAveragePaceMinPerKm; // 静止区間は平均で代替

                float segmentSpeed = dm / dt; // m/s
                return Mathf.Clamp(1000f / segmentSpeed / 60f,
                    MinPaceMinutesPerKm, MaxPaceMinutesPerKm);
            }
        }

        // タイムライン終端を越えたら平均ペースで巡航
        return _ghostAveragePaceMinPerKm;
    }
}
