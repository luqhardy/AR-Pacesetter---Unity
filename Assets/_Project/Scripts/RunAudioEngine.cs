using System.Collections;
using UnityEngine;

/// <summary>
/// セーフティ＆サウンド・システム (企画書 §3 / 要件定義 9.2):
///  - 距離減衰する足音（3D空間音響、路面に応じた音色変化、速度連動ケイデンス）
///  - 心拍連動の呼吸音（HR/4 ≒ 呼吸レート）
///  - システム音（スタートカウントダウン / ゴールファンファーレ）
///  - 環境適応音響（周囲45dB超で自動音量調整、上限75dB相当にキャップ）
/// スタート/ゴール音はResources内の提供音源を優先し、見つからない場合は
/// 実行時生成クリップへフォールバックする。
/// </summary>
public class RunAudioEngine : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform avatarTransform;   // Pacing companion anchor
    [SerializeField] private Transform userCamera;        // XR Origin Main Camera
    [SerializeField] private AvatarEngine avatarEngine;
    [SerializeField] private PeripheralHUDManager hudManager; // HR source

    [Header("Mix Levels")]
    [Range(0f, 1f)] [SerializeField] private float footstepLevel = 0.85f;
    [Range(0f, 1f)] [SerializeField] private float breathLevel = 0.55f;
    [Range(0f, 1f)] [SerializeField] private float systemLevel = 0.9f;

    [Header("Start Signal")]
    [Tooltip("未設定時はResources/Audio/StartSignals/RaceStartBeepsから自動読込")]
    [SerializeField] private AudioClip startSignalClip;

    [Header("Goal Jingles")]
    [Tooltip("未設定時はResources/Audio/GoalJinglesから自動読込")]
    [SerializeField] private AudioClip[] goalJingles;

    [Header("Ambient Adaptation (企画書 §3 環境適応音響)")]
    [Tooltip("Normalized master volume in a quiet environment.")]
    [Range(0f, 1f)] [SerializeField] private float quietMasterVolume = 0.7f;
    [Tooltip("Normalized ceiling corresponding to the 75dB output cap.")]
    [Range(0f, 1f)] [SerializeField] private float loudMasterVolumeCap = 1.0f;
    [Tooltip("Mic RMS treated as the 45dB ambient-noise threshold (device approximation).")]
    [SerializeField] private float micRmsThreshold = 0.05f;

    private const int SampleRate = 44100;

    private AudioSource _footstepSource;  // 3D, on avatar
    private AudioSource _breathSource;    // 3D, on avatar
    private AudioSource _systemSource;    // 2D, on user

    private AudioClip _stepHardClip;   // asphalt / track surface
    private AudioClip _stepSoftClip;   // grass / dirt
    private AudioClip _breathLoopClip;
    private AudioClip _beepShortClip;
    private AudioClip _beepGoClip;

    private Vector3 _lastAvatarPos;
    private float _stepPhase = 0f;
    private bool _stepAlternate = false;
    private float _breathPhase = 0f;
    private float _masterVolume;
    private bool _ambientLoud = false;
    private bool _startSignalPlayed = false;
    private bool _goalPlayed = false;
    private string _lastGoalJingleName = string.Empty;

    // Device microphone sampling state
    private AudioClip _micClip;
    private float[] _micBuffer = new float[256];

    /// <summary>足音の発生タイミング(VFXの接地サイバーパルスが購読)。</summary>
    public event System.Action FootstepOccurred;
    public bool HasGoalJingles => goalJingles != null && goalJingles.Length > 0;
    public string LastGoalJingleName => _lastGoalJingleName;

    void Start()
    {
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (hudManager == null)
            hudManager = FindFirstObjectByType<PeripheralHUDManager>(FindObjectsInactive.Include);
        if (avatarTransform == null && avatarEngine != null)
            avatarTransform = avatarEngine.transform;
        if (userCamera == null && Camera.main != null)
            userCamera = Camera.main.transform;

        _masterVolume = quietMasterVolume;

        LoadStartSignal();
        LoadGoalJingles();
        GenerateClips();
        BuildAudioSources();

        if (avatarTransform != null)
            _lastAvatarPos = avatarTransform.position;

#if !UNITY_EDITOR
        StartMicrophoneMonitoring();
#endif
    }

    void Update()
    {
        if (avatarTransform == null) return;

        UpdateAmbientAdaptation();
        UpdateFootsteps();
        UpdateBreathing();
        UpdateSystemCues();
    }

    // ── 環境適応音響 ─────────────────────────────────────────────────────────

    private void UpdateAmbientAdaptation()
    {
#if UNITY_EDITOR
        // Editor: M key toggles a simulated >45dB noisy environment
        if (Input.GetKeyDown(KeyCode.M))
        {
            _ambientLoud = !_ambientLoud;
            Debug.Log($"[AUDIO] Ambient noise simulation (>45dB): {_ambientLoud}");
        }
#else
        SampleMicrophoneLevel();
#endif

        // >45dB ambient -> raise output toward the 75dB-equivalent ceiling; never above it
        float target = _ambientLoud ? loudMasterVolumeCap : quietMasterVolume;
        _masterVolume = Mathf.MoveTowards(_masterVolume, target, Time.deltaTime * 0.5f);
    }

    private void StartMicrophoneMonitoring()
    {
        if (Microphone.devices.Length == 0) return;
        _micClip = Microphone.Start(null, true, 1, 16000);
    }

    // 自己出力(足音・呼吸音)がマイクに回り込んで騒音判定→音量アップ→さらに
    // 騒音判定…と張り付くのを防ぐため、進入/退出の二段閾値+連続サンプル数で判定
    private int _loudStreak = 0;
    private int _quietStreak = 0;
    private const int AmbientSwitchStreak = 10; // 約10フレーム連続で切替

    private void SampleMicrophoneLevel()
    {
        if (_micClip == null) return;

        int micPos = Microphone.GetPosition(null) - _micBuffer.Length;
        if (micPos < 0) return;

        _micClip.GetData(_micBuffer, micPos);
        float sum = 0f;
        for (int i = 0; i < _micBuffer.Length; i++)
            sum += _micBuffer[i] * _micBuffer[i];
        float rms = Mathf.Sqrt(sum / _micBuffer.Length);

        // RMS threshold approximates the 45dB ambient trigger on-device.
        // ヒステリシス: 進入は閾値超、退出は閾値の6割未満
        if (!_ambientLoud && rms > micRmsThreshold)
        {
            _quietStreak = 0;
            if (++_loudStreak >= AmbientSwitchStreak) _ambientLoud = true;
        }
        else if (_ambientLoud && rms < micRmsThreshold * 0.6f)
        {
            _loudStreak = 0;
            if (++_quietStreak >= AmbientSwitchStreak) _ambientLoud = false;
        }
        else
        {
            _loudStreak = 0;
            _quietStreak = 0;
        }
    }

    // ── 足音（路面連動・速度連動・3D距離減衰）─────────────────────────────────

    private void UpdateFootsteps()
    {
        Vector3 delta = avatarTransform.position - _lastAvatarPos;
        delta.y = 0f;
        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.001f);
        _lastAvatarPos = avatarTransform.position;

        if (avatarEngine != null && !avatarEngine.IsRunMotionActive) return;
        if (speed < 0.3f) return; // standing still — no steps

        // Cadence rises with speed: walk ~1.8 steps/s, run ~3.0 steps/s
        float stepsPerSecond = Mathf.Clamp(1.4f + speed * 0.35f, 1.6f, 3.2f);
        _stepPhase += stepsPerSecond * Time.deltaTime;

        if (_stepPhase >= 1f)
        {
            _stepPhase -= 1f;
            PlayFootstep();
        }
    }

    private void PlayFootstep()
    {
        if (_footstepSource == null) return;

        AudioClip clip = IsOnSoftSurface() ? _stepSoftClip : _stepHardClip;
        _stepAlternate = !_stepAlternate;

        // Left/right feet get slightly different pitches for realism
        _footstepSource.pitch = (_stepAlternate ? 1.0f : 0.94f) + Random.Range(-0.03f, 0.03f);
        _footstepSource.PlayOneShot(clip, footstepLevel * _masterVolume);

        FootstepOccurred?.Invoke();
    }

    private bool IsOnSoftSurface()
    {
        if (Physics.Raycast(avatarTransform.position + Vector3.up * 0.5f, Vector3.down,
                out RaycastHit hit, 3.0f))
        {
            string surfaceName = hit.collider.sharedMaterial != null
                ? hit.collider.sharedMaterial.name.ToLowerInvariant()
                : hit.collider.tag.ToLowerInvariant();

            return surfaceName.Contains("grass") || surfaceName.Contains("dirt")
                || surfaceName.Contains("soft") || surfaceName.Contains("soil");
        }
        return false;
    }

    // ── 呼吸音（心拍連動）────────────────────────────────────────────────────

    private void UpdateBreathing()
    {
        if (_breathSource == null) return;

        // 終了後はアバターが消滅するため呼吸音も止める
        bool running = avatarEngine == null || avatarEngine.IsRunMotionActive;
        if (!running)
        {
            _breathSource.volume = 0f;
            return;
        }

        // GPS Standby等でアバターコンテナが一時無効化されるとループ再生が
        // 止まったままになる — 復帰後に自動で再開させる
        if (!_breathSource.isPlaying && _breathSource.isActiveAndEnabled)
            _breathSource.Play();

        int heartRate = hudManager != null ? hudManager.CurrentHeartRate : 135;

        // Physiological approximation: ~1 breath per 4 heart beats
        float breathsPerSecond = heartRate / 60f / 4f;
        _breathPhase += breathsPerSecond * Time.deltaTime;

        // Shaped inhale/exhale swell over the looping noise bed
        float swell = Mathf.Pow(Mathf.Abs(Mathf.Sin(_breathPhase * Mathf.PI)), 1.5f);
        _breathSource.volume = breathLevel * _masterVolume * (0.25f + 0.75f * swell);

        // Faster HR -> slightly sharper (higher) breath timbre
        _breathSource.pitch = Mathf.Lerp(0.9f, 1.25f, Mathf.InverseLerp(100f, 190f, heartRate));
    }

    // ── システム音（カウントダウン / ゴール）─────────────────────────────────

    private void UpdateSystemCues()
    {
        if (avatarEngine == null) return;

        if (avatarEngine.HasStarted && !_startSignalPlayed)
        {
            _startSignalPlayed = true;
            StartCoroutine(PlayStartSignal());
        }
    }

    private IEnumerator PlayStartSignal()
    {
        if (startSignalClip != null)
        {
            _systemSource.pitch = 1f;
            _systemSource.PlayOneShot(startSignalClip, systemLevel * _masterVolume);
            yield break;
        }

        // 音源を同梱できないビルド向けフォールバック。表示側の1秒間隔と同期する。
        for (int i = 0; i < 3; i++)
        {
            _systemSource.PlayOneShot(_beepShortClip, systemLevel * _masterVolume);
            yield return new WaitForSeconds(1.0f);
        }
        _systemSource.PlayOneShot(_beepGoClip, systemLevel * _masterVolume);
    }

    /// <summary>再走行対応: スタート/ゴール音の再生フラグを戻す。</summary>
    public void ResetSession()
    {
        _startSignalPlayed = false;
        _goalPlayed = false;
        _lastGoalJingleName = string.Empty;
        if (_systemSource != null)
            _systemSource.Stop();
    }

    /// <summary>Called by the run-stop flow when the session finishes.</summary>
    public void PlayGoalFanfare()
    {
        if (_goalPlayed || _systemSource == null) return;
        _goalPlayed = true;
        StartCoroutine(PlayGoalSequence());
    }

    private IEnumerator PlayGoalSequence()
    {
        if (HasGoalJingles)
        {
            AudioClip selected = goalJingles[Random.Range(0, goalJingles.Length)];
            if (selected != null)
            {
                _lastGoalJingleName = selected.name;
                _systemSource.pitch = 1f;
                _systemSource.PlayOneShot(selected, systemLevel * _masterVolume);
                yield break;
            }
        }

        // インポート音源が無いビルドでも、従来の手続き生成ファンファーレを鳴らす。
        float[] arpeggio = { 660f, 880f, 1100f };
        foreach (float freq in arpeggio)
        {
            _systemSource.PlayOneShot(CreateSineClip(freq, 0.22f), systemLevel * _masterVolume);
            yield return new WaitForSeconds(0.18f);
        }
        _systemSource.PlayOneShot(CreateSineClip(1320f, 0.7f), systemLevel * _masterVolume);
    }

    private void LoadGoalJingles()
    {
        if (goalJingles != null && goalJingles.Length > 0)
            return;

        goalJingles = Resources.LoadAll<AudioClip>("Audio/GoalJingles");
        if (goalJingles == null || goalJingles.Length == 0)
            Debug.LogWarning("[AUDIO] Goal jingles were not found; procedural fanfare will be used.");
        else
            Debug.Log($"[AUDIO] Loaded {goalJingles.Length} goal jingles.");
    }

    private void LoadStartSignal()
    {
        if (startSignalClip != null)
            return;

        startSignalClip = Resources.Load<AudioClip>("Audio/StartSignals/RaceStartBeeps");
        if (startSignalClip == null)
            Debug.LogWarning("[AUDIO] Race start signal was not found; procedural beeps will be used.");
        else
            Debug.Log($"[AUDIO] Loaded race start signal: {startSignalClip.name}.");
    }

    // ── AudioSource / クリップ生成 ───────────────────────────────────────────

    private void BuildAudioSources()
    {
        Transform footAnchor = avatarTransform != null ? avatarTransform : transform;

        _footstepSource = CreateSource("FootstepAudio", footAnchor, spatial: true);
        _breathSource = CreateSource("BreathAudio", footAnchor, spatial: true);
        _breathSource.clip = _breathLoopClip;
        _breathSource.loop = true;
        _breathSource.volume = 0f;
        _breathSource.Play();

        Transform sysAnchor = userCamera != null ? userCamera : transform;
        _systemSource = CreateSource("SystemAudio", sysAnchor, spatial: false);
    }

    private static AudioSource CreateSource(string name, Transform parent, bool spatial)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = spatial ? 1.0f : 0.0f;
        if (spatial)
        {
            // Audible presence out to ~15m so the receding avatar fades naturally
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1.0f;
            src.maxDistance = 15.0f;
            src.dopplerLevel = 0.3f;
        }
        return src;
    }

    private void GenerateClips()
    {
        _stepHardClip = CreateFootstepClip(sharp: true);
        _stepSoftClip = CreateFootstepClip(sharp: false);
        _breathLoopClip = CreateNoiseLoopClip(2.0f);
        _beepShortClip = CreateSineClip(880f, 0.15f);
        _beepGoClip = CreateSineClip(1320f, 0.5f);
    }

    // Short noise burst with exponential decay. Sharp = asphalt, smooth = grass/dirt.
    private static AudioClip CreateFootstepClip(bool sharp)
    {
        float duration = sharp ? 0.10f : 0.14f;
        int samples = Mathf.CeilToInt(SampleRate * duration);
        float[] data = new float[samples];

        System.Random rng = new System.Random(sharp ? 101 : 202);
        float previous = 0f;
        float smoothing = sharp ? 0.35f : 0.8f; // heavier smoothing = duller thud

        for (int i = 0; i < samples; i++)
        {
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            previous = Mathf.Lerp(noise, previous, smoothing);

            float t = i / (float)samples;
            float envelope = Mathf.Exp(-t * (sharp ? 28f : 18f));
            data[i] = previous * envelope * 0.9f;
        }

        AudioClip clip = AudioClip.Create(sharp ? "StepHard" : "StepSoft", samples, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Heavily low-passed noise bed; the breath swell shape is applied at runtime.
    private static AudioClip CreateNoiseLoopClip(float duration)
    {
        int samples = Mathf.CeilToInt(SampleRate * duration);
        float[] data = new float[samples];

        System.Random rng = new System.Random(42);
        float previous = 0f;
        for (int i = 0; i < samples; i++)
        {
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            previous = Mathf.Lerp(noise, previous, 0.92f);
            data[i] = previous * 0.8f;
        }

        AudioClip clip = AudioClip.Create("BreathLoop", samples, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateSineClip(float frequency, float duration)
    {
        int samples = Mathf.CeilToInt(SampleRate * duration);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)samples;
            // Quick attack, gentle release
            float envelope = Mathf.Min(1f, t * 20f) * (1f - Mathf.Pow(t, 3f));
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / SampleRate) * envelope * 0.6f;
        }

        AudioClip clip = AudioClip.Create($"Tone{frequency:F0}", samples, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
