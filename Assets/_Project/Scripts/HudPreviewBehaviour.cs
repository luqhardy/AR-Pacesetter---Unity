#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// エディタ専用: グラスに映る HUD を画像に書き出す(デザインの確認用)。ビルドには含まれない。
///
/// <para>XREAL One は光学シースルー(加算表示)なので、黒は透けて見えない。書き出しは2種類:
///   *_glass.png       … グラスが出す光そのもの(黒い所は透明)
///   *_seethrough.png  … 晴れたトラックに重ねたときの見え方の近似(グラスの減光 40% + HUD の光を足す)</para>
///
/// <para>入口は Build → Capture HUD Preview(CLI: -executeMethod HudPreviewRunner.Run)。
/// 状態は E2E と同じ公開 API で作る(走行開始・実測ペース・GPS喪失・スプリット)。
/// 出力は Builds/HudPreview/(git 管理外)。</para>
/// </summary>
public class HudPreviewBehaviour : MonoBehaviour
{
    private const string SessionFlag = "HudPreview.Run";
    private const int Width = 1920, Height = 1080;          // XREAL One の1眼
    private const float TargetPaceKmH = 13.0f;              // 4'37"/km
    private const float SeeThroughTransmittance = 0.40f;    // グラスの減光(晴天の屋外を想定)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (!SessionState.GetBool(SessionFlag, false)) return;
        SessionState.SetBool(SessionFlag, false);
        var go = new GameObject("[HUD Preview]");
        go.AddComponent<HudPreviewBehaviour>();
    }

    public static void RequestRun() => SessionState.SetBool(SessionFlag, true);

    private string _outDir;
    private ARSessionManagerBridge _bridge;

    private IEnumerator Start()
    {
        _outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "HudPreview"));
        Directory.CreateDirectory(_outDir);

        // ブートストラップ(マネージャーの自動生成)を待つ
        for (int i = 0; i < 30 && _bridge == null; i++)
        {
            _bridge = FindFirstObjectByType<ARSessionManagerBridge>(FindObjectsInactive.Include);
            yield return null;
        }
        var hud = FindFirstObjectByType<PeripheralHUDManager>(FindObjectsInactive.Include);
        var engine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        var state = FindFirstObjectByType<GameStateController>(FindObjectsInactive.Include);
        var analytics = FindFirstObjectByType<AnalyticsManager>(FindObjectsInactive.Include);
        if (_bridge == null || hud == null || engine == null)
        {
            Debug.LogError("[HUD PREVIEW] bridge / HUD / engine が見つからない");
            EditorApplication.Exit(1);
            yield break;
        }

        // グラス出力中と同じ HUD(Unity が HUD を持つ・セーフエリア 90%)
        _bridge.OnSwiftCommand(
            "{\"command\":\"StartSession\",\"targetPaceKmH\":" + TargetPaceKmH.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"distanceKm\":5.0,\"avatarHeightCm\":175,\"forwardOffsetM\":3.0,\"hideUnityHud\":false}");
        hud.ApplyEdgeInsetFraction(0.90f);

        // 3-2-1-START を待つ
        for (float t = 0; t < 8f && !engine.IsRunMotionActive; t += Time.deltaTime) yield return null;
        hud.PresentationDistanceMeters = 2345f;   // 表示用の距離(2.35 km)

        // 1. ペースを維持(緑)
        yield return FeedPace(TargetPaceKmH, 1.5f);
        Capture("1_on_pace");

        // 2. 遅れ(赤)
        yield return FeedPace(TargetPaceKmH * 0.78f, 1.5f);
        Capture("2_behind");

        // 3. スプリット通知(1km)
        if (analytics != null)
        {
            analytics.CheckDistanceIntervalSplits(1000.5f);
            yield return FeedPace(TargetPaceKmH, 0.8f);
            Capture("3_split");
            yield return FeedPace(TargetPaceKmH, 3.2f); // 通知が消えるまで
        }

        // 4. GPS 喪失(F-10 の警告)
        if (state != null)
        {
            // 測位を流し続けると GPS が戻った扱いになるので、ここからは流さない
            state.TransitionToState(GameStateController.ARVisionState.InertialMovement);
            yield return new WaitForSeconds(0.6f);
            LogText("SafetyWarning");
            Capture("4_gps_lost");
        }

        Debug.Log($"[HUD PREVIEW] 書き出し完了: {_outDir}");
        EditorApplication.Exit(0);
    }

    /// <summary>文字が本当に描かれているか(有効か・どのキャンバスか・位置・フォント・描いた文字数)をログに出す</summary>
    private static void LogText(string objectName)
    {
        foreach (var t in FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name != objectName) continue;
            t.ForceMeshUpdate();
            int visible = 0;
            for (int i = 0; i < t.textInfo.characterCount; i++)
                if (t.textInfo.characterInfo[i].isVisible) visible++;
            Canvas c = t.canvas;
            Debug.Log($"[HUD PREVIEW] text '{objectName}': active={t.gameObject.activeInHierarchy} canvas='{(c != null ? c.rootCanvas.name : "-")}' " +
                      $"pos={t.rectTransform.position} anchored={t.rectTransform.anchoredPosition} size={t.rectTransform.rect.size} " +
                      $"font='{(t.font != null ? t.font.name : "-")}' color={t.color} chars={t.textInfo.characterCount} visibleChars={visible} text='{t.text}'");
        }
    }

    /// <summary>実測ペースを Swift と同じ経路(UpdateMetrics)で流し続ける</summary>
    private IEnumerator FeedPace(float kmh, float seconds)
    {
        string json = "{\"command\":\"UpdateMetrics\",\"paceKmH\":" + kmh.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                      ",\"heartRate\":0,\"distanceKm\":0.3,\"locationSampleFresh\":true,\"speedSampleValid\":true}";
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            _bridge.OnSwiftCommand(json);
            yield return null;
        }
    }

    private void Capture(string name)
    {
        Texture2D hud = RenderCanvases();
        File.WriteAllBytes(Path.Combine(_outDir, name + "_glass.png"), hud.EncodeToPNG());

        Texture2D see = SeeThrough(hud);
        File.WriteAllBytes(Path.Combine(_outDir, name + "_seethrough.png"), see.EncodeToPNG());
        Destroy(hud);
        Destroy(see);
        Debug.Log($"[HUD PREVIEW] {name}");
    }

    /// <summary>画面に直接描く(Overlay)キャンバスを一時的にカメラ描画へ切り替えて RenderTexture に描く</summary>
    private Texture2D RenderCanvases()
    {
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        var camGo = new GameObject("[HUD Capture Camera]");
        camGo.transform.position = new Vector3(0f, -5000f, 0f); // シーンの物が写り込まない所
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;                      // グラスでは黒 = 透明
        cam.cullingMask = ~0;                                   // シーンから離れた所にいるので HUD だけが写る
        cam.orthographic = true;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 10f;
        cam.targetTexture = rt;
        cam.enabled = false;

        var switched = new List<Canvas>();
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay || !c.gameObject.activeInHierarchy) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = 1f;
            switched.Add(c);
            Debug.Log($"[HUD PREVIEW] canvas '{c.name}' layer={LayerMask.LayerToName(c.gameObject.layer)} children={c.transform.childCount}");
        }
        Canvas.ForceUpdateCanvases();
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        foreach (Canvas c in switched)
        {
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.worldCamera = null;
        }
        cam.targetTexture = null;
        Destroy(camGo);
        rt.Release();
        Destroy(rt);
        return tex;
    }

    /// <summary>晴れたトラック(空 + タータン + レーンの白線)に HUD の光を足した近似</summary>
    private static Texture2D SeeThrough(Texture2D hud)
    {
        var outTex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        Color[] h = hud.GetPixels();
        var px = new Color[h.Length];
        float horizon = Height * 0.46f;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Color world;
                if (y > horizon)
                {
                    float t = (y - horizon) / (Height - horizon);              // 0=地平線 1=上端
                    world = Color.Lerp(new Color(0.86f, 0.90f, 0.95f), new Color(0.42f, 0.62f, 0.88f), t);
                }
                else
                {
                    float t = y / horizon;                                     // 0=手前 1=地平線
                    world = Color.Lerp(new Color(0.72f, 0.30f, 0.22f), new Color(0.80f, 0.46f, 0.38f), t);
                    // レーンの白線(消失点へ集まる)
                    float cx = Width * 0.5f;
                    for (int lane = -3; lane <= 3; lane++)
                    {
                        float bottomX = cx + lane * Width * 0.36f;
                        float lx = Mathf.Lerp(bottomX, cx + lane * 40f, t);
                        float halfWidth = Mathf.Lerp(7f, 1f, t);
                        if (Mathf.Abs(x - lx) < halfWidth) world = new Color(0.95f, 0.95f, 0.93f);
                    }
                }
                Color g = h[y * Width + x];
                px[y * Width + x] = new Color(
                    Mathf.Clamp01(world.r * SeeThroughTransmittance + g.r),
                    Mathf.Clamp01(world.g * SeeThroughTransmittance + g.g),
                    Mathf.Clamp01(world.b * SeeThroughTransmittance + g.b));
            }
        }
        outTex.SetPixels(px);
        outTex.Apply();
        return outTex;
    }
}
#endif
