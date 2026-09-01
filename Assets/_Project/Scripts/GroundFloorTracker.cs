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

    private static bool IsUsable(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
