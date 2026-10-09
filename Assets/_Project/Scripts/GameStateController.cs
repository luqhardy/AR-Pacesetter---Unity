using System.Collections;
using UnityEngine;

public class GameStateController : MonoBehaviour
{
    // The 5 mandatory states from AGENTS.md §5
    public enum ARVisionState
    {
        Normal,
        InertialMovement,
        FadeOut,
        Standby,
        Reaccumulation
    }

    /// <summary>
    /// スタンバイに入った原因。GPS復帰で ReAccumulation へ進めてよいのは
    /// <see cref="GpsLost"/> のときだけ — グラス切断(§8.3)は再接続後の再スタート操作を、
    /// 低バッテリー退避は利用者の判断を待つ。
    /// </summary>
    public enum StandbyCause
    {
        None,
        GpsLost,
        GlassDisconnected,
        Other
    }

    [Header("Current Status")]
    public ARVisionState currentState = ARVisionState.Normal;

    /// <summary>現在のスタンバイの原因。Standby以外では <see cref="StandbyCause.None"/>。</summary>
    public StandbyCause CurrentStandbyCause { get; private set; } = StandbyCause.None;

    /// <summary>
    /// GPSロスト由来の状態か(慣性移動・フェードアウト・GPS起因のスタンバイ)。
    /// Swiftへの GPSLost 通知・HUDのF-10警告はこれを見る。グラス切断や低バッテリーの
    /// スタンバイを「GPS喪失」と報告しないため、Standby は原因で区別する
    /// </summary>
    public bool IsGpsLossState =>
        currentState == ARVisionState.InertialMovement
        || currentState == ARVisionState.FadeOut
        || (currentState == ARVisionState.Standby && CurrentStandbyCause == StandbyCause.GpsLost);

    [Header("References")]
    [SerializeField] private GameObject avatarTarget;
    [SerializeField] private MeshRenderer avatarRenderer;
    [SerializeField] private AvatarEngine avatarEngine; // For overtake simulation shortcuts

    private SkinnedMeshRenderer _avatarSkinnedRenderer;
    private float _gpsLostTimer = 0.0f;
    private Coroutine _fadeCoroutine;

    // ── GPS Accuracy Gate (AGENTS.md §5 — accuracy radius ≤5m required) ────
    // In production this is fed by ARKit/CoreLocation. In the editor press 'A'.
    public float SimulatedGPSAccuracyRadius { get; set; } = 99f; // 99 = uncertain

    // ────────────────────────────────────────────────────────────────────────
    void Update()
    {
        switch (currentState)
        {
            case ARVisionState.Normal:           HandleNormalState();        break;
            case ARVisionState.InertialMovement: HandleInertialState();      break;
            case ARVisionState.FadeOut:          HandleFadeOutState();       break;
            case ARVisionState.Standby:          HandleStandbyState();       break;
            case ARVisionState.Reaccumulation:   HandleReaccumulationState();break;
        }
    }

    // ── State handlers ───────────────────────────────────────────────────────
    private void HandleNormalState()
    {
        if (Input.GetKeyDown(KeyCode.G))
            TransitionToState(ARVisionState.InertialMovement);

        // ── Overtake simulation shortcuts (AGENTS.md feature #8 / #9) ──────
        // O  = Simulate user running faster than avatar (追い抜かされる動作)
        // P  = Simulate user catching the avatar / avatar surging (追い抜かせる動作)
        // B  = Toggle 20ms latency benchmark HUD (handled by LatencyBenchmarkRunner)
        if (Input.GetKeyDown(KeyCode.O))
        {
            Debug.Log("[SIMULATOR] Simulating BEING OVERTAKEN (O key). " +
                      "User is now faster than avatar for 1.5s.");
            // Directly inject the overtake state for testing
            SimulateBeingOvertaken();
        }

        if (Input.GetKeyDown(KeyCode.P))
        {
            Debug.Log("[SIMULATOR] Simulating OVERTAKING (P key). " +
                      "Avatar surging to stay ahead of user.");
            SimulateAvatarOvertaking();
        }
    }

    private void HandleInertialState()
    {
        _gpsLostTimer += Time.deltaTime;

        if (_gpsLostTimer >= 5.0f)
            TransitionToState(ARVisionState.FadeOut);

        if (Input.GetKeyDown(KeyCode.R))
            TransitionToState(ARVisionState.Normal);
    }

