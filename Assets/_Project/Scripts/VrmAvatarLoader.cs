using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 差し替えアバターの計測・受け入れ判定・入れ替えを行う。
///
/// <para><b>設計の要点</b>: 「.vrmファイルを解釈する」部分だけが UniVRM に依存し、
/// <b>計測・判定・差し替えは素のGameObjectに対して動く</b>。おかげで
/// パッケージが入っていない環境(Windows開発機・E2E)でも、シーンにある既存モデルを
/// 使って入れ替え経路そのものを検証できる — 一番壊れやすいのはVRMのパースではなく、
/// 差し替えた後の再配線(Animator・レンダラー・身長)のほうなので、そこを常時テストに掛ける。</para>
///
/// <para>UniVRM 未導入時は <see cref="IsRuntimeLoadAvailable"/> が false になり、
/// ファイルからの読み込みだけが無効になる。既定アバターの動作には一切影響しない。</para>
/// </summary>
public sealed class VrmAvatarLoader : MonoBehaviour
{
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private AvatarModelSwitcher modelSwitcher;

    /// <summary>実行時に .vrm を読み込めるか(UniVRM が導入されていれば true)。</summary>
#if UNIVRM
    public const bool IsRuntimeLoadAvailable = true;
#else
    public const bool IsRuntimeLoadAvailable = false;
#endif

    /// <summary>現在表示している差し替えアバターの名前。既定モデルのままなら空。</summary>
    public string CurrentAvatarName { get; private set; } = "";

    /// <summary>直近に計測したプロファイル。</summary>
    public VrmAvatarProfile LastProfile { get; private set; }

    /// <summary>直近の判定結果(人が読める形)。開発者モードへ出す。</summary>
    public string LastReport { get; private set; } = "";

    private void Awake()
    {
        if (avatarEngine == null) avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (modelSwitcher == null && avatarEngine != null)
            modelSwitcher = avatarEngine.GetComponent<AvatarModelSwitcher>();
    }

    // ════════════════════════════════════════════════════════════════════
    // 計測 — UniVRM に依存しない
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// モデルの素性を実測する。三角形数はメッシュから、発光はマテリアルのキーワード/プロパティから。
    /// </summary>
    public static VrmAvatarProfile Measure(GameObject model, string name = null)
    {
        var p = new VrmAvatarProfile { Name = name ?? (model != null ? model.name : "") };
        if (model == null) return p;

        Animator anim = model.GetComponentInChildren<Animator>(true);
        p.HasHumanoidRig = anim != null && anim.avatar != null && anim.avatar.isHuman;

        var materials = new HashSet<Material>();
        float minY = float.MaxValue, maxY = float.MinValue;

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        p.RendererCount = renderers.Length;

        foreach (Renderer r in renderers)
        {
            if (r is SkinnedMeshRenderer skinned)
            {
                p.SkinnedMeshCount++;
                if (skinned.sharedMesh != null) p.TriangleCount += CountTriangles(skinned.sharedMesh);
            }
            else if (r is MeshRenderer)
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) p.TriangleCount += CountTriangles(mf.sharedMesh);
            }

            foreach (Material m in r.sharedMaterials)
            {
                if (m != null) materials.Add(m);
            }

