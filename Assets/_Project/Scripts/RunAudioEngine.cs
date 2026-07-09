using System.Collections;
using UnityEngine;

/// <summary>
/// セーフティ＆サウンド・システム (企画書 §3 / 要件定義 9.2):
///  - 距離減衰する足音（3D空間音響、路面に応じた音色変化、速度連動ケイデンス）
///  - 心拍連動の呼吸音（HR/4 ≒ 呼吸レート）
///  - システム音（スタートカウントダウン / ゴールファンファーレ）
///  - 環境適応音響（周囲45dB超で自動音量調整、上限75dB相当にキャップ）
/// 全クリップは実行時に手続き生成するため、オーディオアセットは不要。
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

    // Device microphone sampling state
    private AudioClip _micClip;
    private float[] _micBuffer = new float[256];

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

        // RMS threshold approximates the 45dB ambient trigger on-device
        _ambientLoud = rms > micRmsThreshold;
    }

    // ── 足音（路面連動・速度連動・3D距離減衰）─────────────────────────────────

    private void UpdateFootsteps()
    {
        Vector3 delta = avatarTransform.position - _lastAvatarPos;
        delta.y = 0f;
        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.001f);
        _lastAvatarPos = avatarTransform.position;

        if (avatarEngine != null && !avatarEngine.HasStarted) return;
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

        bool running = avatarEngine == null || avatarEngine.HasStarted;
        if (!running)
        {
            _breathSource.volume = 0f;
            return;
        }

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
        for (int i = 0; i < 3; i++)
        {
            _systemSource.PlayOneShot(_beepShortClip, systemLevel * _masterVolume);
            yield return new WaitForSeconds(0.6f);
        }
        _systemSource.PlayOneShot(_beepGoClip, systemLevel * _masterVolume);
    }

    /// <summary>再走行対応: スタート/ゴール音の再生フラグを戻す。</summary>
    public void ResetSession()
    {
        _startSignalPlayed = false;
        _goalPlayed = false;
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
        float[] arpeggio = { 660f, 880f, 1100f };
        foreach (float freq in arpeggio)
        {
            _systemSource.PlayOneShot(CreateSineClip(freq, 0.22f), systemLevel * _masterVolume);
            yield return new WaitForSeconds(0.18f);
        }
        _systemSource.PlayOneShot(CreateSineClip(1320f, 0.7f), systemLevel * _masterVolume);
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