    private void HandleFadeOutState()
    {
        // AGENTS.md §5: GPS restored before 1s completes → return directly to Normal
        if (Input.GetKeyDown(KeyCode.R))
            TransitionToState(ARVisionState.Normal);
    }

    private void HandleStandbyState()
    {
        if (Input.GetKeyDown(KeyCode.R))
            TransitionToState(ARVisionState.Reaccumulation);
    }

    private void HandleReaccumulationState()
    {
        // 'A' simulates GPS accuracy settling to ≤5m in the editor
        if (Input.GetKeyDown(KeyCode.A))
        {
            SimulatedGPSAccuracyRadius = 4.0f; // inside the 5m gate
            Debug.Log("[SIMULATOR] GPS accuracy settled to 4m — ReAccumulation gate now open.");
        }
    }

    // ── Overtake simulation helpers ─────────────────────────────────────────
    /// <summary>
    /// Directly triggers the BeingOvertaken state in AvatarEngine for editor testing.
    /// In production this is driven automatically by AvatarEngine's speed comparison.
    /// </summary>
    private void SimulateBeingOvertaken()
    {
        if (avatarEngine == null)
        {
            avatarEngine = FindObjectOfType<AvatarEngine>();
            if (avatarEngine == null)
            {
                Debug.LogWarning("[SIMULATOR] AvatarEngine not found — assign it in GameStateController Inspector.");
                return;
            }
        }
        avatarEngine.SimulateBeingOvertaken();
    }

    private void SimulateAvatarOvertaking()
    {
        if (avatarEngine == null)
        {
            avatarEngine = FindObjectOfType<AvatarEngine>();
            if (avatarEngine == null) return;
        }
        avatarEngine.SimulateOvertaking();
    }

    // ── Transition dispatcher ────────────────────────────────────────────────
    /// <summary>
    /// 原因つきでスタンバイへ入る。原因は GPS 復帰で自動的に戻してよいかの判断に使う
    /// (<see cref="GpsSignalPolicy"/>)。<see cref="TransitionToState"/> で直接入ると原因は Other。
    /// </summary>
    public void EnterStandby(StandbyCause cause)
    {
        TransitionToState(ARVisionState.Standby);
        CurrentStandbyCause = cause;
    }

    public void TransitionToState(ARVisionState newState)
    {
        currentState = newState;
        CurrentStandbyCause = newState == ARVisionState.Standby ? StandbyCause.Other : StandbyCause.None;
        Debug.Log($"[FSM] AR Vision State → {newState}");

        switch (newState)
        {
            case ARVisionState.InertialMovement:
                _gpsLostTimer = 0.0f;
                break;

            case ARVisionState.FadeOut:
                if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = StartCoroutine(FadeAvatarAlpha(GetCurrentAlpha(), 0.0f, 1.0f));
                break;

            case ARVisionState.Standby:
                if (avatarTarget != null) avatarTarget.SetActive(false);
                break;

            case ARVisionState.Reaccumulation:
                // Reset accuracy so it must be re-confirmed (press A in editor)
                SimulatedGPSAccuracyRadius = 99f;
                if (avatarTarget != null) avatarTarget.SetActive(true);
                if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = StartCoroutine(ExecuteReaccumulationProcess());
                break;

            case ARVisionState.Normal:
                _gpsLostTimer = 0.0f;
                if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
                // Standby(グラス切断/フェード完了)で非表示にしたアバターを復帰させる
                if (avatarTarget != null) avatarTarget.SetActive(true);
                RestoreAvatarAlpha();
                break;
        }
    }

    // ── Renderer hot-swap (called by AvatarModelSwitcher) ───────────────────
    public void UpdateActiveRenderer(MeshRenderer staticMesh, SkinnedMeshRenderer skinnedMesh)
    {
        float currentAlpha = GetCurrentAlpha();
        avatarRenderer        = staticMesh;
        _avatarSkinnedRenderer = skinnedMesh;
        _materials            = null; // 新しいモデルの全マテリアルを取り直す

        GetMaterials().SetAlpha(currentAlpha);
    }

