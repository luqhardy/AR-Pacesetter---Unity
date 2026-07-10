using System.Collections;
using UnityEngine;

/// <summary>
/// VFX演出 (企画書 4.1):
///  - 起動時: 粒子集積 — 球殻から粒子が中心へ収束しつつアバターがスケールイン
///  - 終了時: 挨拶(Goodbyeトリガー) → 粒子拡散とともに消滅
///  - 接地時: 足音に同期したサイバーパルス(地面の拡張リング)
/// パーティクル・リングはすべて実行時生成のためアセット不要。
/// AvatarEngine と同じ GameObject に置く(Bootstrapが自動追加)。
/// </summary>
public class AvatarVFXController : MonoBehaviour
{
    [Header("Colors")]
    [SerializeField] private Color fxColor = new Color(0.0f, 0.94f, 1.0f); // ノーマル発光と同系のシアン

    [Header("Timings")]
    [SerializeField] private float spawnDurationSeconds = 1.0f;
    [SerializeField] private float farewellGreetingSeconds = 1.5f;
    [SerializeField] private float dissolveDurationSeconds = 0.8f;

    private AvatarEngine _engine;
    private RunAudioEngine _audioEngine;

    private ParticleSystem _convergePs; // 起動: 収束
    private ParticleSystem _dissolvePs; // 終了: 拡散
    private Material _fxMaterial;

    // 遷移エッジ検知(再走行時はリセット→開始が同一フレームに起きるためフラグ方式は不可)
    private bool _prevStarted = false;
    private bool _prevEnded = false;
    private Vector3 _fullScale = Vector3.one;
    private Vector3 _lastKnownFullScale = Vector3.one;
    private Coroutine _activeSequence;

    // 接地パルス用リングプール
    private const int RingPoolSize = 4;
    private LineRenderer[] _ringPool;
    private int _nextRing = 0;

    void Start()
    {
        _engine = GetComponent<AvatarEngine>();
        _audioEngine = FindFirstObjectByType<RunAudioEngine>(FindObjectsInactive.Include);

        // Sprites/DefaultはUI同梱シェーダーのためビルドにも常に含まれる
        Shader shader = Shader.Find("Sprites/Default");
        _fxMaterial = shader != null ? new Material(shader) : null;

        _convergePs = CreateParticleSystem("VFX_SpawnConverge", converge: true);
        _dissolvePs = CreateParticleSystem("VFX_Dissolve", converge: false);
        BuildRingPool();

        if (_audioEngine != null)
            _audioEngine.FootstepOccurred += SpawnGroundPulse;
    }

    void OnDestroy()
    {
        if (_audioEngine != null)
            _audioEngine.FootstepOccurred -= SpawnGroundPulse;
    }

    void Update()
    {
        if (_engine == null) return;

        bool started = _engine.HasStarted;
        bool ended = _engine.IsSessionEnded;

        // 起動演出 (未開始→開始の立ち上がりエッジ。再走行にもそのまま対応)
        if (started && !_prevStarted)
        {
            if (_activeSequence != null) StopCoroutine(_activeSequence);
            _activeSequence = StartCoroutine(PlaySpawnSequence());
        }

        // 終了演出 (走行中→終了の立ち上がりエッジ)
        if (ended && !_prevEnded)
        {
            if (_activeSequence != null) StopCoroutine(_activeSequence);
            _activeSequence = StartCoroutine(PlayFarewellSequence());
        }

        _prevStarted = started;
        _prevEnded = ended;
    }

    /// <summary>
    /// GPS復帰演出 (要件定義 6.2): 再登場位置に光の粒子が集まる1.5秒の演出。
    /// GameStateController の Reaccumulation から呼ばれる。
    /// </summary>
    public void PlayRecoveryConvergence()
    {
        if (_convergePs == null) return;
        _convergePs.transform.position = transform.position + Vector3.up * 0.9f;
        _convergePs.Play();
    }

    // ── 起動: 粒子集積 + スケールイン ────────────────────────────────────────

    private IEnumerator PlaySpawnSequence()
    {
        // ブリッジの身長スケール適用後の値。前回の消滅でゼロのままなら復元値を使う
        _fullScale = transform.localScale.sqrMagnitude > 0.0001f
            ? transform.localScale
            : _lastKnownFullScale;
        _lastKnownFullScale = _fullScale;

        _convergePs.transform.position = transform.position + Vector3.up * 0.9f;
        _convergePs.Play();

        float elapsed = 0f;
        while (elapsed < spawnDurationSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / spawnDurationSeconds);
            float eased = 1f - Mathf.Pow(1f - t, 3f); // ease-out cubic
            transform.localScale = _fullScale * Mathf.Lerp(0.15f, 1f, eased);
            yield return null;
        }

