using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 目標距離の手前でAR空間に表示するゴールライン。
///
/// 現在のSwift契約はルート終点座標を持たないため、残距離とAvatarEngineの純化済み
/// 進行方向からゴール位置を推定する。遠方では推定位置を更新し、直前ではワールド
/// 座標へ固定して、頭の動きに追従して揺れることを防ぐ。
/// ビジュアルは実行時生成するためPrefab/シーン配線は不要。
/// </summary>
public class GoalLineController : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private Transform userCamera;
    [SerializeField] private AvatarEngine avatarEngine;

    [Header("Distance")]
    [Tooltip("残り何mからゴールラインを表示するか")]
    [SerializeField] private float revealDistanceMeters = 25f;
    [Tooltip("残り何mで推定位置をワールド座標へ固定するか")]
    [SerializeField] private float lockDistanceMeters = 8f;

    [Header("Appearance")]
    [SerializeField] private float lineWidthMeters = 4f;
    [SerializeField] private float lineDepthMeters = 0.5f;
    [SerializeField] private float postHeightMeters = 2.2f;
    [SerializeField] private float bannerHeightMeters = 0.8f;
    [SerializeField] private float groundOffsetMeters = 0.025f;
    [SerializeField] private Color brightColor = Color.white;
    [SerializeField] private Color darkColor = new Color(0.055f, 0.08f, 0.11f, 1f);
    [SerializeField] private Color accentColor = new Color(0.05f, 0.78f, 1f, 1f);
    [SerializeField] private Color celebrationColor = new Color(1f, 0.82f, 0.12f, 1f);
    [Tooltip("CONGRATULATIONS文字が占める最大ワールド幅(m)")]
    [SerializeField] private float celebrationWidthMeters = 4.8f;
    [Tooltip("CONGRATULATIONS文字が占める最大ワールド高(m)")]
    [SerializeField] private float celebrationHeightMeters = 0.65f;
    [Tooltip("ゴールが現れる時の短いポップ演出(秒)")]
    [SerializeField] private float entranceSeconds = 0.35f;
    [SerializeField] private float reachedHoldSeconds = 4.5f;

    private readonly List<Material> _materials = new List<Material>();
    private GameObject _visualRoot;
    private TextMeshPro _celebrationLabel;
    private ParticleSystem _confetti;
    private Coroutine _celebrationRoutine;
    private Vector3 _celebrationBaseScale = Vector3.one;
    private double _targetDistanceMeters;
    private double _currentDistanceMeters;
    private bool _positionLocked;
    private bool _reached;
    private float _reachedAt;
    private float _appearedAt;

    public bool IsVisible => _visualRoot != null && _visualRoot.activeSelf;
    public bool IsPositionLocked => _positionLocked;
    public bool IsReached => _reached;
    public bool IsCelebrationVisible => _celebrationLabel != null
        && _celebrationLabel.gameObject.activeSelf;
    public bool IsConfettiPlaying => _confetti != null && _confetti.isPlaying;
    public double RemainingMeters => GoalLineMath.RemainingMeters(
        _targetDistanceMeters, _currentDistanceMeters);
    public Vector3 WorldPosition => _visualRoot != null ? _visualRoot.transform.position : Vector3.zero;

    void Awake()
    {
        ResolveReferences();
    }

    void Update()
    {
        UpdateEntranceAnimation();

        if (_reached)
        {
            if (Time.time - _reachedAt >= reachedHoldSeconds)
                SetVisible(false);
            return;
        }

        // Unity側の長押し終了など、ブリッジを通らない手動終了でも残像を残さない。
        if (avatarEngine != null && avatarEngine.IsSessionEnded)
        {
            SetVisible(false);
            return;
        }

        if (!GoalLineMath.ShouldShow(_targetDistanceMeters, _currentDistanceMeters,
                                     revealDistanceMeters))
        {
            SetVisible(false);
            return;
        }

        ResolveReferences();
        if (userCamera == null || avatarEngine == null)
            return;

        EnsureVisual();
        SetVisible(true);

        if (!_positionLocked)
        {
            PlaceFromRemainingDistance();
            if (GoalLineMath.ShouldLock(_targetDistanceMeters, _currentDistanceMeters,
                                        lockDistanceMeters))
                _positionLocked = true;
        }
    }

    /// <summary>新しい走行の目標距離を設定し、前回の表示状態を破棄する。</summary>
    public void ConfigureGoal(double targetDistanceMeters)
    {
        _targetDistanceMeters = targetDistanceMeters > 0 ? targetDistanceMeters : 0;
        _currentDistanceMeters = 0;
        _positionLocked = false;
        _reached = false;
        _reachedAt = 0f;
        SetVisible(false);
    }

    /// <summary>RunSessionControllerと同じ正規距離を受け取る。</summary>
    public void UpdateProgress(double currentDistanceMeters)
    {
        _currentDistanceMeters = System.Math.Max(0.0, currentDistanceMeters);
    }

    /// <summary>ゴール到達時は短時間残し、通過したことを視覚的に確認できるようにする。</summary>
    public void MarkReached()
    {
        if (_targetDistanceMeters <= 0 || _reached)
            return;

        _currentDistanceMeters = _targetDistanceMeters;
        ResolveReferences();
        EnsureVisual();
        // 最初のGPS更新が目標距離まで一気に進んだ場合でも原点表示にしない。
        if (!_positionLocked && userCamera != null && avatarEngine != null)
            PlaceFromRemainingDistance();
        _positionLocked = true;
        _reached = true;
        _reachedAt = Time.time;
        SetVisible(true);
        PlayCelebration();
    }

    public void HideImmediately()
    {
        _reached = false;
        StopCelebration();
        SetVisible(false);
    }

    public void ResetSession()
    {
        _targetDistanceMeters = 0;
        _currentDistanceMeters = 0;
        _positionLocked = false;
        _reached = false;
        _reachedAt = 0f;
        StopCelebration();
        SetVisible(false);
    }

    private void ResolveReferences()
    {
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (userCamera == null && Camera.main != null)
            userCamera = Camera.main.transform;
    }

    private void PlaceFromRemainingDistance()
    {
        Vector3 heading = avatarEngine.CurrentHeading;
        heading.y = 0f;
        if (heading.sqrMagnitude < 0.0001f)
        {
            heading = userCamera.forward;
            heading.y = 0f;
        }
        if (heading.sqrMagnitude < 0.0001f)
            heading = Vector3.forward;
        heading.Normalize();

        float forwardDistance = Mathf.Max(0f, (float)RemainingMeters);
        Vector3 position = userCamera.position + heading * forwardDistance;
        position.y = ResolveGroundY(position);

        _visualRoot.transform.SetPositionAndRotation(
            position + Vector3.up * groundOffsetMeters,
            Quaternion.LookRotation(heading, Vector3.up));
    }

    private float ResolveGroundY(Vector3 position)
    {
        Vector3 origin = position + Vector3.up * 3f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 8f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        // ARプレーンがまだ無い場合はGroundSnap済みのアバター足元を暫定床にする。
        return avatarEngine != null ? avatarEngine.transform.position.y : position.y;
    }

    private void EnsureVisual()
    {
        if (_visualRoot != null)
            return;

        _visualRoot = new GameObject("AR Goal Line");
        _visualRoot.SetActive(false);

        Material bright = CreateMaterial(brightColor);
        Material dark = CreateMaterial(darkColor);
        Material accent = CreateMaterial(accentColor);

        const int columns = 12;
        const int rows = 2;
        float tileWidth = lineWidthMeters / columns;
        float tileDepth = lineDepthMeters / rows;

        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var tile = CreateBlock($"Checker {z}-{x}", _visualRoot.transform,
                    new Vector3((x + 0.5f) * tileWidth - lineWidthMeters * 0.5f,
                                0f,
                                (z + 0.5f) * tileDepth - lineDepthMeters * 0.5f),
                    new Vector3(tileWidth, 0.025f, tileDepth),
                    ((x + z) & 1) == 0 ? bright : dark);
                tile.GetComponent<MeshRenderer>().sortingOrder = 2;
            }
        }

        // 参考画像と同じ、上部の黒白チェック柄バナー。
        const int bannerRows = 4;
        float bannerTileHeight = bannerHeightMeters / bannerRows;
        for (int y = 0; y < bannerRows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                CreateBlock($"Banner Checker {y}-{x}", _visualRoot.transform,
                    new Vector3((x + 0.5f) * tileWidth - lineWidthMeters * 0.5f,
                                postHeightMeters - (y + 0.5f) * bannerTileHeight,
                                0f),
                    new Vector3(tileWidth, bannerTileHeight, 0.035f),
                    ((x + y) & 1) == 0 ? bright : dark);
            }
        }

        float postX = lineWidthMeters * 0.5f + 0.08f;
        CreatePost("Left Goal Post", -postX, dark, accent);
        CreatePost("Right Goal Post", postX, dark, accent);

        // 黒い背景や夜間でも輪郭を見失わない、ゲーム風の細いアクセント。
        CreateBlock("Banner Accent", _visualRoot.transform,
            new Vector3(0f, postHeightMeters - bannerHeightMeters - 0.035f, 0.015f),
            new Vector3(lineWidthMeters, 0.045f, 0.055f), accent);

        BuildCelebrationLabel();
        BuildConfetti();
    }

    private void CreatePost(string name, float localX, Material material, Material accent)
    {
        GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = name;
        post.transform.SetParent(_visualRoot.transform, false);
        post.transform.localPosition = new Vector3(localX, postHeightMeters * 0.5f, 0f);
        // Unity Cylinderの標準高は2mなのでYスケールは半分にする。
        post.transform.localScale = new Vector3(0.065f, postHeightMeters * 0.5f, 0.065f);

        Collider collider = post.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        MeshRenderer renderer = post.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cap.name = name + " Accent Cap";
        cap.transform.SetParent(_visualRoot.transform, false);
        cap.transform.localPosition = new Vector3(localX, postHeightMeters + 0.02f, 0f);
        cap.transform.localScale = Vector3.one * 0.18f;
        Collider capCollider = cap.GetComponent<Collider>();
        if (capCollider != null)
            Destroy(capCollider);
        MeshRenderer capRenderer = cap.GetComponent<MeshRenderer>();
        capRenderer.sharedMaterial = accent;
        capRenderer.shadowCastingMode = ShadowCastingMode.Off;
        capRenderer.receiveShadows = false;
    }

    private void BuildCelebrationLabel()
    {
        GameObject labelGo = new GameObject("Goal Congratulations");
        labelGo.transform.SetParent(_visualRoot.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, postHeightMeters + 0.5f, 0f);

        _celebrationLabel = labelGo.AddComponent<TextMeshPro>();
        _celebrationLabel.text = "CONGRATULATIONS!";
        _celebrationLabel.alignment = TextAlignmentOptions.Center;
        _celebrationLabel.textWrappingMode = TextWrappingModes.NoWrap;
        _celebrationLabel.overflowMode = TextOverflowModes.Overflow;
        _celebrationLabel.enableAutoSizing = false;
        _celebrationLabel.fontSize = 4.5f;
        _celebrationLabel.color = celebrationColor;
        if (_celebrationLabel.font == null && TMP_Settings.defaultFontAsset != null)
            _celebrationLabel.font = TMP_Settings.defaultFontAsset;

        // TextMeshPro 3DのfontSizeはワールド単位に近いため、transform scale=1のままでは
        // 文字がゲート数個分まで巨大化する。希望サイズを先に測り、実寸がゲート幅内へ
        // 必ず収まる共通スケールを算出する。
        Vector2 preferred = _celebrationLabel.font != null
            ? _celebrationLabel.GetPreferredValues(
                _celebrationLabel.text, Mathf.Infinity, Mathf.Infinity)
            : new Vector2(40f, 6f);
        preferred.x = Mathf.Max(preferred.x, 0.01f);
        preferred.y = Mathf.Max(preferred.y, 0.01f);
        float fittedScale = Mathf.Min(
            celebrationWidthMeters / preferred.x,
            celebrationHeightMeters / preferred.y);
        _celebrationLabel.rectTransform.sizeDelta = preferred;
        _celebrationBaseScale = Vector3.one * fittedScale;
        labelGo.transform.localScale = _celebrationBaseScale;

        if (_celebrationLabel.font != null && _celebrationLabel.fontSharedMaterial != null)
        {
            _celebrationLabel.fontMaterial.EnableKeyword("OUTLINE_ON");
            _celebrationLabel.outlineWidth = 0.22f;
            _celebrationLabel.outlineColor = new Color32(0, 0, 0, 255);
        }

        labelGo.SetActive(false);
    }

    private void BuildConfetti()
    {
        GameObject go = new GameObject("Goal Confetti");
        go.transform.SetParent(_visualRoot.transform, false);
        go.transform.localPosition = new Vector3(0f, postHeightMeters * 0.65f, 0f);

        _confetti = go.AddComponent<ParticleSystem>();
        _confetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _confetti.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 4.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.11f);
        main.startColor = new ParticleSystem.MinMaxGradient(celebrationColor,
            new Color(0.05f, 0.85f, 1f, 1f));
        main.gravityModifier = 0.7f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 260;

        var colorOverLifetime = _confetti.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(celebrationColor, 0f),
                new GradientColorKey(accentColor, 0.45f),
                new GradientColorKey(new Color(1f, 0.25f, 0.55f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.75f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        var rotation = _confetti.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-7f, 7f);

        var shape = _confetti.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(lineWidthMeters, 0.15f, 0.35f);

        var emission = _confetti.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)120),
            new ParticleSystem.Burst(0.3f, (short)90)
        });

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Particles/Standard Unlit")
                             ?? Shader.Find("Sprites/Default");
        if (particleShader != null)
        {
            Material particleMaterial = new Material(particleShader);
            _materials.Add(particleMaterial);
            renderer.sharedMaterial = particleMaterial;
        }
    }

    private void PlayCelebration()
    {
        StopCelebration();
        if (_celebrationLabel != null)
        {
            _celebrationLabel.gameObject.SetActive(true);
            _celebrationRoutine = StartCoroutine(AnimateCelebrationLabel());
        }
        if (_confetti != null)
            _confetti.Play(true);
    }

    private IEnumerator AnimateCelebrationLabel()
    {
        float elapsed = 0f;
        while (elapsed < reachedHoldSeconds && _celebrationLabel != null)
        {
            elapsed += Time.deltaTime;
            float entrance = Mathf.SmoothStep(0.72f, 1f, Mathf.Clamp01(elapsed / 0.25f));
            float pulse = 1f + Mathf.Abs(Mathf.Sin(elapsed * Mathf.PI * 3f)) * 0.10f;
            _celebrationLabel.transform.localScale = _celebrationBaseScale * entrance * pulse;
            _celebrationLabel.color = Color.Lerp(celebrationColor, brightColor,
                Mathf.PingPong(elapsed * 2.2f, 1f));
            yield return null;
        }

        if (_celebrationLabel != null)
        {
            _celebrationLabel.transform.localScale = _celebrationBaseScale;
            _celebrationLabel.gameObject.SetActive(false);
        }
        _celebrationRoutine = null;
    }

    private void StopCelebration()
    {
        if (_celebrationRoutine != null)
        {
            StopCoroutine(_celebrationRoutine);
            _celebrationRoutine = null;
        }
        if (_celebrationLabel != null)
        {
            _celebrationLabel.transform.localScale = _celebrationBaseScale;
            _celebrationLabel.gameObject.SetActive(false);
        }
        if (_confetti != null)
            _confetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private GameObject CreateBlock(string name, Transform parent, Vector3 localPosition,
                                   Vector3 localScale, Material material)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(parent, false);
        block.transform.localPosition = localPosition;
        block.transform.localScale = localScale;

        Collider collider = block.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        MeshRenderer renderer = block.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return block;
    }

    private Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Standard");
        Material material = shader != null ? new Material(shader) : null;
        if (material != null)
        {
            material.color = color;
            _materials.Add(material);
        }
        return material;
    }

    private void SetVisible(bool visible)
    {
        if (_visualRoot == null || _visualRoot.activeSelf == visible)
            return;

        _visualRoot.SetActive(visible);
        if (visible)
        {
            _appearedAt = Time.time;
            _visualRoot.transform.localScale = Vector3.one * 0.82f;
        }
        else
        {
            _visualRoot.transform.localScale = Vector3.one;
        }
    }

    private void UpdateEntranceAnimation()
    {
        if (_visualRoot == null || !_visualRoot.activeSelf)
            return;

        float t = Mathf.Clamp01((Time.time - _appearedAt) / Mathf.Max(entranceSeconds, 0.01f));
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        _visualRoot.transform.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, eased);
    }

    void OnDestroy()
    {
        foreach (Material material in _materials)
        {
            if (material != null)
                Destroy(material);
        }
        _materials.Clear();

        if (_visualRoot != null)
            Destroy(_visualRoot);
    }
}
