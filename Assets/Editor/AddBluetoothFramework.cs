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

        File.WriteAllText(projPath, proj.WriteToString());
        Debug.Log("[BLE BUILDER] CoreBluetooth successfully injected into Xcode project framework target.");
    }
}