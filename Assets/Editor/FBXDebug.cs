using UnityEngine;
using UnityEditor;
using System.IO;

public static class FBXDebug
{
    public static void LogClips()
    {
        string[] paths = {
            "Assets/Y Bot@Idle.fbx",
            "Assets/Y Bot@Jogging.fbx",
            "Assets/Y Bot@Running.fbx",
            "Assets/Y Bot@Running2.fbx"
        };
        
        string output = "";
        
        foreach (string path in paths)
        {
            output += "Path: " + path + "\n";
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var asset in allAssets)
            {
                if (asset is AnimationClip clip)
                {
                    output += "  Clip: '" + clip.name + "' | isLooping: " + clip.isLooping + "\n";
                }
            }
        }
        
        File.WriteAllText("fbx_clips.txt", output);
        Debug.Log("Wrote fbx_clips.txt");
    }
}
