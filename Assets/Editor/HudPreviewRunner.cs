using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// グラスに映る HUD を画像に書き出す(デザインの確認用)。実処理は HudPreviewBehaviour(Play Mode内)。
///  - エディタ: メニュー「Build → Capture HUD Preview」
///  - CLI: Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod HudPreviewRunner.Run -logFile hud-preview.log
/// 出力は Builds/HudPreview/(*_glass.png = グラスが出す光 / *_seethrough.png = トラックに重ねた見え方の近似)
/// </summary>
public static class HudPreviewRunner
{
    [MenuItem("Build/Capture HUD Preview")]
    public static void Run()
    {
        HudPreviewBehaviour.RequestRun();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.isPlaying = true;
    }
}
