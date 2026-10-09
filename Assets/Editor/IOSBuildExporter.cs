using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
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

    // Finder/iCloud-style conflict copies (for example "Foo 2.cpp") must never
    // enter an IL2CPP build. They can compile successfully and then make the
    // metadata and native method tables disagree at runtime.
    private static readonly Regex NumberedCopyPattern = new Regex(
        @"^(?<base>.+) (?<copy>[2-9][0-9]*)(?<suffix>\..+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] GeneratedCacheRoots =
    {
        "Library/Bee/artifacts/iOS",
        "Library/Bee/PlayerScriptAssemblies",
        "Library/BuildPlayerData/Player",
        "Library/PlayerDataCache/iOS",
        "Library/BurstCache/iOS-Arm"
    };

    [MenuItem("Build/Export iOS (ios/UnityExport)")]
    public static void ExportIOS()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputPath = Path.GetFullPath(Path.Combine(projectRoot, RelativeOutputPath));

        EnsureIosIsActiveBuildTarget();
        RemoveConflictedGeneratedCaches(projectRoot);
        PrepareCleanExportDirectory(projectRoot, outputPath);

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
            EnsureNoIl2CppCollisionCopies(outputPath);
            EnsureArKitNativePluginIsPresent(outputPath);

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

    private static void EnsureIosIsActiveBuildTarget()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS)
            return;

        string batchHint = Application.isBatchMode
            ? " Restart Unity with the command-line option '-buildTarget iOS'."
            : " Switch the active platform to iOS in Build Profiles first.";

        throw new BuildFailedException(
            "iOS must be the active build target before exporting. The ARKit package uses the UNITY_IOS " +
            "compile-time define to include its native library." + batchHint);
    }

    private static void RemoveConflictedGeneratedCaches(string projectRoot)
    {
        foreach (string relativeRoot in GeneratedCacheRoots)
        {
            string cacheRoot = Path.GetFullPath(Path.Combine(projectRoot, relativeRoot));
            if (!Directory.Exists(cacheRoot))
                continue;

            string collisionCopy = FindNumberedCollisionCopy(cacheRoot);
            if (collisionCopy == null)
                continue;

            Debug.LogWarning($"[iOS EXPORT] 衝突コピーを検出したため生成キャッシュを再作成します: " +
                             $"{relativeRoot} ({Path.GetFileName(collisionCopy)})");
            Directory.Delete(cacheRoot, true);
        }
    }

    private static void PrepareCleanExportDirectory(string projectRoot, string outputPath)
    {
        string expectedPath = Path.GetFullPath(Path.Combine(projectRoot, RelativeOutputPath));
        if (!string.Equals(outputPath, expectedPath, StringComparison.Ordinal))
            throw new BuildFailedException($"Unexpected iOS export path: {outputPath}");

        // UnityExport is ignored generated output. Incremental export can retain
        // collision copies, so always regenerate this exact directory.
        if (Directory.Exists(outputPath))
            Directory.Delete(outputPath, true);

        Directory.CreateDirectory(outputPath);
    }

    private static void EnsureNoIl2CppCollisionCopies(string outputPath)
    {
        string il2CppRoot = Path.Combine(
            outputPath,
            "Il2CppOutputProject",
            "Source",
            "il2cppOutput");

        string collisionCopy = FindNumberedCollisionCopy(il2CppRoot);
        if (collisionCopy == null)
            return;

        throw new BuildFailedException(
            "Generated IL2CPP output contains a numbered collision copy: " + collisionCopy);
    }

    private static void EnsureArKitNativePluginIsPresent(string outputPath)
    {
        string il2CppRoot = Path.Combine(
            outputPath,
            "Il2CppOutputProject",
            "Source",
            "il2cppOutput");

        bool containsManagedArKit = File.Exists(Path.Combine(il2CppRoot, "Unity.XR.ARKit.cpp"));
        if (!containsManagedArKit)
            return;

        bool containsNativeArKit = Directory
            .EnumerateFiles(outputPath, "*", SearchOption.AllDirectories)
            .Any(candidate =>
            {
                string name = Path.GetFileName(candidate);
                return string.Equals(name, "libUnityARKit.a", StringComparison.Ordinal) ||
                       string.Equals(name, "UnityARKit.m", StringComparison.Ordinal);
            });

        if (!containsNativeArKit)
        {
            throw new BuildFailedException(
                "The export contains managed ARKit code but no native ARKit plug-in. " +
                "Make iOS the active target (batch mode: add '-buildTarget iOS') and export again.");
        }
    }

    private static string FindNumberedCollisionCopy(string root)
    {
        if (!Directory.Exists(root))
            return null;

        foreach (string candidate in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(candidate);
            Match match = NumberedCopyPattern.Match(fileName);
            if (!match.Success)
                continue;

            string canonicalName = match.Groups["base"].Value + match.Groups["suffix"].Value;
            string canonicalPath = Path.Combine(Path.GetDirectoryName(candidate) ?? root, canonicalName);
            if (File.Exists(canonicalPath))
                return candidate;
        }

        return null;
    }
}
