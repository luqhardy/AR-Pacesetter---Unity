using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// ビルドに同梱した差し替えアバター(.vrm)の一覧。
///
/// <para>置き場は <c>StreamingAssets/Avatars/*.vrm</c>。Resources ではなく StreamingAssets を
/// 使うのは、VRMが<b>実行時にパースするファイル</b>であってUnityのアセットではないため。
/// Resources へ入れてもバイト列として持つだけで、結局ファイルとして読み直すことになる。</para>
///
/// <para><b>同梱のみで、ユーザーによる取り込みはまだ行わない</b>。第1期のスコープ(F-01〜F-11)
/// 外の機能であり、まずは「ヒューマノイドリグ → ロコモーション」「MToon → URP」
/// 「発光 → §7.1のペース色」「ポリゴン予算 → 60fps/M2P 20ms」が通ることを
/// 安全な場所で確かめる。ファイル取り込みUI・ライセンス確認・拒否時の導線は次段階。</para>
/// </summary>
public static class VrmAvatarCatalog
{
    /// <summary>同梱アバターのディレクトリ名(StreamingAssets 直下)。</summary>
    public const string FolderName = "Avatars";

    /// <summary>同梱アバターの置き場。</summary>
    public static string Directory => Path.Combine(Application.streamingAssetsPath, FolderName);

    /// <summary>
    /// 同梱されている .vrm のパス一覧(名前順)。1件も無ければ空。
    ///
    /// <para><b>モデルは同梱していない</b> — ライセンス上、購入者本人が自分のモデルを置く。
    /// 置き方は Docs/VRM_AVATARS.md。</para>
    /// </summary>
    public static List<string> ListBundledFiles()
    {
        var found = new List<string>();

        try
        {
            if (!System.IO.Directory.Exists(Directory)) return found;

            string[] files = System.IO.Directory.GetFiles(Directory, "*.vrm");
            System.Array.Sort(files, System.StringComparer.OrdinalIgnoreCase);
            found.AddRange(files);
        }
        catch (System.Exception e)
        {
            // 一覧が作れないこと自体で走行を止めない。既定アバターで動き続ける
            Debug.LogWarning($"[VRM] 同梱アバターの一覧に失敗: {e.Message}");
        }

        return found;
    }

    /// <summary>表示用の名前(拡張子を除いたファイル名)。</summary>
    public static string DisplayName(string path)
        => string.IsNullOrEmpty(path) ? "" : Path.GetFileNameWithoutExtension(path);
}
