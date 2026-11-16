using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// ARグラスへ出す映像の背景に、iPhone のカメラ映像(パススルー)を描く。
/// <see cref="GlassViewRig"/> が出力カメラに付けて使う(既定ON・開発者モードで切替)。
///
/// <para><b>なぜ ARCameraBackground を使わないか</b>: ARCameraBackground は自分の付いたカメラ
/// (= ARKitカメラ)にしか描けない。グラス出力中の描画は別カメラ(画角・視点がグラス)へ移しているので、
/// 映像はこちらで受け取る。ARKit の YCbCr テクスチャを AR Foundation のカメラ用マテリアルで
/// RenderTexture へ変換し(Unity が案内している方法)、出力カメラの最背面に張る。</para>
///
/// <para><b>画角を揃える</b>: カメラ映像はグラスより広いので、そのまま全面に出すと現実より小さく映る。
/// 中央をグラスの画角ぶんだけ切り出す(<see cref="GlassPassthroughMath"/>)。映像は頭に付けた
/// iPhone から来るので、画面に固定したままで向きは現実と一致する。</para>
///
/// <para>XREAL は光学シースルーで黒は透ける(加算表示)。映像を出すとレンズ越しの現実に重なるので、
/// 見やすさはグラスの調光に依存する。</para>
/// </summary>
public sealed class GlassPassthroughLayer : MonoBehaviour
{
    private static readonly int DisplayTransformId = Shader.PropertyToID("_UnityDisplayTransform");

    // 映像に深度を書かせない — 環境でアバターを隠さない(ARPlaneOcclusionController と同じ方針・§7.7 中央は透過)
    private static readonly string[] OcclusionKeywords =
        { "ARKIT_ENVIRONMENT_DEPTH_ENABLED", "ARKIT_HUMAN_SEGMENTATION_ENABLED" };

    // 帯域を抑えるため描画面の半分の解像度で変換する(グラスの視野中央を約2倍に拡大して見るので足りる)
    private const int TextureDownscale = 2;

    // ARKit の背景シェーダーは深度を書く(ZWrite On・SV_Depth)。深度の無い描画先へ描くと
    // Metal では何も描かれないことがあるので、変換先にも深度バッファを持たせる
    private const int DepthBufferBits = 24;

    // 変換結果が本当に絵になっているかの確認(小さく縮めて読み戻す)。確認できるまで1秒に1回だけ
    private const int ProbeSize = 8;
    private const float ProbeIntervalSeconds = 1f;

    private Camera _outputCamera;
    private ARCameraManager _cameraManager;
    private Material _blitMaterial;
    private RenderTexture _texture;
    private Canvas _canvas;
    private RawImage _image;
    private float _cameraProjectionM11;
    private RenderTexture _probe;
    private ARCameraBackground _arBackground;
    private CommandBuffer _releaseCommands;

    // ARKit はフレームごとにカメラの Metal テクスチャを掴み、「背景を描く直前」の合図で手放す
    // (ARKitCameraSubsystem.OnBeforeBackgroundRender)。合図を出すのは ARCameraBackground の描画だけで、
    // グラス出力中はそれを止めているので誰も出さない — 掴んだままだと ARKit がカメラのフレームを
    // 配らなくなる疑いがある(映像もトラッキングも止まる)。AR Foundation と同じ形で自分で出す。
    // 描画スレッドから呼ばれるので static のデリゲートを関数ポインタにする(IL2CPP は MonoPInvokeCallback が要る)
    private static XRCameraSubsystem s_ReleaseSubsystem;
    private static readonly Action<int> s_ReleaseHandler = ReleaseCameraBuffers;
    private static readonly IntPtr s_ReleaseHandlerPtr = Marshal.GetFunctionPointerForDelegate(s_ReleaseHandler);

    [AOT.MonoPInvokeCallback(typeof(Action<int>))]
    private static void ReleaseCameraBuffers(int eventId) => s_ReleaseSubsystem?.OnBeforeBackgroundRender(eventId);

