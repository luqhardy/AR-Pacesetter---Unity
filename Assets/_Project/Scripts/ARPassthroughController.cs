using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// ARKitカメラ(iPhone画面)のカメラ映像(パススルー)の表示可否を、出力先に応じて切り替える。
///
/// グラス出力中は描画を <see cref="GlassViewRig"/> の出力カメラへ移しているので、ARKitカメラの背景は止める。
/// グラスへのカメラ映像は <see cref="GlassPassthroughLayer"/> がグラスの画角で出す
/// (2026-11-01 から既定ON・開発者モードで切替。以前は光学シースルーで二重像になるとして出していなかった)。
///
/// iPhoneの画面に出しているときは逆に、カメラ映像がないとARとして成立しないため
/// パススルーを有効に保つ(ビデオシースルー)。
///
/// カメラのトラッキング自体は常に動き続ける — 消すのは表示だけで、ARKitは
/// 変わらずカメラを使って自己位置推定を行う(頭マウント運用の前提)。
/// </summary>
public class ARPassthroughController : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private Camera arCamera;

    private ARCameraBackground _background;
    private bool _resolved;

    /// <summary>現在カメラ映像を描画しているか。既定はtrue(iPhone表示)。</summary>
    public bool IsPassthroughEnabled { get; private set; } = true;

    // 出すかどうかは2つの条件の両方で決まる:
    //   描画が iPhone の画面にある(グラス出力中は ARKit カメラは何も描かない)
    //   利用者が iPhone の画面にカメラ映像を出す設定にしている(開発者モード・既定ON)
    private bool _renderingOnPhone = true;

    /// <summary>
    /// iPhone の画面にカメラ映像を出す設定(開発者モード「iPhoneにカメラ映像を出す」・既定ON)。
    /// グラス出力中でも保持し、iPhone の画面へ戻ったときに使う
    /// </summary>
    public bool PhoneCameraWanted { get; private set; } = true;

    /// <summary>
    /// iPhone の画面にカメラ映像を出すかを切り替える(<c>SetPhonePassthrough</c>)。
    /// OFF にすると背景は黒で、アバターとHUDだけが出る。ARKit のトラッキングは止めない
    /// </summary>
    public void SetPhoneCameraWanted(bool wanted)
    {
        PhoneCameraWanted = wanted;
        Apply();
        Debug.Log($"[PASSTHROUGH] iPhone のカメラ映像を{(wanted ? "ON" : "OFF")}に設定");
    }

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
        _renderingOnPhone = enabled;
        Apply();
    }

    private void Apply()
    {
        Resolve();
        bool enabled = _renderingOnPhone && PhoneCameraWanted;
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
