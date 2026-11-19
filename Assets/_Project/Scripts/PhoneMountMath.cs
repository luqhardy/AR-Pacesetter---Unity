using System;

/// <summary>頭のマウントに付けた iPhone の向き(AR設定「iPhoneの向き」・既定は縦)。</summary>
public enum PhoneMountOrientation
{
    /// <summary>縦向き(iPhone の上端が上)。既定</summary>
    Portrait,

    /// <summary>横向き。どちらへ倒したかは重力から決める(<see cref="PhoneMountMath.ResolveMountAngleDegrees"/>)</summary>
    Landscape,
}

/// <summary>
/// 頭に付けた iPhone の縦横と、グラスへ出すカメラ映像の回転の判断。依存ゼロ(ユニットテスト対象)。
///
/// <para><b>何が縦横で変わるか</b>: グラスの 3D 描画は ARKit カメラの前方向と鉛直だけで向きを作るので
/// (ロールを足さない — <see cref="GlassViewRig"/>)、縦横で変わらない。変わるのは<b>カメラ映像</b>:
/// ARKit の映像は Unity の画面の向き(Screen.orientation)に合わせて立てられている。iPhone の実際の向きが
/// それと違えば(画面の向きを固定したまま横に付けた等)、グラスでは映像だけが 90° 倒れて見える。
/// 実際の向きとの差だけ映像を回して戻す。</para>
///
/// <para>角度はすべて「縦向きから反時計回り」(度)。Unity の ScreenOrientation と同じく
/// LandscapeLeft = +90(反時計回りに倒した)、LandscapeRight = -90、PortraitUpsideDown = 180。</para>
/// </summary>
public static class PhoneMountMath
{
    /// <summary>Swift から届く文字列("portrait" / "landscape")。知らない値は既定の縦</summary>
    public static PhoneMountOrientation Parse(string value)
        => value == "landscape" ? PhoneMountOrientation.Landscape : PhoneMountOrientation.Portrait;

    public static string ToWireValue(PhoneMountOrientation mount)
        => mount == PhoneMountOrientation.Landscape ? "landscape" : "portrait";

    /// <summary>
    /// 重力が画面の面内にこれ未満(G)しか無ければ倒した向きは決めない — 真下・真上を向いているとき
    /// </summary>
    public const float MinInPlaneGravityG = 0.35f;

    /// <summary>
    /// 端末座標の重力(G・x=右・y=上端・縦向きの端末の座標)から、縦向きから反時計回りに何度倒しているか。
    /// 決められなければ NaN
    /// </summary>
    public static float DeviceAngleFromGravity(float gravityX, float gravityY)
    {
        if (float.IsNaN(gravityX) || float.IsNaN(gravityY)) return float.NaN;
        double inPlane = Math.Sqrt((double)gravityX * gravityX + (double)gravityY * gravityY);
        if (inPlane < MinInPlaneGravityG) return float.NaN;
        // 縦(上端が上)で重力は (0,-1)。反時計回りに90°倒すと上端が左を向き、重力は (-1,0)
        return (float)(Math.Atan2(-gravityX, -gravityY) * 180.0 / Math.PI);
    }

    /// <summary>
    /// 設定と重力から、iPhone が縦向きから何度倒れているか(0 / +90 / -90)。
    /// 縦は常に 0。横はどちらへ倒したかを重力で決め、はっきりしない間(真下を向いた・首を大きくかしげた)は
    /// 前の値を保つ(<paramref name="previousDegrees"/>。初回は +90)
    /// </summary>
    public static int ResolveMountAngleDegrees(PhoneMountOrientation mount, float deviceAngleDegrees, int previousDegrees)
    {
        if (mount == PhoneMountOrientation.Portrait) return 0;
        int fallback = previousDegrees == -90 ? -90 : 90;
        if (float.IsNaN(deviceAngleDegrees) || float.IsInfinity(deviceAngleDegrees)) return fallback;
        float a = HeadPoseMath.WrapDegrees(deviceAngleDegrees);
        // 30°〜150° の外(縦に近い・逆さに近い)では切り替えない — 首かしげで左右が入れ替わらないように
        if (a >= 30f && a <= 150f) return 90;
        if (a <= -30f && a >= -150f) return -90;
        return fallback;
    }

    /// <summary>
    /// カメラ映像を何度回して出すか(反時計回り・0 / 90 / 180 / -90)。
    /// 映像は画面の向き <paramref name="screenDegrees"/> で立っていて、iPhone が実際には
    /// <paramref name="mountDegrees"/> だけ倒れていると、映像は差だけ時計回りに倒れて見える — 同じだけ反時計回りに戻す
    /// </summary>
    public static int PassthroughRotationDegrees(int mountDegrees, int screenDegrees)
    {
        int quarter = (int)Math.Round(HeadPoseMath.WrapDegrees(mountDegrees - screenDegrees) / 90f);
        switch (((quarter % 4) + 4) % 4)
        {
            case 1: return 90;
            case 2: return 180;
            case 3: return -90;
            default: return 0;
        }
    }

    /// <summary>映像の縦横が入れ替わる回転か(±90°)</summary>
    public static bool SwapsAxes(int rotationDegrees) => rotationDegrees == 90 || rotationDegrees == -90;
}
