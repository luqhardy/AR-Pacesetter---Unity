using System;

/// <summary>
/// グラスへ出すカメラ映像(パススルー)の切り出し。依存ゼロ(ユニットテスト対象)。
///
/// <para>iPhone のカメラ映像はグラスより画角が広い(XREAL One は垂直約25.7°)。
/// そのまま全面に出すと現実より小さく映り、アバターとも縮尺が合わない。
/// 中央をグラスの画角ぶんだけ切り出して、見えている角度を揃える。</para>
/// </summary>
public static class GlassPassthroughMath
{
    /// <summary>
    /// 映像の縦横それぞれで使う割合(0〜1、中央から)。
    /// </summary>
    /// <param name="glassVerticalFovDegrees">グラスの垂直画角(度)</param>
    /// <param name="cameraProjectionM11">ARKit の投影行列の [1,1](= 1 / tan(垂直画角/2))</param>
    /// <returns>
    /// 切り出す割合。グラスの方が広い(カメラ映像が足りない)ときは 1(全面)で止める —
    /// 映像の外側は無いので、引き伸ばすより縮尺が少しずれる方を選ぶ。値が不正なら 1
    /// </returns>
    public static double UvCropScale(double glassVerticalFovDegrees, double cameraProjectionM11)
    {
        if (!(glassVerticalFovDegrees > 0.0 && glassVerticalFovDegrees < 180.0)) return 1.0;
        if (!(cameraProjectionM11 > 0.0) || double.IsInfinity(cameraProjectionM11)) return 1.0;

        double glassHalfTan = Math.Tan(glassVerticalFovDegrees * Math.PI / 360.0);
        double cameraHalfTan = 1.0 / cameraProjectionM11;
        double scale = glassHalfTan / cameraHalfTan;
        return scale > 1.0 ? 1.0 : scale;
    }

    /// <summary>中央から <paramref name="scale"/> の割合を切り出すときの UV の起点(縦横共通)。</summary>
    public static double CenteredUvOffset(double scale) => 0.5 - scale * 0.5;

    /// <summary>
    /// iPhone を画面の向きと 90° 違えて付けたとき(<see cref="PhoneMountMath.SwapsAxes"/>)の切り出し。
    ///
    /// <para>映像は画面の向きで立っているので、グラスの横は映像の縦に、グラスの縦は映像の横に当たる。
    /// ARKit の表示変換(displayMatrix)は画面の縦横比で映像を切り詰めているが、変換に 0〜1 の外の UV を
    /// 入れれば切り詰められた外側も読める。そこで画面の UV を中央から軸ごとに
    /// <paramref name="scaleX"/> / <paramref name="scaleY"/> 倍して変換へ入れる(1 を超えてよい)。</para>
    ///
    /// <para>映像の外へはみ出す(読む先が 0〜1 を出る)ときは、縦横を同じ割合で縮める —
    /// 引き伸ばすより縮尺が少しずれる方を選ぶ(<see cref="UvCropScale"/> と同じ方針)。</para>
    /// </summary>
    /// <param name="glassVerticalFovDegrees">グラスの垂直画角(度)</param>
    /// <param name="glassAspect">グラスの描画面の縦横比(幅/高さ)</param>
    /// <param name="cameraProjectionM00">ARKit の投影行列 [0,0](= 1 / tan(画面の横の半画角))</param>
    /// <param name="cameraProjectionM11">ARKit の投影行列 [1,1](= 1 / tan(画面の縦の半画角))</param>
    /// <param name="dxx">表示変換: 画面の u がテクスチャの x へ効く係数(行0列0)</param>
    /// <param name="dxy">表示変換: 画面の u がテクスチャの y へ効く係数(行0列1)</param>
    /// <param name="dyx">表示変換: 画面の v がテクスチャの x へ効く係数(行1列0)</param>
    /// <param name="dyy">表示変換: 画面の v がテクスチャの y へ効く係数(行1列1)</param>
    /// <param name="ox">表示変換の定数項 x(行2列0 + 行3列0 — ARKit のシェーダーは (u, v, 1, 1) を掛ける)</param>
    /// <param name="oy">表示変換の定数項 y(行2列1 + 行3列1)</param>
    /// <returns>決められれば true。値が不正なら false(呼び手は切り出しをやめて全面を出す)</returns>
    public static bool QuarterTurnUvScales(double glassVerticalFovDegrees, double glassAspect,
                                           double cameraProjectionM00, double cameraProjectionM11,
                                           double dxx, double dxy, double dyx, double dyy, double ox, double oy,
                                           out double scaleX, out double scaleY)
    {
        scaleX = 1.0;
        scaleY = 1.0;
        if (!(glassVerticalFovDegrees > 0.0 && glassVerticalFovDegrees < 180.0)) return false;
        if (!(glassAspect > 0.0) || double.IsInfinity(glassAspect)) return false;
        if (!(cameraProjectionM00 > 0.0) || double.IsInfinity(cameraProjectionM00)) return false;
        if (!(cameraProjectionM11 > 0.0) || double.IsInfinity(cameraProjectionM11)) return false;
        if (!IsFinite(dxx) || !IsFinite(dxy) || !IsFinite(dyx) || !IsFinite(dyy) || !IsFinite(ox) || !IsFinite(oy))
            return false;

        double glassHalfTanV = Math.Tan(glassVerticalFovDegrees * Math.PI / 360.0);
        double glassHalfTanH = glassHalfTanV * glassAspect;
        // 画面の横(u)はグラスの縦を、画面の縦(v)はグラスの横を受け持つ
        double kx = glassHalfTanV * cameraProjectionM00;
        double ky = glassHalfTanH * cameraProjectionM11;

        // 画面の中央が読むテクスチャの位置と、そこから四隅までの広がり
        double cx = 0.5 * (dxx + dyx) + ox;
        double cy = 0.5 * (dxy + dyy) + oy;
        double reachX = 0.5 * (Math.Abs(kx * dxx) + Math.Abs(ky * dyx));
        double reachY = 0.5 * (Math.Abs(kx * dxy) + Math.Abs(ky * dyy));
        double roomX = Math.Min(cx, 1.0 - cx);
        double roomY = Math.Min(cy, 1.0 - cy);
        if (!(roomX > 0.0) || !(roomY > 0.0)) return false;

        double fit = 1.0;
        if (reachX > roomX) fit = Math.Min(fit, roomX / reachX);
        if (reachY > roomY) fit = Math.Min(fit, roomY / reachY);

        scaleX = kx * fit;
        scaleY = ky * fit;
        return true;
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    /// <summary>
    /// これを超える明るさ(0〜255)の画素があれば「絵が出ている」とみなす。
    /// 仮の値: レンズを覆ったときのセンサーのノイズより上、暗い室内の映像より下を狙っている(実機で未調整)
    /// </summary>
    public const int VisiblePixelThreshold = 8;

    /// <summary>
    /// 読み戻した画素の最大の明るさから、変換結果が真っ黒でないかを判定する。
    /// 真っ黒 = 変換が何も描いていない(またはカメラが覆われている)
    /// </summary>
    public static bool HasVisiblePixels(int peakChannelValue) => peakChannelValue > VisiblePixelThreshold;
}
