using System;

/// <summary>ARグラスのFoV値がどの軸を指すか。メーカー公称値は多くが対角。</summary>
public enum GlassFovAxis
{
    Diagonal,
    Horizontal,
    Vertical,
}

/// <summary>
/// ARグラスの光学系から「何がどこまで見えるか」を求める純幾何ロジック (依存ゼロ・テスト可能)。
///
/// <para><b>なぜ必要か</b>: 第1期はiPhoneのARKitカメラがそのまま描画カメラになっている。
/// しかしiPhoneのカメラ画角とXREAL Oneの画角は別物で、ARKit由来の投影行列のまま
/// グラスへ出すと「アバターが3.0m前方にいるのに、見かけの角度が実物と一致しない」。
/// グラスへ出す映像はグラスの光学系に合わせた投影で描く必要がある。</para>
///
/// <para><b>公称FoVは対角である根拠</b>: XREAL One は「50°」、One Pro は「57°」と公称し、
/// 同時に One Pro を「4mで171インチ相当」と表現する。対角57°から求めた対角長は
/// 2 × 4m × tan(28.5°) = 4.35m = 171.1インチで公称値と一致する(One も 50° →
/// 4mで147インチで一致)。よって公称値は対角FoVとして扱う。</para>
///
/// <para>角度の符号: 水平を0°として<b>上が正</b>。カメラの下向き俯角(downPitch)は正値で渡す。</para>
/// </summary>
public static class GlassOpticsMath
{
    private const double Rad2Deg = 180.0 / Math.PI;
    private const double Deg2Rad = Math.PI / 180.0;

    /// <summary>16:9 の対角に対する高さの比 (= 1 / √(a²+1))。</summary>
    private static double HeightPerDiagonal(double aspect) => 1.0 / Math.Sqrt(aspect * aspect + 1.0);

    /// <summary>公称FoVを垂直FoV(度)へ換算する。Unityの <c>Camera.fieldOfView</c> は垂直。</summary>
    public static double VerticalFovDegrees(double fovDegrees, GlassFovAxis axis, double aspect)
    {
        if (fovDegrees <= 0.0 || fovDegrees >= 180.0 || aspect <= 0.0) return 0.0;

        switch (axis)
        {
            case GlassFovAxis.Vertical:
                return fovDegrees;
            case GlassFovAxis.Horizontal:
                return 2.0 * Math.Atan(Math.Tan(fovDegrees * 0.5 * Deg2Rad) / aspect) * Rad2Deg;
            default:
                return 2.0 * Math.Atan(Math.Tan(fovDegrees * 0.5 * Deg2Rad) * HeightPerDiagonal(aspect)) * Rad2Deg;
        }
    }

    /// <summary>公称FoVを水平FoV(度)へ換算する。</summary>
    public static double HorizontalFovDegrees(double fovDegrees, GlassFovAxis axis, double aspect)
    {
        if (fovDegrees <= 0.0 || fovDegrees >= 180.0 || aspect <= 0.0) return 0.0;

        switch (axis)
        {
            case GlassFovAxis.Horizontal:
                return fovDegrees;
            case GlassFovAxis.Vertical:
                return 2.0 * Math.Atan(Math.Tan(fovDegrees * 0.5 * Deg2Rad) * aspect) * Rad2Deg;
            default:
                return 2.0 * Math.Atan(Math.Tan(fovDegrees * 0.5 * Deg2Rad) * aspect * HeightPerDiagonal(aspect)) * Rad2Deg;
        }
    }

    /// <summary>
    /// ビューポートのアスペクトがグラスと違うとき、<b>水平画角を正として</b>垂直FoVを求め直す。
    /// 横方向の角度スケールが実物と一致していることを優先する(横に潰れ/伸びを起こさない)。
    /// </summary>
    public static double VerticalFovForViewport(double horizontalFovDegrees, double viewportAspect)
    {
        if (horizontalFovDegrees <= 0.0 || viewportAspect <= 0.0) return 0.0;
        return 2.0 * Math.Atan(Math.Tan(horizontalFovDegrees * 0.5 * Deg2Rad) / viewportAspect) * Rad2Deg;
    }

    /// <summary>仮想スクリーンの対角インチ(公称表記の検算用)。</summary>
    public static double VirtualScreenDiagonalInches(double diagonalFovDegrees, double distanceMeters)
    {
        if (diagonalFovDegrees <= 0.0 || distanceMeters <= 0.0) return 0.0;
        double meters = 2.0 * distanceMeters * Math.Tan(diagonalFovDegrees * 0.5 * Deg2Rad);
        return meters / 0.0254;
    }

