using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 走行開始カウントダウンのAR表示。
///
/// 音のカウントダウン(<see cref="RunAudioEngine"/> の PlayStartSignal — 0.6秒間隔の
/// ビープ3回 + GO)は既にあるが、視覚表示が無かった。ここでは HUD のオーバーレイでは
/// なく、**ワールド空間に置いた実体のテキスト**として前方に出す。グラス越しに
/// 「その場に浮かんでいる」ように見え、アバターと同じ空間にあるものとして読める。
///
/// 実行時生成のTextMeshPro(3D)なのでアセット・シーン配線とも不要。
/// 表示位置はカウント開始時のユーザー前方に固定し、向きだけ毎フレーム追従させる
/// (完全に頭に追従させると空間に貼り付いている感じが失われるため)。
/// </summary>
public class CountdownDisplay : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private Transform userCamera;
    [SerializeField] private AvatarEngine avatarEngine;

    [Header("Placement")]
    [Tooltip("ユーザー前方どれだけの距離に出すか(m)。アバターの3.0mより手前へ置く")]
    [SerializeField] private float distanceMeters = 2.2f;
    [Tooltip("目線からの高さオフセット(m)")]
    [SerializeField] private float heightOffsetMeters = 0.1f;
    [Tooltip("文字の物理的な高さ(m)。2.2m先で読める大きさ")]
    [SerializeField] private float characterHeightMeters = 0.5f;

    [Header("Timing")]
    [Tooltip("1カウントの間隔(秒)。RunAudioEngineのビープ間隔と一致させること")]
    [SerializeField] private float stepSeconds = 0.6f;
    [Tooltip("カウント開始値。3 なら 3→2→1→GO")]
    [SerializeField] private int countFrom = 3;
    [Tooltip("GO表示を残す時間(秒)")]
    [SerializeField] private float goHoldSeconds = 0.7f;

    [Header("Appearance")]
    [SerializeField] private Color countColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color goColor = new Color(0.30f, 0.92f, 0.45f);

    private const string GoText = "GO";

    private TextMeshPro _label;
    private bool _sequencePlayed;
    private Coroutine _routine;

    /// <summary>E2E/検証用: カウントダウンが表示中か。</summary>
    public bool IsShowing => _label != null && _label.gameObject.activeSelf;

    /// <summary>E2E/検証用: 現在の表示文字列("3"/"2"/"1"/"GO"、非表示時は空)。</summary>
    public string CurrentText => IsShowing ? _label.text : string.Empty;

    void Awake()
    {
        if (avatarEngine == null)
            avatarEngine = FindFirstObjectByType<AvatarEngine>(FindObjectsInactive.Include);
        if (userCamera == null && Camera.main != null)
            userCamera = Camera.main.transform;
    }

    void Update()
    {
        if (avatarEngine == null || userCamera == null) return;

        // 走行開始と同時に1回だけ走らせる(音のカウントダウンと同じトリガ)
        if (avatarEngine.HasStarted && !_sequencePlayed)
        {
            _sequencePlayed = true;
            _routine = StartCoroutine(RunSequence());
        }

        // 置いた位置は動かさず、向きだけユーザーへ追従させる
        if (IsShowing)
            FaceUser();
    }

    /// <summary>再走行対応: もう一度カウントダウンできるようにする。</summary>
    public void ResetSession()
    {
        _sequencePlayed = false;
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
        if (_label != null)
            _label.gameObject.SetActive(false);
    }

    private IEnumerator RunSequence()
    {
        EnsureLabel();
        if (_label == null) yield break;

        PlaceInFrontOfUser();
        _label.gameObject.SetActive(true);

        for (int n = countFrom; n >= 1; n--)
        {
            _label.text = n.ToString();
            _label.color = countColor;
            yield return new WaitForSeconds(stepSeconds);
        }

        _label.text = GoText;
        _label.color = goColor;
        yield return new WaitForSeconds(goHoldSeconds);

        _label.gameObject.SetActive(false);
        _routine = null;
    }

    /// <summary>カウント開始時のユーザー前方(水平方向)へ配置する。</summary>
    private void PlaceInFrontOfUser()
    {
        Vector3 forward = userCamera.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        _label.transform.position = userCamera.position
                                  + forward * distanceMeters
                                  + Vector3.up * heightOffsetMeters;
        FaceUser();
    }

    private void FaceUser()
    {
        Vector3 toUser = _label.transform.position - userCamera.position;
        toUser.y = 0f;
        if (toUser.sqrMagnitude < 0.0001f) return;
        _label.transform.rotation = Quaternion.LookRotation(toUser.normalized, Vector3.up);
    }

    private void EnsureLabel()
    {
        if (_label != null) return;

        var go = new GameObject("CountdownLabel");
        go.transform.SetParent(null); // ワールド空間に置く(アバターにもカメラにも親付けしない)

        _label = go.AddComponent<TextMeshPro>();
        _label.text = countFrom.ToString();
        _label.fontSize = characterHeightMeters * 10f; // TMP 3D: おおよそ 0.1 = 1単位
        _label.alignment = TextAlignmentOptions.Center;
        _label.color = countColor;
        _label.enableWordWrapping = false;

        // 明るい屋外でも視認できるよう輪郭を付ける。
        // フォント未解決のまま fontMaterial / outline 系へ触ると例外になるため先に確認する
        if (_label.font != null && _label.fontSharedMaterial != null)
        {
            _label.fontMaterial.EnableKeyword("OUTLINE_ON");
            _label.outlineWidth = 0.2f;
            _label.outlineColor = new Color32(0, 0, 0, 255);
        }

        go.SetActive(false);
    }
}
