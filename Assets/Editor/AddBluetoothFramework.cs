using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

public class AddBluetoothFramework
{
    [PostProcessBuild]
    public static void OnPostProcessBuild(BuildTarget buildTarget, string pathToBuiltProject)
    {
        if (buildTarget != BuildTarget.iOS) return;

        string projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        PBXProject proj = new PBXProject();
        proj.ReadFromString(File.ReadAllText(projPath));

        // FIX: Changed GetFrameworkTargetGuid() to the correct API call: GetUnityFrameworkTargetGuid()
        string targetGuid = proj.GetUnityFrameworkTargetGuid();
        string mainTargetGuid = proj.GetUnityMainTargetGuid();

        // Automatically links CoreBluetooth so Xcode stops throwing undefined symbols
        proj.AddFrameworkToProject(targetGuid, "CoreBluetooth.framework", false);

        // ARVisionSensorTiming.mm (第1期PoCの計測基盤) が要求するフレームワーク。
        // CoreMotion  = 100Hz IMU (CMMotionManager)
        // QuartzCore  = 提示予定時刻 (CADisplayLink / CACurrentMediaTime)
        // Unityが暗黙にリンクする構成もあるが、依存を明示しないと
        // 「実機ビルドのときだけ undefined symbol」になり原因を追いにくい
        proj.AddFrameworkToProject(targetGuid, "CoreMotion.framework", false);
        proj.AddFrameworkToProject(targetGuid, "QuartzCore.framework", false);

        // Unity as a Library loads global-metadata.dat relative to
        // UnityFramework.framework/Data. A fresh Unity export assigns Data to
        // the standalone Unity-iPhone app target, which is not built by the
        // SwiftUI host workspace. Move the folder reference to UnityFramework
        // so the IL2CPP metadata is embedded in the framework every time.
        string dataGuid = proj.FindFileGuidByProjectPath("Data");
        if (string.IsNullOrEmpty(dataGuid))
            throw new BuildFailedException("Generated Xcode project is missing its Data folder reference.");

        proj.RemoveFileFromBuild(mainTargetGuid, dataGuid);
        proj.AddFileToBuild(targetGuid, dataGuid);

        File.WriteAllText(projPath, proj.WriteToString());
        Debug.Log("[BUILD] UnityFrameworkへiOS frameworksとDataフォルダを設定しました。");
    }
}
