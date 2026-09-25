using System;

/// <summary>
/// ARグラス1機種ぶんの表示仕様 (依存ゼロ・テスト可能)。
///
/// <para>XREAL One は iPhone に対して<b>ただの外部ディスプレイ</b>として振る舞い、
/// 機種名も画角もOSからは取得できない (XREALのSDKはAndroid専用で、iOSからグラスへ
/// 問い合わせる手段が無い)。したがって解像度・リフレッシュレートという
/// 「iOSが知っている値」から機種を推定し、画角は本テーブルの公称値で補う。</para>
///
/// <para>1920×1080 は One / One Pro / Air2 で共通のため解像度だけでは分離できない。
/// 既定は本プロジェクトの実機である <see cref="XrealOne"/> とし、
/// Swift の <c>ConnectXREAL</c> ペイロードに <c>model</c> があればそれを優先する。</para>
/// </summary>
public sealed class GlassDisplayProfile
{
    /// <summary>機種名(<see cref="FromModelName"/> の照合キーでもある)。</summary>
    public string Model { get; }

    /// <summary>公称FoV(度)と、その値が指す軸。</summary>
    public double FovDegrees { get; }
    public GlassFovAxis FovAxis { get; }

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public double RefreshHz { get; }

    /// <summary>
    /// HUDを置いてよい画面比率。バードバス光学系は最外周で歪み・減光が出るうえ、
    /// アイボックスから僅かに外れただけで四隅が最初に欠ける。F-07の周辺視野レイアウトは
    /// この内側に収める。
    /// </summary>
    public double SafeAreaFraction { get; }

    public GlassDisplayProfile(string model, double fovDegrees, GlassFovAxis fovAxis,
                               int pixelWidth, int pixelHeight, double refreshHz,
                               double safeAreaFraction = 0.90)
    {
        Model = model ?? "Unknown";
        FovDegrees = fovDegrees;
        FovAxis = fovAxis;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        RefreshHz = refreshHz;
        SafeAreaFraction = safeAreaFraction;
    }

    public double Aspect => PixelHeight > 0 ? (double)PixelWidth / PixelHeight : 16.0 / 9.0;

    /// <summary>Unityの <c>Camera.fieldOfView</c> にそのまま入る垂直FoV(度)。</summary>
    public double VerticalFovDegrees => GlassOpticsMath.VerticalFovDegrees(FovDegrees, FovAxis, Aspect);

    public double HorizontalFovDegrees => GlassOpticsMath.HorizontalFovDegrees(FovDegrees, FovAxis, Aspect);

    // ────────────────────────────────────────────────────────────────────
    // 機種テーブル(公称値。出典は Docs/XREAL_ONE_INTEGRATION.md)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>本プロジェクトの実機。対角50°・1080p/eye・最大120Hz。</summary>
    public static readonly GlassDisplayProfile XrealOne =
        new GlassDisplayProfile("XREAL One", 50.0, GlassFovAxis.Diagonal, 1920, 1080, 120.0);

    public static readonly GlassDisplayProfile XrealOnePro =
        new GlassDisplayProfile("XREAL One Pro", 57.0, GlassFovAxis.Diagonal, 1920, 1080, 120.0);

    public static readonly GlassDisplayProfile XrealAir2 =
        new GlassDisplayProfile("XREAL Air 2", 46.0, GlassFovAxis.Diagonal, 1920, 1080, 120.0);

    /// <summary>
    /// グラス非接続時(iPhone画面へのビデオシースルー表示)。
    /// 画角はARKitのカメラ内部パラメータが正なので、ここでは「上書きしない」印として使う。
    /// </summary>
    public static readonly GlassDisplayProfile PhoneScreen =
        new GlassDisplayProfile("iPhone Screen", 0.0, GlassFovAxis.Diagonal, 0, 0, 60.0, 1.0);

    /// <summary>画角の上書きを行うべきプロファイルか(<see cref="PhoneScreen"/> は false)。</summary>
    public bool OverridesProjection => FovDegrees > 0.0;

    private static readonly GlassDisplayProfile[] KnownProfiles =
    {
        XrealOne, XrealOnePro, XrealAir2,
    };

    /// <summary>機種名で照合する(大小文字・空白無視の部分一致)。未知なら null。</summary>
    public static GlassDisplayProfile FromModelName(string model)
    {
        if (string.IsNullOrEmpty(model)) return null;

        string key = Normalize(model);
        // 長い名前(One Pro)を先に見ることで "One" が "One Pro" を食わないようにする
        GlassDisplayProfile best = null;
        foreach (var p in KnownProfiles)
        {
            string candidate = Normalize(p.Model);
            if (key.Contains(candidate) && (best == null || candidate.Length > Normalize(best.Model).Length))
                best = p;
        }
        return best;
    }

    private static string Normalize(string s)
        => s.Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();

    /// <summary>
    /// iOSが知っている値(ピクセル数・リフレッシュレート)と、任意の機種名からプロファイルを決める。
    /// 機種名が当たればそれが最優先。当たらなければ解像度が既知グラスと一致するかを見る。
    /// 解像度が未知(=グラス以外のディスプレイ)なら null を返し、呼び出し側が上書きを見送る。
    /// </summary>
    public static GlassDisplayProfile Resolve(string model, int pixelWidth, int pixelHeight, double refreshHz)
    {
        GlassDisplayProfile named = FromModelName(model);
        if (named != null) return named.WithDisplayMode(pixelWidth, pixelHeight, refreshHz);

        foreach (var p in KnownProfiles)
        {
            if (p.PixelWidth == pixelWidth && p.PixelHeight == pixelHeight)
                return p.WithDisplayMode(pixelWidth, pixelHeight, refreshHz); // 既定は先頭の XREAL One
        }

        return null;
    }

    /// <summary>実際に来た表示モードで解像度・リフレッシュレートだけ差し替える(画角は公称値のまま)。</summary>
    public GlassDisplayProfile WithDisplayMode(int pixelWidth, int pixelHeight, double refreshHz)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) return this;
        if (pixelWidth == PixelWidth && pixelHeight == PixelHeight && Math.Abs(refreshHz - RefreshHz) < 0.01)
            return this;

        return new GlassDisplayProfile(Model, FovDegrees, FovAxis, pixelWidth, pixelHeight,
                                       refreshHz > 0.0 ? refreshHz : RefreshHz, SafeAreaFraction);
    }

    public override string ToString()
        => $"{Model} {PixelWidth}x{PixelHeight}@{RefreshHz:F0}Hz " +
           $"(FoV {FovDegrees:F0}° {FovAxis} → H{HorizontalFovDegrees:F1}° V{VerticalFovDegrees:F1}°)";
}
