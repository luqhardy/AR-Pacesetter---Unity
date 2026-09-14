using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARKit;
using Unity.XR.CoreUtils;

/// <summary>
/// LiDAR対応iPhoneでは ARKit の密なシーンメッシュと面分類を有効化する。
/// 非対応端末では自動的に無効になり、既存のARPlane経路がそのままフォールバックになる。
/// </summary>
public sealed class EnvironmentSceneScanner : MonoBehaviour
{
    [SerializeField, Range(0.1f, 1f)] private float meshDensity = 0.45f;
    [SerializeField] private Vector3 scanVolumeMeters = new Vector3(12f, 6f, 12f);
    [SerializeField] private float recenterDistanceMeters = 1.0f;
    [SerializeField] private bool debugVisualization = false;

    private ARMeshManager _meshManager;
    private XROrigin _origin;
    private Transform _camera;
    private MeshFilter _runtimeMeshTemplate;
    private Material _debugMaterial;
    private Vector3 _lastCenter;
    private float _nextStatusLogTime;
    private float _nextSubsystemRetryTime;
    private float _nextClassificationRetryTime;
    private int _subsystemRetryCount;
    private bool _classificationRequested;

    private const int MaxSubsystemRetries = 10;

    public bool IsMeshing => _meshManager != null
                             && _meshManager.enabled
                             && _meshManager.subsystem != null
                             && _meshManager.subsystem.running;
    public int MeshCount => _meshManager != null ? _meshManager.meshes.Count : 0;
    public int ClassifiedFaceCount { get; private set; }

    public bool DebugVisualization
    {
        get => debugVisualization;
        set
        {
            if (debugVisualization == value) return;
            debugVisualization = value;
            ApplyDebugVisibility();
        }
    }

    private void Awake()
    {
        _origin = GetComponentInParent<XROrigin>();
        if (_origin == null)
        {
            Debug.LogWarning("[SCENE SCAN] XROrigin が無いためLiDARメッシュを開始できません。ARPlaneへフォールバックします。");
            enabled = false;
            return;
        }

        _camera = _origin.Camera != null ? _origin.Camera.transform : Camera.main?.transform;
        transform.localScale = scanVolumeMeters;
        CreateRuntimeMeshTemplate();

        _meshManager = gameObject.AddComponent<ARMeshManager>();
        _meshManager.meshPrefab = _runtimeMeshTemplate;
        _meshManager.density = meshDensity;
        // GroundSnap は MeshCollider の RaycastHit.normal を使うため頂点法線は不要。
        // LiDAR更新時のCPU仕事を減らし、60fps描画経路への影響を抑える。
        _meshManager.normals = false;
        _meshManager.tangents = false;
        _meshManager.textureCoordinates = false;
        _meshManager.colors = false;
        _meshManager.concurrentQueueSize = 1;
        _meshManager.meshInfosChanged.AddListener(OnMeshInfosChanged);
    }

    private void Start()
    {
        TryEnableClassification();
        Debug.Log(IsMeshing
            ? "[SCENE SCAN] ARKit LiDAR scene mesh started (classification requested)."
            : "[SCENE SCAN] Mesh subsystem unavailable; using classified AR planes and geometry fallback.");
    }

    private void Update()
    {
        // AfterSceneLoad直後はXRMeshSubsystemの準備が間に合わない場合がある。
        // ARMeshManagerはその場合自身を無効化するので、低頻度で再試行する。
        if (_meshManager != null
            && !_meshManager.enabled
            && _subsystemRetryCount < MaxSubsystemRetries
            && Time.unscaledTime >= _nextSubsystemRetryTime)
        {
            _nextSubsystemRetryTime = Time.unscaledTime + 1f;
            _subsystemRetryCount++;
            _meshManager.enabled = true;
        }

        if (!_classificationRequested && Time.unscaledTime >= _nextClassificationRetryTime)
        {
            _nextClassificationRetryTime = Time.unscaledTime + 1f;
            TryEnableClassification();
        }
        RecenterScanVolumeIfNeeded();

        if (Time.unscaledTime >= _nextStatusLogTime)
        {
            _nextStatusLogTime = Time.unscaledTime + 5f;
            if (IsMeshing)
                Debug.Log($"[SCENE SCAN] meshes={MeshCount}, classifiedFaces={ClassifiedFaceCount}, " +
                          $"classification={_classificationRequested}, debug={debugVisualization}");
        }
    }

