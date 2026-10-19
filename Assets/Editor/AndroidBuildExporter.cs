#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Android のテスト用 APK を作る(第1期のスコープ外 — 動作確認用。詳細は Docs/ANDROID_BUILD.md)。
/// メニュー「Build → Android test APK (Builds/Android)」またはバッチモード:
///
///   Unity -batchmode -quit -buildTarget Android -projectPath &lt;repo&gt;
///         -executeMethod AndroidBuildExporter.BuildTestApk -logFile android-build.log
///
/// 出力は Builds/Android/ARPacesetter-test.apk(git 管理外)。Development ビルドなので
/// `adb logcat -s Unity` に Debug.Log が出る。Unity のアクティブなプラットフォームが Android に切り替わる
/// (次の iOS エクスポートは -buildTarget iOS で戻る — アセットの再インポートに時間がかかる)。
/// </summary>
public static class AndroidBuildExporter
{
    private const string RelativeOutput = "Builds/Android/ARPacesetter-test.apk";

    [MenuItem("Build/Android test APK (Builds/Android)")]
    public static void BuildTestApk()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string apk = Path.Combine(root, RelativeOutput);
        Directory.CreateDirectory(Path.GetDirectoryName(apk));

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) scenes = new[] { "Assets/Scenes/SampleScene.unity" };

        EditorUserBuildSettings.buildAppBundle = false;

        // iOS エクスポートと同じく、ビルド後処理が preloadedAssets を空にしても元へ戻す
        Object[] preloadedBefore = PlayerSettings.GetPreloadedAssets();

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = apk,
            target = BuildTarget.Android,
            options = BuildOptions.Development,
        });

        if (!preloadedBefore.SequenceEqual(PlayerSettings.GetPreloadedAssets()))
        {
            PlayerSettings.SetPreloadedAssets(preloadedBefore);
            AssetDatabase.SaveAssets();
        }

        var s = report.summary;
        if (s.result == BuildResult.Succeeded)
        {
            Debug.Log($"[ANDROID BUILD] 成功 → {apk} ({s.totalSize / (1024 * 1024)}MB, {s.totalTime})");
        }
        else
        {
            Debug.LogError($"[ANDROID BUILD] {s.result} — エラー {s.totalErrors}件(Docs/ANDROID_BUILD.md のよくある失敗を参照)");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}

/// <summary>
/// Unity 6000.3 の Android Gradle Plugin 9.0 は、同じ namespace を持つライブラリが2つあるとビルドを止める。
/// ARCore パッケージ 6.4.2 自身に同梱の arcore_client と unityandroidpermissions がどちらも
/// com.google.ar.core のため、AGP の暫定フラグ android.uniquePackageNames=false を生成された
/// Gradle プロジェクトへ足す。ARCore パッケージ側で直れば消してよい(AGP の将来版でフラグが消える可能性がある)。
/// Android ビルドのときだけ呼ばれる。
/// </summary>
public class ArCoreNamespaceGradleFix : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 999;

    public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
    {
        string props = Path.Combine(Directory.GetParent(unityLibraryPath).FullName, "gradle.properties");
        if (File.Exists(props) && !File.ReadAllText(props).Contains("android.uniquePackageNames"))
        {
            File.AppendAllText(props, "\nandroid.uniquePackageNames=false\n");
            Debug.Log($"[ANDROID BUILD] android.uniquePackageNames=false → {props}");
        }
    }
}
#endif
