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

    /// <summary>同梱アバターの置き場(読み取り専用)。</summary>
    public static string Directory => Path.Combine(Application.streamingAssetsPath, FolderName);

    /// <summary>
    /// ユーザーが取り込んだアバターの置き場。
    ///
    /// <para>iOSでは <c>Application.persistentDataPath</c> が <c>Documents/</c> を指すため、
    /// Swift側が <c>Documents/Avatars/</c> へコピーしたファイルはここに現れる。
    /// 取り込み自体は絶対パスを受け取って読むので、仮に両者がずれても読み込みは成立する
    /// (一覧に出なくなるだけ)。</para>
    /// </summary>
    public static string ImportedDirectory => Path.Combine(Application.persistentDataPath, FolderName);

    /// <summary>
    /// 同梱されている .vrm のパス一覧(名前順)。1件も無ければ空。
    ///
    /// <para><b>モデルは同梱していない</b> — ライセンス上、購入者本人が自分のモデルを置く。
    /// 置き方は Docs/VRM_AVATARS.md。</para>
    /// </summary>
    public static List<string> ListBundledFiles() => ListIn(Directory);

    /// <summary>ユーザーが取り込んだ .vrm の一覧(名前順)。</summary>
    public static List<string> ListImportedFiles() => ListIn(ImportedDirectory);

    /// <summary>同梱 + 取り込みの全一覧。同名ファイルは取り込み側を優先する。</summary>
    public static List<string> ListAllFiles()
    {
        var all = new List<string>();
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (string path in ListImportedFiles())
        {
            if (seen.Add(DisplayName(path))) all.Add(path);
        }
        foreach (string path in ListBundledFiles())
        {
            if (seen.Add(DisplayName(path))) all.Add(path);
        }
        return all;
    }

    /// <summary>取り込み先ディレクトリを用意する(Swift側がここへコピーする)。</summary>
    public static bool EnsureImportedDirectory()
    {
        try
        {
            System.IO.Directory.CreateDirectory(ImportedDirectory);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[VRM] 取り込み先を作成できません: {e.Message}");
            return false;
        }
    }

    private static List<string> ListIn(string dir)
    {
        var found = new List<string>();

        try
        {
            if (!System.IO.Directory.Exists(dir)) return found;

            string[] files = System.IO.Directory.GetFiles(dir, "*.vrm");
            System.Array.Sort(files, System.StringComparer.OrdinalIgnoreCase);
            found.AddRange(files);
        }
        catch (System.Exception e)
        {
            // 一覧が作れないこと自体で走行を止めない。既定アバターで動き続ける
            Debug.LogWarning($"[VRM] アバターの一覧に失敗 ({dir}): {e.Message}");
        }

        return found;
    }

    /// <summary>表示用の名前(拡張子を除いたファイル名)。</summary>
    public static string DisplayName(string path)
        => string.IsNullOrEmpty(path) ? "" : Path.GetFileNameWithoutExtension(path);
}
