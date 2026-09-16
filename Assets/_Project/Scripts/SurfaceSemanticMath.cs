/// <summary>
/// ARKit/AR Foundation に依存しない環境面の意味分類。
/// 実機の分類が取れない屋外路面は Unknown のまま幾何判定へ流し、
/// Ceiling/Table/Seat など明確に床でないものだけを確実に除外する。
/// </summary>
public enum SurfaceSemantic
{
    Unknown = 0,
    Other = 1,
    Floor = 2,
    Ceiling = 3,
    Wall = 4,
    Table = 5,
    Seat = 6,
    Window = 7,
    Door = 8,

    // ── 屋外ラベル (ARCore Scene Semantics 由来) ──────────────────────
    // ARKitの面分類は屋内語彙しか持たず、路面・芝・建物は Unknown のまま落ちてくる。
    // 第1期の走行は屋外なので、そこを埋めるための語彙。
    Road = 9,       // 車道
    Sidewalk = 10,  // 歩道
    Terrain = 11,   // 芝・土・砂など歩ける地面
    Building = 12,
    Sky = 13,
    Structure = 14, // フェンス・ガードレール等の非建物構造
    Vehicle = 15,
    Person = 16,
    Tree = 17,      // 樹木・低木(歩けない植生)
    Water = 18,
}

public static class SurfaceSemanticMath
{
    /// <summary>
    /// 床候補の優先度。Floor は Unknown/Other より優先し、家具・構造物は拒否する。
    /// Unknown を許可するのは、ARKit に「road」が無く屋外路面が未分類になり得るため。
    /// </summary>
    public static int GroundPriority(SurfaceSemantic semantic)
    {
        switch (semantic)
        {
            // 明示的に「地面」と分類されたもの。屋内=Floor、屋外=路面/歩道/芝土
            case SurfaceSemantic.Floor:
            case SurfaceSemantic.Road:
            case SurfaceSemantic.Sidewalk:
            case SurfaceSemantic.Terrain:
                return 2;
            case SurfaceSemantic.Unknown:
            case SurfaceSemantic.Other:
                return 1;

            // Water は幾何的には水平な「地面」に見えるが、そこへアバターを置くと
            // ランナーを水面へ誘導することになる。地面候補から明示的に落とす
            default:
                return 0;
        }
    }

    public static bool CanBeGround(SurfaceSemantic semantic)
        => GroundPriority(semantic) > 0;

    /// <summary>進行空間を塞ぐ分類。Ceiling/Floor は水平形状判定へ委ねる。</summary>
    public static bool IsExplicitObstacle(SurfaceSemantic semantic)
    {
        switch (semantic)
        {
            case SurfaceSemantic.Wall:
            case SurfaceSemantic.Table:
            case SurfaceSemantic.Seat:
            case SurfaceSemantic.Window:
            case SurfaceSemantic.Door:
            // 屋外: 建物・フェンス・車両・人・樹木は進路を塞ぐ
            case SurfaceSemantic.Building:
            case SurfaceSemantic.Structure:
            case SurfaceSemantic.Vehicle:
            case SurfaceSemantic.Person:
            case SurfaceSemantic.Tree:
                return true;

            // Sky は無限遠であって障害物ではない。Water で足踏み停止させると
            // 水たまり程度で走行が止まるため、落差判定(§4.2)へ委ねる
            default:
                return false;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // ARCore Scene Semantics (屋外の画像ベース分類) との橋渡し
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ARCore Scene Semantics のラベル値(セマンティック画像の画素値 0〜11)を変換する。
    ///
    /// <para>数値で受けるのは、ARCore Extensions パッケージが入っていない環境でも
    /// この変換をユニットテストできるようにするため。値の定義は
    /// ARCore の <c>ArSemanticLabel</c>(UNLABELED=0 〜 WATER=11)。</para>
    ///
    /// <para>未知の値は <see cref="SurfaceSemantic.Unknown"/> に落とす — 新しいラベルが
    /// 増えたときに、知らないものを勝手に「地面」や「障害物」と決めつけないため。</para>
    /// </summary>
    public static SurfaceSemantic FromArcoreLabel(int label)
    {
        switch (label)
        {
            case 0:  return SurfaceSemantic.Unknown;   // UNLABELED
            case 1:  return SurfaceSemantic.Sky;
            case 2:  return SurfaceSemantic.Building;
            case 3:  return SurfaceSemantic.Tree;
            case 4:  return SurfaceSemantic.Road;
            case 5:  return SurfaceSemantic.Sidewalk;
            case 6:  return SurfaceSemantic.Terrain;
            case 7:  return SurfaceSemantic.Structure;
            case 8:  return SurfaceSemantic.Other;     // OBJECT (一般物体)
            case 9:  return SurfaceSemantic.Vehicle;
            case 10: return SurfaceSemantic.Person;
            case 11: return SurfaceSemantic.Water;
            default: return SurfaceSemantic.Unknown;
        }
    }

    /// <summary>
    /// 3D面分類(LiDARメッシュ/ARPlane)と画像分類(Scene Semantics)を合成する。
    ///
    /// <para><b>3Dが明示的に分類していればそちらが常に勝つ</b>。メッシュの面分類は
    /// その三角形そのものの分類で、画像分類は2D投影からの推定にすぎない。
    /// 画像側が働くのは、3Dが Unknown/Other のとき — つまり
    /// 「ARKitには road が無いので屋外路面が未分類で落ちてくる」まさにその穴だけ。</para>
    /// </summary>
    public static SurfaceSemantic Combine(SurfaceSemantic geometric, SurfaceSemantic image)
    {
        if (geometric != SurfaceSemantic.Unknown && geometric != SurfaceSemantic.Other)
            return geometric;

        if (image == SurfaceSemantic.Unknown)
            return geometric;

        return image;
    }

    /// <summary>
    /// 画像分類サンプルが十分新しいか。セマンティック画像は毎フレーム更新しない
    /// (60fps経路へMLコストを載せないため低頻度で引く)ので、古いサンプルで
    /// 接地判定を動かさないよう明示的に切る。
    /// </summary>
    public static bool IsImageSampleFresh(float nowSeconds, float sampleSeconds, float maxAgeSeconds)
    {
        if (sampleSeconds <= 0f) return false;
        float age = nowSeconds - sampleSeconds;
        return age >= 0f && age <= maxAgeSeconds;
    }

    /// <summary>
    /// ビューポート座標(0〜1)をセマンティック画像の画素位置へ変換する。
    /// 画面外・カメラ後方(<paramref name="viewportZ"/> が0以下)は false。
    /// </summary>
    public static bool TryViewportToPixel(float viewportX, float viewportY, float viewportZ,
                                          int imageWidth, int imageHeight, out int pixelX, out int pixelY)
    {
        pixelX = 0;
        pixelY = 0;
        if (imageWidth <= 0 || imageHeight <= 0) return false;
        if (viewportZ <= 0f) return false;
        if (viewportX < 0f || viewportX > 1f || viewportY < 0f || viewportY > 1f) return false;

        pixelX = (int)(viewportX * (imageWidth - 1) + 0.5f);
        pixelY = (int)(viewportY * (imageHeight - 1) + 0.5f);
        return true;
    }
}