    // ────────────────────────────────────────────────────────────────────
    // 「アバターが視野に入るか」— F-03(3.0m前方)と光学系の突き合わせ
    // ────────────────────────────────────────────────────────────────────

    /// <summary>対象点の仰角(度)。水平が0で上が正。</summary>
    public static double ElevationDegrees(double eyeHeightMeters, double targetHeightMeters, double distanceMeters)
    {
        if (distanceMeters <= 0.0) return 0.0;
        return Math.Atan((targetHeightMeters - eyeHeightMeters) / distanceMeters) * Rad2Deg;
    }

    /// <summary>足元から頭頂までが占める垂直角(度)。これが垂直FoVを超えると全身は収まらない。</summary>
    public static double BodySpanDegrees(double eyeHeightMeters, double avatarHeightMeters, double distanceMeters)
    {
        double feet = ElevationDegrees(eyeHeightMeters, 0.0, distanceMeters);
        double head = ElevationDegrees(eyeHeightMeters, avatarHeightMeters, distanceMeters);
        return head - feet;
    }

    /// <summary>全身がFoVに収まるか。</summary>
    public static bool FullBodyFits(double eyeHeightMeters, double avatarHeightMeters,
                                    double distanceMeters, double verticalFovDegrees)
        => BodySpanDegrees(eyeHeightMeters, avatarHeightMeters, distanceMeters) <= verticalFovDegrees;

    /// <summary>
    /// 全身を収めるのに最低限必要な前方距離(m)。距離が伸びるほど占有角は単調減少するため二分探索。
    /// 収まらない設定(眼高0など)では 0 を返す。
    /// </summary>
    public static double MinimumFullBodyDistanceMeters(double eyeHeightMeters, double avatarHeightMeters,
                                                       double verticalFovDegrees)
    {
        if (verticalFovDegrees <= 0.0 || eyeHeightMeters <= 0.0 || avatarHeightMeters <= 0.0) return 0.0;

        double lo = 0.05, hi = 200.0;
        if (BodySpanDegrees(eyeHeightMeters, avatarHeightMeters, hi) > verticalFovDegrees) return hi;
        if (BodySpanDegrees(eyeHeightMeters, avatarHeightMeters, lo) <= verticalFovDegrees) return lo;

        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (BodySpanDegrees(eyeHeightMeters, avatarHeightMeters, mid) > verticalFovDegrees) lo = mid;
            else hi = mid;
        }
        return hi;
    }

    /// <summary>
    /// アバターを視野の中心に据えるための下向き俯角(度・正が下向き)。
    /// 全身が収まらない場合でも「足元と頭頂の欠けが均等」になる角度を返す。
    /// </summary>
    public static double CenteringDownPitchDegrees(double eyeHeightMeters, double avatarHeightMeters,
                                                   double distanceMeters)
    {
        double feet = ElevationDegrees(eyeHeightMeters, 0.0, distanceMeters);
        double head = ElevationDegrees(eyeHeightMeters, avatarHeightMeters, distanceMeters);
        return -0.5 * (feet + head);
    }

    /// <summary>指定距離で視野に入る最も低い高さ(m)。負なら地面まで見えている。</summary>
    public static double LowestVisibleHeightMeters(double eyeHeightMeters, double verticalFovDegrees,
                                                   double downPitchDegrees, double distanceMeters)
    {
        double lowestElevation = -(downPitchDegrees + verticalFovDegrees * 0.5);
        return eyeHeightMeters + distanceMeters * Math.Tan(lowestElevation * Deg2Rad);
    }

    /// <summary>
    /// 地面が見え始める最短距離(m)。視野の下端が水平より上を向いていれば地面は一切見えない
    /// (<see cref="double.PositiveInfinity"/>)。足元のオーラ(§7.2)や接地の見えに直結する。
    /// </summary>
    public static double NearestVisibleGroundDistanceMeters(double eyeHeightMeters, double verticalFovDegrees,
                                                            double downPitchDegrees)
    {
        double lowestDownAngle = downPitchDegrees + verticalFovDegrees * 0.5;
        if (lowestDownAngle <= 0.0) return double.PositiveInfinity;
        return eyeHeightMeters / Math.Tan(lowestDownAngle * Deg2Rad);
    }
}
