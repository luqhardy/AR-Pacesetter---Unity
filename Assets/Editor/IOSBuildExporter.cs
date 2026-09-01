using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// モノレポ用 iOS エクスポート。
/// メニュー「Build → Export iOS (ios/UnityExport)」で、ワークスペース
/// ios/ARRunner.xcworkspace が参照する固定パスへ Unity-iPhone.xcodeproj を書き出す。
/// Windows でも実行可能（Xcodeビルド自体はMacで行う）。
/// </summary>
public static class IOSBuildExporter
{
    private const string RelativeOutputPath = "ios/UnityExport";

    [MenuItem("Build/Export iOS (ios/UnityExport)")]
    public static void ExportIOS()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputPath = Path.Combine(projectRoot, RelativeOutputPath);

        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();
        if (scenes.Length == 0)
            scenes = new[] { "Assets/Scenes/SampleScene.unity" };

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.iOS,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"[iOS EXPORT] 成功 → {outputPath}\n" +
                      "次: MacでiOS/ARRunner.xcworkspaceを開き、AR_Runner_UIスキームでビルド。");
        }
        else
        {
            Debug.LogError($"[iOS EXPORT] {summary.result} — エラー {summary.totalErrors}件。" +
                           "iOS Build Supportモジュールがインストール済みか確認してください。");

            // バッチモードでは終了コードで失敗を伝える。これが無いと -quit が 0 を返し、
            // CIもシェルもエクスポート失敗を検知できない(実際に Microphone Usage
            // Description 未設定で失敗していたのを長らく取りこぼしていた)
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }
}