    /// <summary>カメラのテクスチャを手放す合図を自分で出した回数(診断用)。</summary>
    public int CameraBufferReleaseCount { get; private set; }

    private System.Action<AsyncGPUReadbackRequest> _onProbe;
    private bool _probePending;
    private float _nextProbeTime;

    /// <summary>グラスへ映像を出す指定になっているか(フレームが届く前でも true)。</summary>
    public bool IsShowing { get; private set; }

    /// <summary>
    /// 今の表示中にカメラのフレームを1枚以上<b>変換にかけた</b>か(エディタでは届かないので false のまま)。
    /// 変換を実行したことしか言わない — 絵が出たかは <see cref="HasVisiblePixels"/>
    /// </summary>
    public bool HasFrame { get; private set; }

    /// <summary>変換結果を読み戻して、黒でない画素を確かめたか。</summary>
    public bool HasVisiblePixels { get; private set; }

    /// <summary>今の表示中に変換結果を読み戻した回数(0 なら未確認)。</summary>
    public int PixelProbeCount { get; private set; }

    /// <summary>直近に読み戻した画素の最大の明るさ(0〜255)。</summary>
    public int LastProbePeak { get; private set; }

    /// <summary>直近に使った切り出しの割合(1 = 全面)。</summary>
    public float UvScale { get; private set; } = 1f;

    public void Initialize(Camera outputCamera, Camera arCamera)
    {
        _outputCamera = outputCamera;
        _cameraManager = arCamera != null ? arCamera.GetComponent<ARCameraManager>() : null;
        if (_cameraManager == null)
            _cameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
        if (_cameraManager != null)
        {
            _arBackground = _cameraManager.GetComponent<ARCameraBackground>();
            _cameraManager.frameReceived += OnCameraFrameReceived;
        }
        else
            Debug.LogWarning("[GLASS] ARCameraManager が見つからないためグラスへカメラ映像を出せません。");

        BuildCanvas();
    }

    /// <summary>グラスへ映像を出すかどうか。止めると次に出すまで最後のフレームも見せない。</summary>
    public void SetShowing(bool show)
    {
        IsShowing = show;
        HasFrame = false;
        HasVisiblePixels = false;
        PixelProbeCount = 0;
        LastProbePeak = 0;
        _nextProbeTime = 0f;
        if (_canvas != null) _canvas.enabled = show;
        if (_image != null) _image.enabled = false; // 最初のフレームが届いてから出す
    }

    void OnDestroy()
    {
        if (_cameraManager != null) _cameraManager.frameReceived -= OnCameraFrameReceived;
        if (_texture != null) { _texture.Release(); Destroy(_texture); }
        if (_probe != null) { _probe.Release(); Destroy(_probe); }
        if (_releaseCommands != null) _releaseCommands.Release();
        if (_blitMaterial != null) Destroy(_blitMaterial);
    }

