using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// カメラ映像(パススルー)の表示可否を、出力先に応じて切り替える。
///
/// なぜ必要か: XREALは**光学シースルー**のグラスで、装着者はレンズ越しに現実を直接見る。
/// そこへiPhoneのカメラ映像を描くと、目に見えている現実の上に「現実の動画」を重ねることに
/// なり、二重像になって全体が白く濁る。グラスへ出すべきはアバターとHUDだけで、
/// 背景は黒(=このグラスでは発光せず透過)にする。
///
/// iPhoneの画面に出しているときは逆に、カメラ映像がないとARとして成立しないため
/// パススルーを有効に保つ(ビデオシースルー)。
///
/// カメラのトラッキング自体は常に動き続ける — 消すのは表示だけで、ARKitは
/// 変わらずカメラを使って自己位置推定を行う(胸マウント運用の前提)。
/// </summary>
public class ARPassthroughController : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private Camera arCamera;

    private ARCameraBackground _background;
    private bool _resolved;

    /// <summary>現在カメラ映像を描画しているか。既定はtrue(iPhone表示)。</summary>
    public bool IsPassthroughEnabled { get; private set; } = true;

    void Awake() => Resolve();

    private void Resolve()
    {
        if (_resolved) return;

        if (arCamera == null)
            arCamera = Camera.main;
        if (arCamera != null)
            _background = arCamera.GetComponent<ARCameraBackground>();

        _resolved = arCamera != null;
    }

    /// <summary>
    /// パススルー表示の切り替え。
    /// false でカメラ映像を止め、背景を黒(透過グラスでは非表示)にする。
    /// </summary>
    public void SetPassthroughEnabled(bool enabled)
    {
        Resolve();
        IsPassthroughEnabled = enabled;

        if (_background != null)
            _background.enabled = enabled;

        if (arCamera != null)
        {
            // 背景は常に黒で塗る。グラス側は黒を発光させないので現実が透けて見える
            arCamera.clearFlags = CameraClearFlags.SolidColor;
            arCamera.backgroundColor = Color.black;
        }

        Debug.Log($"[PASSTHROUGH] カメラ映像を{(enabled ? "表示" : "非表示")}に切り替え " +
                  $"(background={( _background != null ? "あり" : "なし")})");
    }
}