    // ── Coroutines ───────────────────────────────────────────────────────────
    private IEnumerator FadeAvatarAlpha(float start, float end, float duration)
    {
        // マテリアルが取れなくてもフェードの時間は進めてスタンバイへ入る。
        // 以前はここで yield break しており、FSM が FadeOut のまま止まっていた
        AvatarMaterialSet materials = GetMaterials();
        float elapsed = 0.0f;

        while (elapsed < duration)
        {
            // GPS復帰(→Normal)やグラス切断(→Standby)でいつでも打ち切る (AGENTS.md §5)
            if (currentState != ARVisionState.FadeOut) yield break;

            elapsed += Time.deltaTime;
            materials.SetAlpha(Mathf.Lerp(start, end, elapsed / duration));
            yield return null;
        }

        // 途中で別の遷移が入っていたら、その原因(例: グラス切断)を GPS で上書きしない
        if (end == 0.0f && currentState == ARVisionState.FadeOut)
            EnterStandby(StandbyCause.GpsLost);
    }

    private IEnumerator ExecuteReaccumulationProcess()
    {
        // Step 1: 1.5s particle-gathering animation (要件定義 6.2)
        Debug.Log("[REACCUMULATION] Playing 1.5s light-particle gathering FX…");
        AvatarVFXController vfx = avatarTarget != null
            ? avatarTarget.GetComponent<AvatarVFXController>()
            : null;
        if (vfx != null)
            vfx.PlayRecoveryConvergence();
        yield return new WaitForSeconds(1.5f);
        // 演出中にグラス切断等で別の状態へ移っていたら、ここで Normal へ戻してはいけない
        if (currentState != ARVisionState.Reaccumulation) yield break;

        // Step 2: AGENTS.md §5 accuracy gate — wait until radius ≤ 5m
        Debug.Log("[REACCUMULATION] Waiting for GPS accuracy ≤5m… (press A in Editor)");
        while (SimulatedGPSAccuracyRadius > 5.0f)
        {
            if (currentState != ARVisionState.Reaccumulation) yield break;
            yield return null;
        }

        // Step 3: Materialize and confirm
        RestoreAvatarAlpha();

        OvertakeBehaviourController overtake = avatarTarget != null ? avatarTarget.GetComponent<OvertakeBehaviourController>() : null;
        Animator anim = (overtake != null && overtake.ActiveAnimator != null) ? overtake.ActiveAnimator : (avatarTarget != null ? AvatarRigLocator.FindBestAnimator(avatarTarget.transform) : null);
        if (anim != null) anim.SetTrigger("Nod");

        Debug.Log("[REACCUMULATION] GPS lock confirmed. Returning to Normal.");
        TransitionToState(ARVisionState.Normal);
    }

    // ── Material helpers ─────────────────────────────────────────────────────
    // フェード・透過率はモデル配下の**全マテリアル**へ掛ける(AvatarMaterialSet)。
    // 以前は最初のレンダラーの最初のマテリアルだけで、Y Bot(2メッシュ)は半身だけ消えていった
    private AvatarMaterialSet _materials;

    /// <summary>E2E検証用: 透過率を操作しているマテリアルの集合。</summary>
    public AvatarMaterialSet AvatarMaterials => GetMaterials();

    private float GetCurrentAlpha() => GetMaterials().Alpha;

    private AvatarMaterialSet GetMaterials()
    {
        if (_materials != null && !_materials.IsEmpty) return _materials;

        Animator model = avatarTarget != null ? AvatarRigLocator.FindBestAnimator(avatarTarget.transform) : null;
        Renderer fallback = avatarRenderer != null ? avatarRenderer : (Renderer)_avatarSkinnedRenderer;
        _materials = AvatarMaterialSet.FromModel(model != null ? model.transform : null, fallback);
        return _materials;
    }

    /// <summary>
    /// 企画書 4.1: アバターの基準透過率(50%半透明)。
    /// フェード復帰・通常表示はこの値へ戻す。視覚反映にはアバターの
    /// マテリアルが透過モード(lilToon: 半透明)であることが前提。
    /// </summary>
    public const float AvatarBaseAlpha = 0.5f;

    private void RestoreAvatarAlpha() => GetMaterials().SetAlpha(AvatarBaseAlpha);
}
