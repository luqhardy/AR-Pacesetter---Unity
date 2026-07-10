using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// E2Eシナリオ検証のエントリポイント。
///  - エディタ: メニュー「Build → Run E2E Scenario」
///  - CLI: Unity.exe -batchmode -projectPath <path> -executeMethod E2EScenarioRunner.Run -logFile <log>
/// 実処理は E2EScenarioBehaviour (Play Mode内) が行う。
/// </summary>
public static class E2EScenarioRunner
{
    [MenuItem("Build/Run E2E Scenario")]
    public static void Run()
    {
        E2EScenarioBehaviour.RequestRun();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.isPlaying = true;
    }
}
