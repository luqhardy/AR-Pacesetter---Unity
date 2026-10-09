using System;
using System.Collections.Generic;
using System.Text;

/// <summary>差し替えアバターを受け入れられない理由。</summary>
public enum VrmRejectReason
{
    None = 0,
    NoHumanoidRig,      // ヒューマノイドリグが無い
    TooManyTriangles,   // 描画予算を超える
    TooManyMaterials,   // ドローコール(SetPass)が増えすぎる
    TooManySkinnedMeshes,
    NoRenderer,
}

/// <summary>
/// 差し替えアバター1体の素性。ローダーが実測して詰め、<see cref="VrmAvatarPolicy"/> が判定する。
/// </summary>
public struct VrmAvatarProfile
{
    public string Name;
    public bool HasHumanoidRig;
    public int TriangleCount;
    public int MaterialCount;
    public int SkinnedMeshCount;
    public int RendererCount;
    /// <summary>発光(Emission)を持つマテリアルの数。§7.1のペースシンクロ色に必要。</summary>
    public int EmissiveMaterialCount;
    /// <summary>素の身長(m)。スケール正規化の元になる。</summary>
    public float MeasuredHeightMeters;
}

/// <summary>
/// 差し替えアバターの受け入れ判定 (依存ゼロ・テスト可能)。
///
/// <para><b>なぜ判定が要るか</b>: VRChat向けのアバターは7万〜20万ポリゴン、マテリアル10枚超が
/// 珍しくない。本アプリはその裏で <b>ARKit + GPS + 100Hz IMU + CSV書き出し</b> を回しながら
/// 60fpsと <b>M2P 20ms</b>(§10)を守らなければならない。重いモデルを黙って読み込むと、
/// 「アバターは綺麗だが計測が予算を割る」という最悪の結果になる — PoCの成果物はCSVなので、
/// <b>見た目のために計測を壊してはいけない</b>。</para>
///
/// <para><b>VRChatのアバターをそのまま読めない理由</b>は別にある(iOSはランタイムでの
/// コード読み込みを許さず、VRC SDKのコンポーネントやPoiyomi等のシェーダはビルドに存在しない)。
/// ここが見るのは「読み込めた後、走らせてよいか」だけ。詳細は Docs/VRM_AVATARS.md。</para>
/// </summary>
public static class VrmAvatarPolicy
{
    /// <summary>三角形数の上限。ARKitと同居して60fpsを保てる実用上の目安。</summary>
    public const int MaxTriangles = 70000;

    /// <summary>マテリアル数の上限(SetPass呼び出しに直結する)。</summary>
    public const int MaxMaterials = 8;

    /// <summary>スキンメッシュ数の上限(スキニングはCPU/GPUどちらでも効く)。</summary>
    public const int MaxSkinnedMeshes = 4;

    /// <summary>受け入れ可否。</summary>
    public static bool IsAcceptable(VrmAvatarProfile p) => Evaluate(p) == VrmRejectReason.None;

    /// <summary>
    /// 最初に見つかった不適合を返す。順序は「直せない理由 → 重すぎる理由」。
    /// </summary>
    public static VrmRejectReason Evaluate(VrmAvatarProfile p)
    {
        // リグが無いと歩行・走行アニメーションを当てられない。差し替えの前提条件
        if (!p.HasHumanoidRig) return VrmRejectReason.NoHumanoidRig;
        if (p.RendererCount <= 0) return VrmRejectReason.NoRenderer;

        if (p.TriangleCount > MaxTriangles) return VrmRejectReason.TooManyTriangles;
        if (p.MaterialCount > MaxMaterials) return VrmRejectReason.TooManyMaterials;
        if (p.SkinnedMeshCount > MaxSkinnedMeshes) return VrmRejectReason.TooManySkinnedMeshes;

        return VrmRejectReason.None;
    }

    /// <summary>
    /// §7.1 のペースシンクロ色(緑/橙→赤/青)が見た目に出るか。
    ///
    /// <para>発光マテリアルが1枚も無いモデルは<b>読み込めるが色が出ない</b>。
    /// 拒否はしない — 走ること自体はできるため。ただし黙って落とすと
    /// 「色が変わらないのはバグだ」と誤解されるので、警告として明示する。</para>
    /// </summary>
    public static bool SupportsPaceColor(VrmAvatarProfile p) => p.EmissiveMaterialCount > 0;

    /// <summary>人が読める判定結果。開発者モードとログにそのまま出す。</summary>
    public static string Describe(VrmAvatarProfile p)
    {
        VrmRejectReason reason = Evaluate(p);
        var sb = new StringBuilder();

        sb.Append(string.IsNullOrEmpty(p.Name) ? "(無名)" : p.Name).Append(": ");
        sb.Append(reason == VrmRejectReason.None ? "使用可" : "使用不可 — " + ReasonText(reason));
        sb.Append(" / ")
          .Append(p.TriangleCount.ToString("N0")).Append("三角形 (上限 ").Append(MaxTriangles.ToString("N0")).Append(")")
          .Append(" / マテリアル ").Append(p.MaterialCount).Append(" (上限 ").Append(MaxMaterials).Append(")")
          .Append(" / スキンメッシュ ").Append(p.SkinnedMeshCount);

        if (p.MeasuredHeightMeters > 0f)
            sb.Append(" / 素の身長 ").Append(p.MeasuredHeightMeters.ToString("F2")).Append("m");

        if (reason == VrmRejectReason.None && !SupportsPaceColor(p))
            sb.Append(" / ⚠ 発光マテリアルが無いため §7.1 のペース色は出ません");

        return sb.ToString();
    }

    public static string ReasonText(VrmRejectReason reason)
    {
        switch (reason)
        {
            case VrmRejectReason.None: return "問題なし";
            case VrmRejectReason.NoHumanoidRig:
                return "ヒューマノイドリグが無い(歩行・走行アニメーションを適用できない)";
            case VrmRejectReason.NoRenderer:
                return "描画するメッシュが無い";
            case VrmRejectReason.TooManyTriangles:
                return "三角形数が多すぎる(60fpsとM2P 20msを守れない)";
            case VrmRejectReason.TooManyMaterials:
                return "マテリアルが多すぎる(ドローコールが増え描画が間に合わない)";
            case VrmRejectReason.TooManySkinnedMeshes:
                return "スキンメッシュが多すぎる";
            default: return "不明";
        }
    }

    /// <summary>
    /// 候補のうち使用可能なものだけを、名前順を保ったまま返す。
    /// 選択UIは「読めるが使えない」ものを並べない — 選べない選択肢は選択肢ではない。
    /// </summary>
    public static List<VrmAvatarProfile> FilterAcceptable(IEnumerable<VrmAvatarProfile> candidates)
    {
        var ok = new List<VrmAvatarProfile>();
        if (candidates == null) return ok;

        foreach (VrmAvatarProfile p in candidates)
        {
            if (IsAcceptable(p)) ok.Add(p);
        }
        return ok;
    }
}
