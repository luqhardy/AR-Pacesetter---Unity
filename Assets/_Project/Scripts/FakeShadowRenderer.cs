using UnityEngine;

/// <summary>
/// フェイクシャドウ (企画書 4.1 コア・レンダリング):
/// AR空間では実光源のシャドウが得られないため、アバター足元に
/// 半透明の放射状ブロブ影を描画して接地感を与える。
/// テクスチャは実行時生成(アセット不要)。アバターの子として配置するため
/// GroundSnapの接地・傾斜追従、消滅時のスケール、Standby非表示に自動で従う。
/// AvatarEngineと同じGameObjectに置く(Bootstrapが自動装着)。
/// </summary>
public class FakeShadowRenderer : MonoBehaviour
{
    [Header("Shadow Settings")]
    [SerializeField] private float diameterMeters = 1.15f;
    [Range(0f, 1f)] [SerializeField] private float maxOpacity = 0.38f;
    [Tooltip("地面へのめり込み/浮き防止のための足元からのオフセット")]
    [SerializeField] private float groundOffset = 0.015f;

    private GameObject _shadowQuad;
    private Material _material;

    /// <summary>影が生成・表示中か(E2E検証用)。</summary>
    public bool IsVisible => _shadowQuad != null && _shadowQuad.activeInHierarchy;

    void Start()
    {
        BuildShadowQuad();
    }

    private void BuildShadowQuad()
    {
        _shadowQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _shadowQuad.name = "FakeShadow";

        // 物理に影響させない
        var collider = _shadowQuad.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        // アバターの子: 接地(GroundSnap)・傾斜・消滅スケール・Standby非表示に追従
        _shadowQuad.transform.SetParent(transform, false);
        _shadowQuad.transform.localPosition = new Vector3(0f, groundOffset, 0f);
        _shadowQuad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 水平に寝かせる
        _shadowQuad.transform.localScale = new Vector3(diameterMeters, diameterMeters, 1f);

        Shader shader = Shader.Find("Sprites/Default"); // UI同梱・ビルドに常在
        _material = shader != null ? new Material(shader) : null;
        if (_material != null)
        {
            _material.mainTexture = CreateRadialBlobTexture();
            _material.color = new Color(0f, 0f, 0f, maxOpacity);
            _material.renderQueue = 2990; // Transparent直前(アバター半透明より先に描く)
        }

        var renderer = _shadowQuad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = _material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // 中心不透明→縁で完全透過の放射状グラデーション(2乗フォールオフで柔らかく)
    private static Texture2D CreateRadialBlobTexture()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f;
                float dy = (y + 0.5f) / size - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f; // 0(中心)〜1(縁)
                float alpha = Mathf.Clamp01(1f - r);
                alpha = alpha * alpha; // ソフトフォールオフ
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return texture;
    }
}
