using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

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

    private Camera _outputCamera;
    private ARCameraManager _cameraManager;
    private Material _blitMaterial;
    private RenderTexture _texture;
    private Canvas _canvas;
    private RawImage _image;
    private float _cameraProjectionM11;

    /// <summary>グラスへ映像を出す指定になっているか(フレームが届く前でも true)。</summary>
    public bool IsShowing { get; private set; }

    /// <summary>今の表示中にカメラのフレームを1枚以上描いたか(エディタでは届かないので false のまま)。</summary>
    public bool HasFrame { get; private set; }

    /// <summary>直近に使った切り出しの割合(1 = 全面)。</summary>
    public float UvScale { get; private set; } = 1f;

    public void Initialize(Camera outputCamera, Camera arCamera)
    {
        _outputCamera = outputCamera;
        _cameraManager = arCamera != null ? arCamera.GetComponent<ARCameraManager>() : null;
        if (_cameraManager == null)
            _cameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
        if (_cameraManager != null)
            _cameraManager.frameReceived += OnCameraFrameReceived;
        else
            Debug.LogWarning("[GLASS] ARCameraManager が見つからないためグラスへカメラ映像を出せません。");

        BuildCanvas();
    }

    /// <summary>グラスへ映像を出すかどうか。止めると次に出すまで最後のフレームも見せない。</summary>
    public void SetShowing(bool show)
    {
        IsShowing = show;
        HasFrame = false;
        if (_canvas != null) _canvas.enabled = show;
        if (_image != null) _image.enabled = false; // 最初のフレームが届いてから出す
    }

    void OnDestroy()
    {
        if (_cameraManager != null) _cameraManager.frameReceived -= OnCameraFrameReceived;
        if (_texture != null) { _texture.Release(); Destroy(_texture); }
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

    private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        if (!IsShowing || args.textures.Count == 0) return;
        if (!EnsureMaterial()) return;

        for (int i = 0; i < args.textures.Count; i++)
            _blitMaterial.SetTexture(args.propertyNameIds[i], args.textures[i]);
        if (args.displayMatrix.HasValue)
            _blitMaterial.SetMatrix(DisplayTransformId, args.displayMatrix.Value);
        ApplyKeywords(args);
        if (args.projectionMatrix.HasValue)
            _cameraProjectionM11 = args.projectionMatrix.Value[1, 1];

        EnsureTexture();
        Graphics.Blit(null, _texture, _blitMaterial);

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
        _texture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = "GlassPassthrough",
            wrapMode = TextureWrapMode.Clamp,
        };
        _texture.Create();
        _image.texture = _texture;
    }

    private static int FirstLayerIn(int mask)
    {
        if ((mask & 1) != 0) return 0;
        for (int i = 1; i < 32; i++)
            if ((mask & (1 << i)) != 0) return i;
        return 0;
    }
}