        transform.localScale = _fullScale;
        _activeSequence = null;
    }

    // ── 終了: 挨拶 → 粒子拡散 + 消滅 ────────────────────────────────────────

    private IEnumerator PlayFarewellSequence()
    {
        _fullScale = transform.localScale;
        if (_fullScale.sqrMagnitude > 0.0001f)
            _lastKnownFullScale = _fullScale;

        SendSafeTrigger("Goodbye"); // お辞儀/手振りモーション(Animator側に用意)
        yield return new WaitForSeconds(farewellGreetingSeconds);

        _dissolvePs.transform.position = transform.position + Vector3.up * 0.9f;
        _dissolvePs.Play();

        float elapsed = 0f;
        while (elapsed < dissolveDurationSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dissolveDurationSeconds);
            transform.localScale = _fullScale * (1f - t * t); // ease-in shrink
            yield return null;
        }

        transform.localScale = Vector3.zero; // 消滅(リセットで復元)
        _activeSequence = null;
    }

    private void SendSafeTrigger(string triggerName)
    {
        Animator anim = AvatarRigLocator.FindBestAnimator(transform);
        if (anim == null) return;
        foreach (var param in anim.parameters)
        {
            if (param.type == AnimatorControllerParameterType.Trigger && param.name == triggerName)
            {
                anim.SetTrigger(triggerName);
                return;
            }
        }
    }

    // ── 接地サイバーパルス ───────────────────────────────────────────────────

    private void SpawnGroundPulse()
    {
        if (_ringPool == null || _ringPool.Length == 0) return;
        // 消滅後・待機前は出さない
        if (_engine != null && (_engine.IsSessionEnded || !_engine.HasStarted)) return;

        LineRenderer ring = _ringPool[_nextRing];
        _nextRing = (_nextRing + 1) % _ringPool.Length;

        ring.transform.position = transform.position + Vector3.up * 0.02f; // 地面すれすれ
        StartCoroutine(AnimateRing(ring));
    }

    private IEnumerator AnimateRing(LineRenderer ring)
    {
        const float duration = 0.35f;
        const float maxRadius = 0.55f;

        ring.gameObject.SetActive(true);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            float radius = Mathf.Lerp(0.08f, maxRadius, 1f - Mathf.Pow(1f - t, 2f));
            SetRingRadius(ring, radius);

            Color c = fxColor;
            c.a = 0.9f * (1f - t);
            ring.startColor = c;
            ring.endColor = c;

            yield return null;
        }

        ring.gameObject.SetActive(false);
    }

    private void BuildRingPool()
    {
        const int segments = 32;
        _ringPool = new LineRenderer[RingPoolSize];

        for (int i = 0; i < RingPoolSize; i++)
        {
            GameObject go = new GameObject($"VFX_GroundPulse_{i}");
            go.transform.SetParent(null); // ワールド固定(アバターに追従させない)

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = segments;
            lr.startWidth = 0.02f;
            lr.endWidth = 0.02f;
            if (_fxMaterial != null) lr.material = _fxMaterial;
            lr.startColor = fxColor;
            lr.endColor = fxColor;

            go.SetActive(false);
            _ringPool[i] = lr;
        }
    }

    private static void SetRingRadius(LineRenderer ring, float radius)
    {
        int count = ring.positionCount;
        for (int i = 0; i < count; i++)
        {
            float angle = i / (float)count * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    // ── パーティクル生成 ─────────────────────────────────────────────────────

    private ParticleSystem CreateParticleSystem(string name, bool converge)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1.2f;
        main.startLifetime = converge ? 0.9f : 1.1f;
        // 負のstartSpeedは球殻から中心へ向かう収束表現になる
        main.startSpeed = converge ? -2.2f : 2.8f;
        main.startSize = 0.05f;
        main.startColor = fxColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = converge ? 1.8f : 0.4f;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, converge ? (short)150 : (short)180) });

        // 寿命の終わりでフェードアウト
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(fxColor, 0f), new GradientColorKey(fxColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        if (_fxMaterial != null) renderer.material = _fxMaterial;

        return ps;
    }
}
