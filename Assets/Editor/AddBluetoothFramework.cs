using UnityEngine;
using UnityEditor;
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

        // Automatically links CoreBluetooth so Xcode stops throwing undefined symbols
        proj.AddFrameworkToProject(targetGuid, "CoreBluetooth.framework", false);

        // ARVisionSensorTiming.mm (第1期PoCの計測基盤) が要求するフレームワーク。
        // CoreMotion  = 100Hz IMU (CMMotionManager)
        // QuartzCore  = 提示予定時刻 (CADisplayLink / CACurrentMediaTime)
        // Unityが暗黙にリンクする構成もあるが、依存を明示しないと
        // 「実機ビルドのときだけ undefined symbol」になり原因を追いにくい
        proj.AddFrameworkToProject(targetGuid, "CoreMotion.framework", false);
        proj.AddFrameworkToProject(targetGuid, "QuartzCore.framework", false);

        File.WriteAllText(projPath, proj.WriteToString());
        Debug.Log("[BUILD] CoreBluetooth / CoreMotion / QuartzCore を Xcode の UnityFramework ターゲットへ注入しました。");
    }
}