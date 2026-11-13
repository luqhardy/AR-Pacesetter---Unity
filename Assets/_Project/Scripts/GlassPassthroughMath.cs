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
}
