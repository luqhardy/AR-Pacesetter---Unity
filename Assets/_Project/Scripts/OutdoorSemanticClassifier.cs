using UnityEngine;

/// <summary>
/// 3D面分類が埋められない場所を、画像ベースの分類で補う供給元。
/// 実装が無い / 端末が非対応 / まだ推論が来ていない間は <see cref="IsAvailable"/> が false になり、
/// 呼び出し側は従来どおり幾何判定だけで動く。
/// </summary>
public interface IOutdoorSemanticSource
{
    /// <summary>今このフレームで分類を引けるか。</summary>
    bool IsAvailable { get; }

    /// <summary>ワールド座標の点を分類する。引けなければ false(呼び出し側は何も変えない)。</summary>
    bool TryClassify(Vector3 worldPoint, out SurfaceSemantic semantic);
}

/// <summary>
/// ARCore Scene Semantics(屋外向けの画像セグメンテーション)で足元の路面を分類する。
///
/// <para><b>埋めている穴</b>: ARKitの面分類の語彙は屋内語(Floor/Wall/Ceiling/Table/…)で、
/// <b>road が無い</b>。そのため屋外の路面は Unknown のまま落ちてきて、接地判定は
/// 法線と高さという幾何だけで決めるしかなかった(既存コードのコメントにもそう書いてある)。
/// Scene Semantics は Road / Sidewalk / Terrain を直接返すので、そこだけを埋める。</para>
///
/// <para><b>M2P予算(§10)への配慮</b>: 毎フレームML推論を引くと20msの予算を確実に食う。
/// セマンティック画像の取得は既定5Hzに落とし、60fpsの描画経路には載せない。
/// 取得したラベル画像はバイト配列へ1回だけ写し、以降の点問い合わせは配列参照だけで済ませる。</para>
///
/// <para><b>ARCore Extensions が無い環境では完全に休眠する</b>(<c>ARCORE_EXTENSIONS</c> 未定義)。
/// エディタ・E2E・Windows開発はこちらを通り、接地判定は従来と1ミリも変わらない。
/// 導入手順は Docs/ARCORE_SCENE_SEMANTICS.md。</para>
///
/// <para><b>ARCoreの制約</b>(公式ドキュメント): 屋外専用・ポートレート向き専用。
/// 陸上トラックの合成路面は road / terrain / unlabeled のどれに落ちるか実機確認が要る
/// (unlabeled でも Unknown 扱いで従来経路へ戻るだけなので安全側に倒れる)。</para>
/// </summary>
public sealed class OutdoorSemanticClassifier : MonoBehaviour, IOutdoorSemanticSource
{
    /// <summary>検証で画像分類を強制的に切りたいときに false にする。</summary>
    public static bool VerificationDefaultImageSemantics = true;

    [Header("References (auto-found if empty)")]
    [Tooltip("セマンティック画像はiPhoneのカメラ由来。ARグラス出力カメラではなく必ずARカメラを使う")]
    [SerializeField] private Camera arCamera;

    [Header("Sampling")]
    [Tooltip("セマンティック画像を引く頻度(Hz)。60fpsの描画経路にML推論を載せないため低く保つ")]
    [SerializeField, Range(1f, 15f)] private float samplesPerSecond = 5f;

    [Tooltip("このフレーム数ぶん古いサンプルは使わない(秒)")]
    [SerializeField] private float maxSampleAgeSeconds = 0.5f;

    [Tooltip("信頼度がこれ未満の画素は Unknown として扱う(0〜1)")]
    [SerializeField, Range(0f, 1f)] private float minConfidence = 0.5f;

    [Tooltip("セマンティック画像の上下がビューポートと逆の場合に有効化する(実機で要確認)")]
    [SerializeField] private bool flipVertically;

    // ── 診断用の公開状態 ────────────────────────────────────────────
    /// <summary>端末・パッケージがScene Semanticsに対応しているか。</summary>
    public bool IsSupported { get; private set; }

    /// <summary>実際に分類を引ける状態か(対応 + 新鮮なサンプルあり)。</summary>
    public bool IsAvailable => IsSupported
                               && _labels != null
                               && SurfaceSemanticMath.IsImageSampleFresh(Time.time, _lastSampleTime, maxSampleAgeSeconds);

    /// <summary>供給元の説明(E2E・実機ログで「何が効いているか」を一目で出す)。</summary>
    public string Source { get; private set; } = "unavailable (ARCore Extensions 未導入)";

    /// <summary>これまでに取り込んだセマンティック画像の枚数。</summary>
    public int SampleCount { get; private set; }

    private byte[] _labels;
    private byte[] _confidence;
    private int _imageWidth;
    private int _imageHeight;
    private float _lastSampleTime = -1f;
    private float _nextSampleTime;

