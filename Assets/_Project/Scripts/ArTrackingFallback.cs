using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// ARKit の位置が走行中に止まったら、GPS の距離でカメラのリグを進めてアバターを止めない。
///
/// <para><b>なぜ必要か</b>: アバターの位置は「ARKitカメラ + 3m × 進行方向」なので、ARKit が止まると
/// GPS と HUD が進んでいてもアバターは同じ場所に留まる。判定は <see cref="ArTrackingWatch"/>。</para>
///
/// <para><b>リグごと動かす理由</b>: アバター・グラスの視点・接地がすべて同じカメラ位置を見ているので、
/// リグを進めれば全部が揃う。アバターやグラスに別の経路を足さない。</para>
///
/// <para>ARKit が戻ると位置が飛ぶことがある。止まっていた間に飛んだ分をリグから引くので、走者から見て
/// 瞬間移動しない。GPSロスト中は慣性移動(F-09)に任せ、展示デモでは動かない。</para>
/// </summary>
[DefaultExecutionOrder(-100)] // AvatarEngine がカメラ位置を読む前に動かす
public sealed class ArTrackingFallback : MonoBehaviour
{
    // GPS は約1Hzなので、届いた距離をそのまま足すとカメラが階段状に飛ぶ。時定数0.5秒でならす
    private const float CatchUpRate = 2f;

    private Transform _camera;
    private Transform _rig;
    private AvatarEngine _engine;
    private ARSessionManagerBridge _bridge;
    private GameStateController _state;

    private ArTrackingWatch _watch;
    private Vector3 _rigOffset;
    private Vector3 _frozenRawPosition;
    private double _lastGpsMeters;
    private double _pendingMeters;

    /// <summary>GPS でリグを進めている最中か。</summary>
    public bool IsActive => _watch.IsFrozen;

    /// <summary>今の走行で ARKit が止まった回数。診断用</summary>
    public int FreezeCount { get; private set; }

    /// <summary>起動からの累計。E2E用</summary>
    public int TotalFreezeCount { get; private set; }

    void Update()
    {
        if (!Resolve()) return;

        if (!CanRun())
        {
            if (_watch.IsFrozen) EndFallback(_camera.position - _rigOffset);
            _watch.Reset();
            if (!_engine.IsRunMotionActive) FreezeCount = 0;
            return;
        }

        Vector3 raw = _camera.position - _rigOffset;
        double gps = _bridge.LatestRawDistanceMeters;
        bool wasFrozen = _watch.IsFrozen;
        bool frozen = _watch.Update(Time.time, raw.x, raw.z, gps, ARSession.state == ARSessionState.SessionInitializing);

        if (frozen && !wasFrozen) BeginFallback(raw, gps);
        else if (!frozen && wasFrozen) EndFallback(raw);

        if (!frozen) return;

        _pendingMeters += gps - _lastGpsMeters;
        _lastGpsMeters = gps;
        double step = _pendingMeters * FrameSmoothing.Factor(Time.deltaTime, CatchUpRate);
        _pendingMeters -= step;

        Vector3 heading = _engine.CurrentHeading;
        heading.y = 0f;
        if (heading.sqrMagnitude < 1e-6f) return;
        ShiftRig(heading.normalized * (float)step);
    }

    private bool Resolve()
    {
        if (_camera == null)
        {
            Camera main = Camera.main;
            if (main == null) return false;
            _camera = main.transform;
            XROrigin origin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            _rig = origin != null ? origin.transform : _camera.root;
        }
        if (_engine == null) _engine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (_bridge == null) _bridge = FindFirstObjectByType<ARSessionManagerBridge>(FindObjectsInactive.Include);
        if (_state == null) _state = FindFirstObjectByType<GameStateController>(FindObjectsInactive.Include);
        return _engine != null && _bridge != null;
    }

    private bool CanRun()
    {
        if (!_engine.IsRunMotionActive || _bridge.IsPresentationSession) return false;
        return _state == null || _state.currentState == GameStateController.ARVisionState.Normal;
    }

    private void BeginFallback(Vector3 raw, double gps)
    {
        FreezeCount++;
        TotalFreezeCount++;
        _frozenRawPosition = raw;
        _pendingMeters = gps - _watch.BaselineGpsMeters; // 気付くまでに進んだ分も取り戻す
        _lastGpsMeters = gps;
        Debug.LogWarning($"[AR FALLBACK] ARKit の位置が止まりました — GPS の距離でリグを進めます(取り戻す距離 {_pendingMeters:F1}m)");
    }

    private void EndFallback(Vector3 raw)
    {
        Vector3 jump = raw - _frozenRawPosition;
        jump.y = 0f;
        ShiftRig(-jump);
        _pendingMeters = 0.0;
        _engine.ResyncPacingAnchor(_engine.transform.position, _camera.position);
        Debug.Log($"[AR FALLBACK] ARKit が復帰 — GPS の追従を終了(復帰時の飛び {jump.magnitude:F1}m を吸収)");
    }

    private void ShiftRig(Vector3 delta)
    {
        _rig.position += delta;
        _rigOffset += delta;
    }
}