    private void OnDestroy()
    {
        if (_meshManager != null)
            _meshManager.meshInfosChanged.RemoveListener(OnMeshInfosChanged);
        if (_debugMaterial != null)
            Destroy(_debugMaterial);
        if (_runtimeMeshTemplate != null)
            Destroy(_runtimeMeshTemplate.gameObject);
    }

    private void CreateRuntimeMeshTemplate()
    {
        var template = new GameObject("[Runtime] AR Environment Mesh Template");
        template.transform.SetParent(_origin.transform, false);
        template.SetActive(false);

        _runtimeMeshTemplate = template.AddComponent<MeshFilter>();
        var renderer = template.AddComponent<MeshRenderer>();
        template.AddComponent<MeshCollider>();
        template.AddComponent<ARMeshSemanticSurface>();

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader != null)
        {
            _debugMaterial = new Material(shader) { color = new Color(0f, 0.85f, 1f, 0.35f) };
            renderer.sharedMaterial = _debugMaterial;
        }
        renderer.enabled = debugVisualization;
    }

    private void TryEnableClassification()
    {
        if (_meshManager == null || _meshManager.subsystem == null) return;

#if UNITY_IOS && !UNITY_EDITOR
        _meshManager.subsystem.SetClassificationEnabled(true);
        _classificationRequested = _meshManager.subsystem.GetClassificationEnabled();
#else
        // Editor/SimulationにはARKitネイティブ関数が無い。メッシュ形状だけを検証する。
        _classificationRequested = true;
#endif
    }

    private void RecenterScanVolumeIfNeeded()
    {
        if (_camera == null || _origin == null) return;
        Vector3 local = _origin.transform.InverseTransformPoint(_camera.position);
        if ((local - _lastCenter).sqrMagnitude < recenterDistanceMeters * recenterDistanceMeters) return;

        _lastCenter = local;
        transform.localPosition = local;
    }

    private void OnMeshInfosChanged(ARMeshInfosChangedEventArgs args)
    {
        for (int i = 0; i < args.added.Count; i++) UpdateMesh(args.added[i]);
        for (int i = 0; i < args.updated.Count; i++) UpdateMesh(args.updated[i]);
        RecountClassifiedFaces();
    }

    private void UpdateMesh(MeshUpdateInfo info)
    {
        if (info.meshFilter == null) return;
        var surface = info.meshFilter.GetComponent<ARMeshSemanticSurface>();
        if (surface == null) surface = info.meshFilter.gameObject.AddComponent<ARMeshSemanticSurface>();

#if UNITY_IOS && !UNITY_EDITOR
        if (_meshManager.subsystem != null && _classificationRequested)
        {
            using NativeArray<uint> native = _meshManager.subsystem.GetMeshClassifications(info.id, Allocator.Temp);
            surface.SetClassifications(native);
        }
        else
        {
            surface.ClearClassifications();
        }
#else
        surface.ClearClassifications();
#endif

        var renderer = info.meshFilter.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = _debugMaterial;
            renderer.enabled = debugVisualization;
        }
    }

    private void RecountClassifiedFaces()
    {
        int count = 0;
        if (_meshManager != null)
        {
            foreach (MeshFilter mesh in _meshManager.meshes)
            {
                if (mesh != null && mesh.TryGetComponent(out ARMeshSemanticSurface semantic))
                    count += semantic.ClassifiedFaceCount;
            }
        }
        ClassifiedFaceCount = count;
    }

    private void ApplyDebugVisibility()
    {
        if (_meshManager == null) return;
        foreach (MeshFilter mesh in _meshManager.meshes)
        {
            if (mesh != null && mesh.TryGetComponent(out MeshRenderer renderer))
                renderer.enabled = debugVisualization;
        }
    }
}