    private void BuildCanvas()
    {
        var go = new GameObject("GlassPassthroughCanvas");
        go.transform.SetParent(transform, false);
        go.layer = FirstLayerIn(_outputCamera != null ? _outputCamera.cullingMask : ~0);

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
        _canvas.worldCamera = _outputCamera;
        // 奥に置き、アバター(既定の並び順 0)より先に描く
        _canvas.planeDistance = _outputCamera != null ? Mathf.Min(_outputCamera.farClipPlane * 0.9f, 100f) : 100f;
        _canvas.sortingOrder = -1000;
        _canvas.enabled = false;

        var imageGo = new GameObject("CameraImage");
        imageGo.layer = go.layer;
        imageGo.transform.SetParent(go.transform, false);
        _image = imageGo.AddComponent<RawImage>();
        _image.raycastTarget = false;
        _image.enabled = false;
        var rect = _image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>ARKit からフレームの通知を受けた回数(表示の指定に関係なく数える)。診断用</summary>
    public int FrameEventCount { get; private set; }

    /// <summary>
    /// 映像がどこで止まっているか(日本語・診断用)。描けていれば空。
    /// 実機で「設定はONなのに映らない」とき、開発者モードの診断でどの段かを読む
    /// </summary>
    public string Blocker
    {
        get
        {
            if (!IsShowing) return "";
            if (HasVisiblePixels) return "";
            if (HasFrame)
            {
                // 変換は実行している。読み戻しがまだ・できない間は止まっているとは言わない
                if (PixelProbeCount == 0) return "";
                return $"変換は実行しているが結果が黒い(最大の明るさ {LastProbePeak}/255)。" +
                       "カメラが覆われていなければ変換の失敗";
            }
            if (_cameraManager == null) return "ARCameraManager が見つからない";
            if (FrameEventCount == 0) return "ARKit のフレームが1枚も届いていない";
            if (_lastFrameHadNoTextures) return "フレームは届くがテクスチャが付いていない";
            if (_blitMaterial == null) return "カメラ用マテリアル(cameraMaterial)が取れない";
            return "フレーム待ち";
        }
    }

    private bool _lastFrameHadNoTextures;

    private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        FrameEventCount++;
        _lastFrameHadNoTextures = args.textures.Count == 0;
        if (!IsShowing || args.textures.Count == 0 || !EnsureMaterial())
        {
            // 映像を出さない設定でも、フレームは毎回掴まれている — 手放す合図は要る
            ScheduleCameraBufferRelease();
            return;
        }

        for (int i = 0; i < args.textures.Count; i++)
            _blitMaterial.SetTexture(args.propertyNameIds[i], args.textures[i]);
        if (args.displayMatrix.HasValue)
            _blitMaterial.SetMatrix(DisplayTransformId, args.displayMatrix.Value);
        ApplyKeywords(args);
        if (args.projectionMatrix.HasValue)
            _cameraProjectionM11 = args.projectionMatrix.Value[1, 1];

        EnsureTexture();
        // 描画先を明示して色と深度を消してから描く(Unity の案内と同じ手順)。
        // 前のフレームが残っていると、変換が失敗しても古い絵で成功に見える
        var previous = RenderTexture.active;
        RenderTexture.active = _texture;
        GL.Clear(true, true, Color.black);
        Graphics.Blit(null, _texture, _blitMaterial);
        RenderTexture.active = previous;
        // 変換の後に出す(描画スレッドでも変換 → 手放しの順で実行される)
        ScheduleCameraBufferRelease();
        ProbePixelsIfDue();

        float fov = _outputCamera != null ? _outputCamera.fieldOfView : 0f;
        UvScale = (float)GlassPassthroughMath.UvCropScale(fov, _cameraProjectionM11);
        float offset = (float)GlassPassthroughMath.CenteredUvOffset(UvScale);
        _image.uvRect = new Rect(offset, offset, UvScale, UvScale);

        if (!HasFrame)
        {
            HasFrame = true;
            _image.enabled = true;
            Debug.Log($"[GLASS] グラスへカメラ映像を表示開始(中央 {UvScale * 100f:F0}% を切り出し・{_texture.width}x{_texture.height})");
        }
    }

    /// <summary>
    /// このフレームのカメラのテクスチャを手放す合図を出す。iPhone の画面へ描いている間
    /// (ARCameraBackground が有効)は AR Foundation が出すので何もしない
    /// </summary>
    private void ScheduleCameraBufferRelease()
    {
        if (_arBackground != null && _arBackground.enabled) return;
        XRCameraSubsystem subsystem = _cameraManager != null ? _cameraManager.subsystem : null;
        if (subsystem == null) return;

        s_ReleaseSubsystem = subsystem;
        if (_releaseCommands == null)
        {
            _releaseCommands = new CommandBuffer { name = "GlassPassthroughReleaseCameraBuffers" };
            _releaseCommands.IssuePluginEvent(s_ReleaseHandlerPtr, 0);
        }
        Graphics.ExecuteCommandBuffer(_releaseCommands);

        if (CameraBufferReleaseCount++ == 0)
            Debug.Log("[GLASS] カメラのテクスチャを手放す合図を自分で出し始めた(ARCameraBackground が止まっているため)");
    }