            Bounds b = r.bounds;
            if (b.size.sqrMagnitude > 0f)
            {
                if (b.min.y < minY) minY = b.min.y;
                if (b.max.y > maxY) maxY = b.max.y;
            }
        }

        p.MaterialCount = materials.Count;
        foreach (Material m in materials)
        {
            if (IsEmissive(m)) p.EmissiveMaterialCount++;
        }

        if (maxY > minY) p.MeasuredHeightMeters = maxY - minY;
        return p;
    }

    private static int CountTriangles(Mesh mesh)
    {
        int total = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            total += (int)(mesh.GetIndexCount(i) / 3);
        return total;
    }

    /// <summary>
    /// §7.1 のペース色を出せるマテリアルか。
    /// URP/Built-in の Emission と、VRMのMToonが持つ発光プロパティの両方を見る。
    /// </summary>
    private static bool IsEmissive(Material m)
    {
        if (m == null) return false;
        if (m.IsKeywordEnabled("_EMISSION")) return true;
        if (m.HasProperty("_EmissionColor")) return true;
        if (m.HasProperty("_EmissionMap")) return true;
        if (m.HasProperty("_EmissiveTex")) return true;  // MToon
        if (m.HasProperty("_EmissionMultiplier")) return true;
        return false;
    }

    // ════════════════════════════════════════════════════════════════════
    // 差し替え — ここが一番壊れやすいので常時テストに掛ける
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 計測したモデルを受け入れ、アバターコンテナの表示モデルとして差し替える。
    ///
    /// <para>既存モデルは<b>破棄せず無効化</b>する。拒否されたときや戻したいときに
    /// 既定アバターへ復帰できるようにするため。</para>
    /// </summary>
    public bool TryAdopt(GameObject model, string displayName, out VrmRejectReason reason)
    {
        reason = VrmRejectReason.NoRenderer;
        if (model == null || avatarEngine == null) return false;

        VrmAvatarProfile profile = Measure(model, displayName);
        LastProfile = profile;
        LastReport = VrmAvatarPolicy.Describe(profile);

        reason = VrmAvatarPolicy.Evaluate(profile);
        if (reason != VrmRejectReason.None)
        {
            Debug.LogWarning($"[VRM] 受け入れませんでした — {LastReport}");
            return false;
        }

        // 既存の表示モデルを退避(破棄はしない)
        Animator current = AvatarRigLocator.FindBestAnimator(avatarEngine.transform);
        if (current != null && current.transform != avatarEngine.transform)
            current.gameObject.SetActive(false);

        model.transform.SetParent(avatarEngine.transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.SetActive(true);

        // 再配線: Animator・IK・レンダラー参照・身長。
        // AvatarModelSwitcher が既に持っている経路を使い、同じ処理を二重に書かない
        if (modelSwitcher != null) modelSwitcher.RebindActiveModel();
        avatarEngine.ReapplyHeight();

        CurrentAvatarName = displayName ?? model.name;
        Debug.Log($"[VRM] アバターを差し替えました — {LastReport}");

        if (!VrmAvatarPolicy.SupportsPaceColor(profile))
            Debug.LogWarning("[VRM] 発光マテリアルが無いため §7.1 のペースシンクロ色は視覚に出ません。");

        return true;
    }

    // ════════════════════════════════════════════════════════════════════
    // ベンダー境界 — .vrm のパースだけが UniVRM に依存する
    // ════════════════════════════════════════════════════════════════════

#if UNIVRM
    /// <summary>同梱の .vrm を読み込んで差し替える。</summary>
    public bool TryLoadFromFile(string path, out VrmRejectReason reason)
    {
        reason = VrmRejectReason.NoRenderer;
        try
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            var data = new UniGLTF.GlbBinaryParser(bytes, path).Parse();
            using (var loader = new VRM.VRMImporterContext(new VRM.VRMData(data)))
            {
                UniGLTF.RuntimeGltfInstance instance = loader.Load();
                instance.ShowMeshes();
                return TryAdopt(instance.Root, VrmAvatarCatalog.DisplayName(path), out reason);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRM] 読み込みに失敗しました ({path}): {e.Message}");
            return false;
        }
    }
#else
    /// <summary>
    /// UniVRM 未導入。ファイルからの読み込みだけが無効で、既定アバターには影響しない。
    /// 導入手順は Docs/VRM_AVATARS.md。
    /// </summary>
    public bool TryLoadFromFile(string path, out VrmRejectReason reason)
    {
        reason = VrmRejectReason.NoRenderer;
        Debug.LogWarning("[VRM] UniVRM が未導入のため .vrm を読み込めません (Docs/VRM_AVATARS.md)。");
        return false;
    }
#endif
}
