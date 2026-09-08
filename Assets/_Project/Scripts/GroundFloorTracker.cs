/// <summary>
/// 接地の基準となる床面高さの確定・保持 (F-05 グラウンドスナップの土台)。Unity非依存の純ロジック。
///
/// 実測(コライダー / ARプレーン)が得られたらその高さを床として採用し、
/// 実測が途切れている間は<b>直前に確定した床を保持する</b>。実測をまだ一度も
/// 得ていない場合に限り、暫定値(カメラ高 − 想定保持高)を<b>1回だけ</b>採用して固定する。
///
/// なぜ保持が要るか: 実測が無い間にカメラ基準の値を毎フレーム再計算すると、
/// 「床」が頭・端末の上下動に追従してしまい、端末を持ち上げる/上を向くたびに
/// アバターが一緒に浮き上がる(=接地しない・宙を飛ぶ)。一度決めた床を動かさない
/// ことでこれを防ぐ。実測が来れば当然そちらが優先され、坂・段差にも追従する。
/// </summary>
public class GroundFloorTracker
{
    /// <summary>床面高さの由来。</summary>
    public enum FloorSource
    {
        /// <summary>未確定(初期状態)。</summary>
        None = 0,
        /// <summary>暫定 — カメラ高からの推定を1回だけ採用して固定した状態。</summary>
        Provisional = 1,
        /// <summary>実測 — コライダー/ARプレーンから得た床。</summary>
        Measured = 2,
    }

    public FloorSource Source { get; private set; } = FloorSource.None;

    /// <summary>確定済みの床面高さ(ワールドY)。</summary>
    public float FloorY { get; private set; }

    public bool HasFloor => Source != FloorSource.None;

    /// <summary>実測の床を掴んでいるか(暫定値は false)。</summary>
    public bool HasMeasuredFloor => Source == FloorSource.Measured;

    /// <summary>
    /// 今フレームの床面高さを解決する。
    /// </summary>
    /// <param name="hasMeasurement">実測(コライダー/ARプレーン)が取れたか</param>
    /// <param name="measuredFloorY">実測の床面高さ。hasMeasurement=false時は無視される</param>
    /// <param name="provisionalFloorY">実測が一度も無い場合に1回だけ採用する暫定値</param>
    /// <param name="floorY">解決された床面高さ</param>
    /// <returns>床の由来(Source)がこの呼び出しで変化したか(ログを1回だけ出す用)</returns>
    public bool Resolve(bool hasMeasurement, float measuredFloorY,
                        float provisionalFloorY, out float floorY)
    {
        if (hasMeasurement && IsUsable(measuredFloorY))
        {
            bool changed = Source != FloorSource.Measured;
            Source  = FloorSource.Measured;
            FloorY  = measuredFloorY;
            floorY  = FloorY;
            return changed;
        }

        if (Source == FloorSource.None)
        {
            // 暫定値の採用は最初の1回だけ。以降はカメラが動いても再計算しない
            FloorY = IsUsable(provisionalFloorY) ? provisionalFloorY : 0f;
            Source = FloorSource.Provisional;
            floorY = FloorY;
            return true;
        }

        // 確定済みの床を保持 — ここでカメラ基準に再計算すると浮き上がる
        floorY = FloorY;
        return false;
    }

    /// <summary>再走行・セッションリセット時に床の確定をやり直す。</summary>
    public void Reset()
    {
        Source = FloorSource.None;
        FloorY = 0f;
    }

    // ════════════════════════════════════════════════════════════════════════
    // 床候補の妥当性 — 天井・机・壁上端を床と誤認しないための高さ帯
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>床はカメラからこれ以上下にある(m)。これ未満の候補は机・天井とみなす。</summary>
    public const float DefaultMinCameraToFloorMeters = 0.5f;

    /// <summary>床はカメラからこれ以内の下にある(m)。これを超える候補は階下・吹き抜け。</summary>
    public const float DefaultMaxCameraToFloorMeters = 3.0f;

    /// <summary>
    /// 床候補がカメラとの高低差として妥当かを判定する。
    ///
    /// <para><b>なぜ法線チェックだけでは足りないのか</b>:
    /// ARKitの <c>ARPlaneAnchor.alignment</c> は <c>.horizontal</c> / <c>.vertical</c> しか
    /// 区別せず、<b>床と天井はどちらも「水平」で、法線は上向きに揃えて返される</b>。
    /// 上下の区別は <c>classification</c>(.floor / .ceiling / .table)にしか無い。
    /// そのため「上向きの面のうち最も高いもの」を床に選ぶと、部屋の中では
    /// <b>天井が常に勝つ</b> — アバターが天井高へ跳ね上がり、視界から消える。
    /// 壁も、平面メッシュの縁を拾うと同じ経路で高い位置の候補になりうる。</para>
    ///
    /// <para>幾何的な事実(床はユーザーの下にある)を制約として課すのが、
    /// プラットフォームの分類に依存しない確実な弾き方になる。</para>
    /// </summary>
    /// <param name="candidateY">床候補のワールドY</param>
    /// <param name="cameraY">カメラ(端末)のワールドY</param>
    /// <param name="minDropMeters">カメラから下へ最低これだけ離れていること</param>
    /// <param name="maxDropMeters">カメラから下へこれ以内であること</param>
    public static bool IsPlausibleFloorCandidate(float candidateY, float cameraY,
                                                 float minDropMeters, float maxDropMeters)
    {
        if (!IsUsable(candidateY) || !IsUsable(cameraY)
            || !IsUsable(minDropMeters) || !IsUsable(maxDropMeters))
            return false;

        if (minDropMeters < 0f || maxDropMeters < minDropMeters)
            return false;

        float drop = cameraY - candidateY;
        return drop >= minDropMeters && drop <= maxDropMeters;
    }

    /// <summary>既定の高さ帯での判定。</summary>
    public static bool IsPlausibleFloorCandidate(float candidateY, float cameraY)
        => IsPlausibleFloorCandidate(candidateY, cameraY,
                                     DefaultMinCameraToFloorMeters,
                                     DefaultMaxCameraToFloorMeters);

    /// <summary>
    /// 確定済みの床が<b>カメラより上</b>にあるなら破棄して確定をやり直す。
    ///
    /// <para>ラッチは「実測が途切れてもカメラに追従させない」ためのものなので、
    /// 原則として解除しない。だが天井を床として掴んでしまった場合(このクラスの
    /// 利用側が高さ帯で弾く前のビルド、あるいは一瞬の誤検出)、アバターは天井高に
    /// 貼り付いたまま二度と戻らず、アプリ再起動しか復帰手段が無くなる。</para>
    ///
    /// <para>そこで<b>「床がカメラより上にある」という物理的にありえない条件だけ</b>を
    /// 解除トリガーにする。端末を頭上へ掲げても床は下のままなので誤発火しない。
    /// 高さ帯の下限(机など)では解除しない — 曖昧な条件で解除するとラッチの意味が消える。</para>
    /// </summary>
    /// <returns>破棄したか(ログを1回だけ出す用)</returns>
    public bool InvalidateIfAboveCamera(float cameraY)
    {
        if (!HasFloor || !IsUsable(cameraY) || !IsUsable(FloorY))
            return false;

        if (FloorY <= cameraY)
            return false;

        Reset();
        return true;
    }

    private static bool IsUsable(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