    private void Awake()
    {
        if (arCamera == null) arCamera = Camera.main;
        Initialize();
    }

    private void Update()
    {
        if (!IsSupported || Time.time < _nextSampleTime) return;

        _nextSampleTime = Time.time + 1f / Mathf.Max(1f, samplesPerSecond);
        RefreshSemanticImage();
    }

    /// <summary>
    /// ワールド座標の点をARカメラへ投影し、その画素のラベルを返す。
    /// 画面外・カメラ後方・低信頼度・サンプル切れはすべて false(呼び出し側は無変更)。
    /// </summary>
    public bool TryClassify(Vector3 worldPoint, out SurfaceSemantic semantic)
    {
        semantic = SurfaceSemantic.Unknown;

        if (!VerificationDefaultImageSemantics || !IsAvailable || arCamera == null) return false;

        Vector3 vp = arCamera.WorldToViewportPoint(worldPoint);
        float y = flipVertically ? 1f - vp.y : vp.y;

        if (!SurfaceSemanticMath.TryViewportToPixel(vp.x, y, vp.z, _imageWidth, _imageHeight,
                                                    out int px, out int py))
            return false;

        int index = py * _imageWidth + px;
        if (index < 0 || index >= _labels.Length) return false;

        if (_confidence != null && index < _confidence.Length &&
            _confidence[index] / 255f < minConfidence)
            return false;

        semantic = SurfaceSemanticMath.FromArcoreLabel(_labels[index]);
        return semantic != SurfaceSemantic.Unknown;
    }

    // ════════════════════════════════════════════════════════════════════
    // ベンダー境界 — ARCore Extensions が無い環境でも全体がそのまま動くよう、
    // パッケージへ触れるのはこの2メソッドだけに閉じてある
    // ════════════════════════════════════════════════════════════════════

#if ARCORE_EXTENSIONS
    private Google.XR.ARCoreExtensions.ARSemanticManager _semanticManager;
    private Texture2D _semanticTexture;
    private Texture2D _confidenceTexture;

    private void Initialize()
    {
        _semanticManager = FindFirstObjectByType<Google.XR.ARCoreExtensions.ARSemanticManager>(
            FindObjectsInactive.Include);

        if (_semanticManager == null)
        {
            Source = "unavailable (ARSemanticManager がシーンに無い)";
            IsSupported = false;
            return;
        }

        var supported = _semanticManager.IsSemanticModeSupported(
            Google.XR.ARCoreExtensions.SemanticMode.Enabled);
        IsSupported = supported == Google.XR.ARCoreExtensions.FeatureSupported.Supported;
        Source = IsSupported ? "ARCore Scene Semantics" : $"unavailable (端末非対応: {supported})";

        Debug.Log($"[SEMANTICS] 屋外画像分類: {Source}");
    }

    /// <summary>
    /// セマンティック画像を1枚引いてバイト配列へ写す。ラベルは画素の8bit値そのもの。
    /// 以後の点問い合わせはこの配列だけを見るので、推論コストは取得時の1回に閉じる。
    /// </summary>
    private void RefreshSemanticImage()
    {
        if (!_semanticManager.TryGetSemanticTexture(ref _semanticTexture) || _semanticTexture == null)
            return;

        _imageWidth = _semanticTexture.width;
        _imageHeight = _semanticTexture.height;

        var pixels = _semanticTexture.GetPixels32();
        if (_labels == null || _labels.Length != pixels.Length)
            _labels = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            _labels[i] = pixels[i].r; // ラベルは赤チャンネルの8bit値

        if (_semanticManager.TryGetSemanticConfidenceTexture(ref _confidenceTexture)
            && _confidenceTexture != null
            && _confidenceTexture.width == _imageWidth
            && _confidenceTexture.height == _imageHeight)
        {
            var conf = _confidenceTexture.GetPixels32();
            if (_confidence == null || _confidence.Length != conf.Length)
                _confidence = new byte[conf.Length];
            for (int i = 0; i < conf.Length; i++)
                _confidence[i] = conf[i].a; // 信頼度画像は Alpha8
        }
        else
        {
            _confidence = null; // 信頼度が取れなければゲートしない
        }

        _lastSampleTime = Time.time;
        SampleCount++;
    }
#else
    /// <summary>
    /// ARCore Extensions 未導入。休眠したまま <see cref="IsAvailable"/> が false を返し続け、
    /// 接地判定は従来の幾何+ARKit面分類だけで動く。
    /// </summary>
    private void Initialize()
    {
        IsSupported = false;
        Source = "unavailable (ARCore Extensions 未導入)";
    }

    private void RefreshSemanticImage() { }
#endif
}
