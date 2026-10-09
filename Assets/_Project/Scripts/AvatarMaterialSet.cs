using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// アバター本体(モデル配下)の<b>全レンダラーの全マテリアル</b>をまとめて扱う。
/// 透過率(F-10 フェードアウト・企画書4.1 の50%半透明)とペースシンクロ色の発光(§7.1)に使う。
///
/// <para><b>以前の不具合</b>: <c>GetComponentInChildren&lt;SkinnedMeshRenderer&gt;().material</c> で
/// 最初のレンダラーの最初のマテリアルだけを操作していた。現行モデルの Y Bot は
/// 2メッシュ(Alpha_Surface / Alpha_Joints)・2マテリアルなので、<b>体の半分にしか色が付かず、
/// フェードアウト中も残り半分は不透明のまま</b>スタンバイで突然消えていた。
/// VRMアバター(最大8マテリアル・複数メッシュ)ではさらに偏る。</para>
/// </summary>
public sealed class AvatarMaterialSet
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private readonly List<Material> _materials = new List<Material>();

    public int Count => _materials.Count;
    public bool IsEmpty => _materials.Count == 0;

    /// <summary>
    /// モデル(Animatorの付いたTransform)配下のメッシュ描画のマテリアルを集める。
    /// モデルが解決できなければ <paramref name="fallback"/> のマテリアルだけを使う。
    /// 足元の影・オーラ・VFXはアバターのルート直下にありモデル配下ではないので含まれない。
    /// </summary>
    public static AvatarMaterialSet FromModel(Transform modelRoot, Renderer fallback)
    {
        var set = new AvatarMaterialSet();
        if (modelRoot != null)
        {
            foreach (Renderer r in modelRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer || r is MeshRenderer)
                    set.AddRenderer(r);
            }
        }
        if (set.IsEmpty && fallback != null)
            set.AddRenderer(fallback);
        return set;
    }

    private void AddRenderer(Renderer r)
    {
        // .materials はこのレンダラー専用のインスタンスを返し、以後の呼び出しでも同じものを返す
        foreach (Material m in r.materials)
        {
            if (m != null && !_materials.Contains(m))
                _materials.Add(m);
        }
    }

    /// <summary>現在の透過率(最初の色付きマテリアル)。色を持つマテリアルが無ければ1。</summary>
    public float Alpha
    {
        get
        {
            foreach (Material m in _materials)
                if (HasMainColor(m)) return m.color.a;
            return 1f;
        }
    }

    /// <summary>全マテリアルの透過率を揃える(RGBは各マテリアルのまま)。</summary>
    public void SetAlpha(float alpha)
    {
        foreach (Material m in _materials)
        {
            if (!HasMainColor(m)) continue;
            Color c = m.color;
            c.a = alpha;
            m.color = c;
        }
    }

    /// <summary>全マテリアルの発光色を設定する(発光を持たないシェーダーは飛ばす)。</summary>
    public void SetEmission(Color color)
    {
        foreach (Material m in _materials)
        {
            if (m.HasProperty(EmissionColorId))
                m.SetColor(EmissionColorId, color);
        }
    }

    public void EnableEmissionKeyword()
    {
        foreach (Material m in _materials)
            m.EnableKeyword("_EMISSION");
    }

    /// <summary>E2E検証用: i番目のマテリアル。</summary>
    public Material this[int index] => _materials[index];

    // Material.color は主色プロパティ(_Color、または [MainColor] の _BaseColor)を読む。
    // 無いシェーダーで触るとエラーログが出るので確かめてから
    private static bool HasMainColor(Material m)
        => m.HasProperty("_Color") || m.HasProperty("_BaseColor");
}