    private bool EnsureMaterial()
    {
        if (_blitMaterial != null) return true;
        Material source = _cameraManager != null ? _cameraManager.cameraMaterial : null;
        if (source == null) return false;
        // ARKitカメラの ARCameraBackground と共有しない(iPhone表示へ戻したときに設定を奪い合わない)
        _blitMaterial = new Material(source) { name = "GlassPassthroughBlit" };
        return true;
    }

    private void ApplyKeywords(ARCameraFrameEventArgs args)
    {
        var keywords = args.shaderKeywords;
        if (keywords.enabledKeywords != null)
            foreach (var k in keywords.enabledKeywords) _blitMaterial.EnableKeyword(k);
        if (keywords.disabledKeywords != null)
            foreach (var k in keywords.disabledKeywords) _blitMaterial.DisableKeyword(k);
        foreach (var k in OcclusionKeywords) _blitMaterial.DisableKeyword(k);
    }

    // displayMatrix は描画面の縦横比で切り出されているので、変換先も同じ縦横比にする
    private void EnsureTexture()
    {
        int w = Mathf.Max(2, Screen.width / TextureDownscale);
        int h = Mathf.Max(2, Screen.height / TextureDownscale);
        if (_texture != null && _texture.width == w && _texture.height == h) return;

        if (_texture != null) { _texture.Release(); Destroy(_texture); }
        _texture = new RenderTexture(w, h, DepthBufferBits, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = "GlassPassthrough",
            wrapMode = TextureWrapMode.Clamp,
        };
        _texture.Create();
        _image.texture = _texture;
    }

    /// <summary>
    /// 変換結果を 8x8 に縮めて読み戻し、黒でない画素があるかを確かめる。
    /// 「変換を呼んだ」だけでは絵が出たとは言えない(描けていなくても例外は出ない)。
    /// 確認できたらやめる。走行中のフレーム時間を乱さないよう、同期の読み戻しは使わない
    /// </summary>
    private void ProbePixelsIfDue()
    {
        if (HasVisiblePixels || _probePending) return;
        if (!SystemInfo.supportsAsyncGPUReadback) return;
        if (Time.unscaledTime < _nextProbeTime) return;

        if (_probe == null)
        {
            _probe = new RenderTexture(ProbeSize, ProbeSize, 0, RenderTextureFormat.ARGB32) { name = "GlassPassthroughProbe" };
            _probe.Create();
        }

        var previous = RenderTexture.active;
        Graphics.Blit(_texture, _probe);
        RenderTexture.active = previous;

        _onProbe ??= OnProbeRead;
        _probePending = true;
        _nextProbeTime = Time.unscaledTime + ProbeIntervalSeconds;
        AsyncGPUReadback.Request(_probe, 0, TextureFormat.RGBA32, _onProbe);
    }

    private void OnProbeRead(AsyncGPUReadbackRequest request)
    {
        _probePending = false;
        if (this == null || !IsShowing || request.hasError) return;

        var pixels = request.GetData<Color32>();
        int peak = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 c = pixels[i];
            if (c.r > peak) peak = c.r;
            if (c.g > peak) peak = c.g;
            if (c.b > peak) peak = c.b;
        }

        PixelProbeCount++;
        LastProbePeak = peak;
        if (!GlassPassthroughMath.HasVisiblePixels(peak)) return;

        HasVisiblePixels = true;
        Debug.Log($"[GLASS] カメラ映像の画素を確認(最大の明るさ {peak}/255)");
    }

    private static int FirstLayerIn(int mask)
    {
        if ((mask & 1) != 0) return 0;
        for (int i = 1; i < 32; i++)
            if ((mask & (1 << i)) != 0) return i;
        return 0;
    }
}
