using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// エディタ専用: アバター(Y Bot)が走る姿を連番 PNG に書き出す(展示用ループ動画の素材)。
///
/// <para>走者の目線(高さ 1.6m・水平)から 3.0m 先のアバターを後ろから見た絵を、動画の画面の一部だけ切り出す
/// 斜めの視錐台で描く。背景は黒 — 動画側では加算合成するので黒は透ける(グラスの見え方と同じ)。
/// 同じ姿勢をペースの4色(緑・橙・赤・青 = AvatarVisualsAndActions の色)で描き、動画側で距離に応じて混ぜる。</para>
///
/// <para>入口は Build → Capture Avatar Run Clip。CLI:
/// Unity.exe -batchmode -projectPath &lt;repo&gt; -executeMethod AvatarClipCapture.Run -logFile capture.log
///   [-captureOut &lt;dir&gt;] [-captureSeconds 15.2]
/// 既定の出力は Builds/AvatarClip/(git 管理外)。開いているシーンは保存せず、空のシーンに置き換えて描く。</para>
/// </summary>
public static class AvatarClipCapture
{
    // 動画(1920×1080)のカメラ: 焦点距離 460px・地平線 y=380 — Builds/KobeCallingLoop/.tesseract-work/build.py と同じ値
    private const float FocalPx = 460f, HorizonY = 380f, CenterX = 960f, EyeHeight = 1.6f, Distance = 3.0f;
    // 3.0m 先のアバターが収まる画面の範囲(px)と、それを描く解像度(近づいて約2.7倍に拡大しても粗くならない大きさ)
    private const float CropX0 = 820f, CropX1 = 1100f, CropY0 = 372f, CropY1 = 652f;
    private const int Size = 1120;
    private const float Fps = 30f;
    private const float HeightMeters = 1.45f;   // 展示デモの身長(BoothDemoScript.AvatarHeightCm)

    private static readonly (string name, Color color)[] Variants =
    {
        ("green", new Color(0.15f, 1.0f, 0.35f)),
        ("orange", new Color(1.0f, 0.55f, 0.0f)),
        ("red", new Color(1.0f, 0.12f, 0.10f)),
        ("blue", new Color(0.10f, 0.45f, 1.0f)),
    };

    [MenuItem("Build/Capture Avatar Run Clip")]
    public static void Run()
    {
        int code = 0;
        try
        {
            Capture(Arg("-captureOut", Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "AvatarClip"))),
                    float.Parse(Arg("-captureSeconds", "15.2"), CultureInfo.InvariantCulture));
        }
        catch (System.Exception e)
        {
            Debug.LogError("[AVATAR CLIP] 失敗: " + e);
            code = 1;
        }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    private static string Arg(string name, string fallback)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return fallback;
    }

    private static void Capture(string outDir, float seconds)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.07f, 0.07f, 0.08f);
        RenderSettings.skybox = null;

        // アバター: 足元が (0,0,3)、進行方向(+Z)を向く = 走者からは背中が見える
        var model = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Y Bot.fbx"));
        Animator animator = model.GetComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Transform headTop = FindDeep(model.transform, "mixamorig:HeadTop_End");
        float unitHeight = headTop != null ? headTop.position.y : 1.8f;   // T ポーズの頭頂 = 素の身長
        model.transform.localScale = Vector3.one * (HeightMeters / unitHeight);
        model.transform.position = new Vector3(0f, 0f, Distance);
        Debug.Log($"[AVATAR CLIP] 素の身長 {unitHeight:F3}m → {HeightMeters}m");

        // マテリアル: 体は色の付いた発光、関節は白寄り(Y Bot の2メッシュ = Alpha_Surface / Alpha_Joints)
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        var surface = new Material(lit) { name = "Capture Surface" };
        var joints = new Material(lit) { name = "Capture Joints" };
        foreach (Material m in new[] { surface, joints })
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetFloat("_Smoothness", 0.12f);
        }
        foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = r.name.Contains("Joint") ? joints : surface;
            r.sharedMaterials = mats;
            r.updateWhenOffscreen = true;
        }

        // 光: 背中を照らす主光(カメラ側の上から)と、向こう側から縁を立てる逆光
        Light key = new GameObject("Key").AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.4f;
        key.transform.rotation = Quaternion.Euler(40f, 35f, 0f);
        Light rim = new GameObject("Rim").AddComponent<Light>();
        rim.type = LightType.Directional;
        rim.intensity = 2.2f;
        rim.transform.rotation = Quaternion.Euler(12f, 195f, 0f);

        // カメラ: 目線の高さで水平。動画の画面のうち Crop の範囲だけを描く斜めの視錐台
        Camera cam = new GameObject("Capture Camera").AddComponent<Camera>();
        cam.transform.position = new Vector3(0f, EyeHeight, 0f);
        cam.transform.rotation = Quaternion.identity;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.allowHDR = false;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 50f;
        float n = cam.nearClipPlane;
        cam.projectionMatrix = Matrix4x4.Frustum(
            (CropX0 - CenterX) / FocalPx * n, (CropX1 - CenterX) / FocalPx * n,
            (HorizonY - CropY1) / FocalPx * n, (HorizonY - CropY0) / FocalPx * n,
            n, cam.farClipPlane);
        var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;
        var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);

        // 走りのアニメーション(アプリの Run 状態と同じ Y Bot@Running)を時刻指定で評価する
        AnimationClip clip = LoadClip("Assets/Y Bot@Running.fbx");
        PlayableGraph graph = PlayableGraph.Create("AvatarClipCapture");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(true);
        AnimationPlayableOutput.Create(graph, "Animation", animator).SetSourcePlayable(playable);
        graph.Play();

        var dirs = new Dictionary<string, string>();
        foreach (var v in Variants)
        {
            dirs[v.name] = Path.Combine(outDir, v.name);
            Directory.CreateDirectory(dirs[v.name]);
        }

        int frames = Mathf.RoundToInt(seconds * Fps);
        Debug.Log($"[AVATAR CLIP] {clip.name} {clip.length:F3}s を {frames} フレーム × {Variants.Length} 色 → {outDir}");
        for (int f = 0; f < frames; f++)
        {
            playable.SetTime(f / Fps % clip.length);
            graph.Evaluate();
            if (f == 0)
            {
                // 最初の1回はシェーダーの準備で色が反映されない(緑の 0 フレーム目が青く出た)ので捨てる。
                // 姿勢を評価する前に描くとアニメーションが付かず T ポーズのままになったので、評価の後に描く
                SetColor(surface, joints, Variants[0].color);
                cam.Render();
            }
            foreach (var v in Variants)
            {
                SetColor(surface, joints, v.color);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dirs[v.name], $"f_{f:D4}.png"), tex.EncodeToPNG());
            }
        }
        graph.Destroy();
        Debug.Log("[AVATAR CLIP] 書き出し完了");
    }

    /// <summary>体は色そのもの(陰影は光で付く・発光は控えめ)、関節は白寄りで少し明るく</summary>
    private static void SetColor(Material surface, Material joints, Color color)
    {
        surface.SetColor("_BaseColor", color * 0.7f);
        surface.SetColor("_EmissionColor", color * 0.42f);
        Color joint = Color.Lerp(color, Color.white, 0.55f);
        joints.SetColor("_BaseColor", joint * 0.6f);
        joints.SetColor("_EmissionColor", joint * 0.65f);
    }

    private static AnimationClip LoadClip(string path)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
        throw new FileNotFoundException("AnimationClip が無い: " + path);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform hit = FindDeep(child, name);
            if (hit != null) return hit;
        }
        return null;
    }
}
