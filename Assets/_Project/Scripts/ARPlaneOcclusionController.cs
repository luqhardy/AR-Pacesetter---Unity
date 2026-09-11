using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 検出済みARプレーンによる<b>アバターの遮蔽</b>を制御する (F-05 / §7 の見え方)。
///
/// <para><b>なぜ必要か</b>: シーンのプレーンプレハブ (<c>ARFeatheredOcclusionPlane</c>) は
/// <c>OcclusionMaterial</c>(<c>ColorMask 0</c> / <c>ZWrite On</c>) を持つ。これは
/// 「色は書かず深度だけ書く」材質で、**検出済みの壁・机の向こう側にいるアバターは
/// 描画されなくなる**。屋内で壁の手前3mに立つとアバターは壁の中/向こう側に入るため、
/// そこで消えてしまう — 「壁があるとアバターが非表示になる」の直接の原因のひとつ。</para>
///
/// <para><b>第1期の既定はOFF</b>(遮蔽しない)。基本設計書 §7.7 の中央領域は
/// 「完全透過(アバターのみ)」であり、伴走者が環境に隠れて見えなくなるのは
/// ペーシングという目的に反する。遮蔽による現実感が欲しくなったら
/// <see cref="OccludeAvatarBehindPlanes"/> を true にすれば元の挙動へ戻る。</para>
///
/// <para>切り替えるのは <b>Renderer だけ</b> — MeshCollider は残す。
/// 接地(<c>GroundSnap</c>)と断崖判定はプレーンのコライダーを使うため、
/// ここでコライダーを消すと床が取れなくなる。</para>
/// </summary>
public class ARPlaneOcclusionController : MonoBehaviour
{
    [Tooltip("検出済み平面でアバターを遮蔽する。第1期は既定OFF — " +
             "壁・机の向こうでもアバターを見せ続ける")]
    [SerializeField] private bool occludeAvatarBehindPlanes = false;

    [Tooltip("平面は走行中に増えるため定期的に適用し直す(秒)")]
    [SerializeField] private float refreshIntervalSeconds = 0.5f;

    private ARPlaneManager _planeManager;
    private float _nextRefreshTime;
    private int _lastAppliedCount = -1;
    private bool _lastAppliedSetting;

    /// <summary>アバターを平面の背後で遮蔽するか。実行時に切り替えられる。</summary>
    public bool OccludeAvatarBehindPlanes
    {
        get => occludeAvatarBehindPlanes;
        set
        {
            if (occludeAvatarBehindPlanes == value) return;
            occludeAvatarBehindPlanes = value;
            Apply(force: true);
        }
    }

    /// <summary>最後に適用した平面の数(検証用)。ARプレーンが無い環境では0。</summary>
    public int AppliedPlaneCount { get; private set; }

    private void Awake()
    {
#if UNITY_2023_1_OR_NEWER
        _planeManager = Object.FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
#else
        _planeManager = Object.FindObjectOfType<ARPlaneManager>(true);
#endif
    }

    private void Update()
    {
        if (Time.time < _nextRefreshTime) return;
        _nextRefreshTime = Time.time + Mathf.Max(0.1f, refreshIntervalSeconds);
        Apply(force: false);
    }

    /// <summary>
    /// 検出済み平面のRendererへ現在の設定を反映する。
    /// 平面が増減した時と設定変更時だけ実際に書き込む(毎回の走査は数枚なので安い)。
    /// </summary>
    private void Apply(bool force)
    {
        if (_planeManager == null) return;

        int count = _planeManager.trackables.count;
        if (!force && count == _lastAppliedCount && occludeAvatarBehindPlanes == _lastAppliedSetting)
            return;

        int applied = 0;
        foreach (ARPlane plane in _planeManager.trackables)
        {
            if (plane == null) continue;

            // Renderer だけを切り替える。コライダーは接地・断崖判定が使うので残す
            var renderers = plane.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = occludeAvatarBehindPlanes;

            applied++;
        }

        if (applied != AppliedPlaneCount || occludeAvatarBehindPlanes != _lastAppliedSetting)
        {
            Debug.Log($"[PLANE OCCLUSION] 遮蔽{(occludeAvatarBehindPlanes ? "ON" : "OFF")} " +
                      $"を平面{applied}枚へ適用(コライダーは維持)。");
        }

        AppliedPlaneCount   = applied;
        _lastAppliedCount   = count;
        _lastAppliedSetting = occludeAvatarBehindPlanes;
    }
}
