using NUnit.Framework;

/// <summary>
/// GroundFloorTracker の検証 (F-05 接地の土台)。
/// 要点は「一度確定した床はカメラに追従して動かない」こと —
/// 実測フロアが無い間に毎フレーム カメラ高−1.5m を再計算していたのが
/// 「アバターが接地せず浮き上がる」不具合の原因だった。
/// </summary>
[TestFixture]
public class GroundFloorTrackerTests
{
    private const float AssumedHeight = 1.5f;

    private static float Provisional(float cameraY) => cameraY - AssumedHeight;

    [Test]
    public void 初期状態では床が未確定()
    {
        var t = new GroundFloorTracker();
        Assert.AreEqual(GroundFloorTracker.FloorSource.None, t.Source);
        Assert.IsFalse(t.HasFloor);
        Assert.IsFalse(t.HasMeasuredFloor);
    }

    [Test]
    public void 実測が無ければ暫定値を1回だけ採用する()
    {
        var t = new GroundFloorTracker();
        bool changed = t.Resolve(false, 0f, Provisional(1.6f), out float y);

        Assert.IsTrue(changed, "初回は由来が変化するのでログ対象");
        Assert.AreEqual(0.1f, y, 0.0001f);
        Assert.AreEqual(GroundFloorTracker.FloorSource.Provisional, t.Source);
        Assert.IsFalse(t.HasMeasuredFloor, "暫定値は実測ではない");
    }

    /// <summary>これが回帰防止の本丸: カメラが上下しても床は動かない。</summary>
    [TestCase(1.6f)]
    [TestCase(3.0f)]
    [TestCase(10.0f)]
    [TestCase(-2.0f)]
    public void 暫定確定後はカメラが動いても床が追従しない(float movedCameraY)
    {
        var t = new GroundFloorTracker();
        t.Resolve(false, 0f, Provisional(1.6f), out float first);

        bool changed = t.Resolve(false, 0f, Provisional(movedCameraY), out float after);

        Assert.IsFalse(changed, "由来は変わらない");
        Assert.AreEqual(first, after, 0.0001f,
            $"カメラY={movedCameraY} へ動いても床は {first} のままであること");
    }

    [Test]
    public void 実測が来たら暫定値を上書きする()
    {
        var t = new GroundFloorTracker();
        t.Resolve(false, 0f, Provisional(1.6f), out _);

        bool changed = t.Resolve(true, -0.4f, Provisional(1.6f), out float y);

        Assert.IsTrue(changed, "Provisional → Measured は由来の変化");
        Assert.AreEqual(-0.4f, y, 0.0001f);
        Assert.IsTrue(t.HasMeasuredFloor);
    }

    [Test]
    public void 実測は毎回追従する_坂や段差に対応()
    {
        var t = new GroundFloorTracker();
        t.Resolve(true, 0.0f, 0f, out _);

        bool changed = t.Resolve(true, 0.35f, 0f, out float y);

        Assert.IsFalse(changed, "Measured のままなので由来は変化しない");
        Assert.AreEqual(0.35f, y, 0.0001f, "実測が来ている間は素直に追従する");
    }

    [Test]
    public void 実測が途切れたら直前の実測値を保持する()
    {
        var t = new GroundFloorTracker();
        t.Resolve(true, 0.8f, 0f, out _);

        // ARプレーンのトラッキングが一瞬切れた状況。カメラは 5m の高さにある
        t.Resolve(false, 0f, Provisional(5.0f), out float y);

        Assert.AreEqual(0.8f, y, 0.0001f, "カメラ基準へ戻さず直前の実測床を維持すること");
        Assert.IsTrue(t.HasMeasuredFloor, "一時的な欠測で実測状態を失わない");
    }

    [Test]
    public void NaNの実測は無視される()
    {
        var t = new GroundFloorTracker();
        t.Resolve(true, 0.5f, 0f, out _);

        t.Resolve(true, float.NaN, Provisional(1.6f), out float y);

        Assert.AreEqual(0.5f, y, 0.0001f);
        Assert.IsTrue(t.HasMeasuredFloor);
    }

    [Test]
    public void NaNの暫定値は0へ丸められる()
    {
        var t = new GroundFloorTracker();
        t.Resolve(false, 0f, float.NaN, out float y);

        Assert.AreEqual(0f, y, 0.0001f);
        Assert.AreEqual(GroundFloorTracker.FloorSource.Provisional, t.Source);
    }

    [Test]
    public void Resetで確定をやり直せる_再走行対応()
    {
        var t = new GroundFloorTracker();
        t.Resolve(true, 2.0f, 0f, out _);
        t.Reset();

        Assert.AreEqual(GroundFloorTracker.FloorSource.None, t.Source);
        Assert.IsFalse(t.HasFloor);

        t.Resolve(false, 0f, Provisional(1.6f), out float y);
        Assert.AreEqual(0.1f, y, 0.0001f, "リセット後は新しい暫定値を採用できる");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 床候補の高さ帯 — 天井を床と誤認する不具合(実機で報告)への回帰テスト
    //
    // ARKitの水平平面は床も天井も「法線上向き」で返るため、法線チェックだけでは
    // 天井を弾けない。室内で「上向きの面のうち最も高いもの」を床に選ぶと
    // 天井が必ず勝ち、アバターが天井高へ跳ね上がって視界から消える。
    // ════════════════════════════════════════════════════════════════════════

    [Test]
    public void 天井は床候補として弾かれる()
    {
        // カメラ1.2m、天井2.6m — 上向き水平面だが床ではありえない
        Assert.IsFalse(GroundFloorTracker.IsPlausibleFloorCandidate(2.6f, 1.2f));
    }

    [Test]
    public void カメラと同じ高さや直上の面も弾かれる()
    {
        Assert.IsFalse(GroundFloorTracker.IsPlausibleFloorCandidate(1.2f, 1.2f));
        Assert.IsFalse(GroundFloorTracker.IsPlausibleFloorCandidate(1.3f, 1.2f));
    }

    [Test]
    public void 足元の床は採用される()
    {
        // 胸マウント想定: カメラ1.2m、床0.0m → 落差1.2m
        Assert.IsTrue(GroundFloorTracker.IsPlausibleFloorCandidate(0f, 1.2f));
        // 目線高で持った場合: カメラ1.6m、床0.0m
        Assert.IsTrue(GroundFloorTracker.IsPlausibleFloorCandidate(0f, 1.6f));
    }

    [Test]
    public void 段差や縁石は床として通る()
    {
        // カメラ1.2m、15cmの段差の上面 → まだ十分下にある
        Assert.IsTrue(GroundFloorTracker.IsPlausibleFloorCandidate(0.15f, 1.2f));
    }

    [Test]
    public void 机の高さは床にしない()
    {
        // カメラ1.2m、机0.75m → 落差0.45m は下限0.5m未満
        Assert.IsFalse(GroundFloorTracker.IsPlausibleFloorCandidate(0.75f, 1.2f));
    }

    [Test]
    public void 遠すぎる下の面は拾わない_吹き抜けや階下()
    {
        // カメラ1.2m、階下-2.5m → 落差3.7m は上限3.0m超
        Assert.IsFalse(GroundFloorTracker.IsPlausibleFloorCandidate(-2.5f, 1.2f));
    }

    [Test]
    public void 高さ帯は呼び出し側で調整できる()
    {
        // 下限0.2mまで許すなら机も通る(実機チューニング用の逃げ道)
        Assert.IsTrue(GroundFloorTracker.IsPlausibleFloorCandidate(0.75f, 1.2f, 0.2f, 3.0f));
    }

    [Test]
    public void 不正な入力や範囲は弾く()
    {
        Assert.IsFalse(GroundFloorTracker.IsPlausibleFloorCandidate(float.NaN, 1.2f));
        Assert.IsFalse(GroundFloorTracker.IsPlausibleFloorCandidate(0f, float.NaN));
        Assert.IsFalse(GroundFloorTracker.IsPlausibleFloorCandidate(0f, float.PositiveInfinity));
        Assert.IsFalse(GroundFloorTracker.IsPlausibleFloorCandidate(0f, 1.2f, -1f, 3f), "負の下限は不正");
        Assert.IsFalse(GroundFloorTracker.IsPlausibleFloorCandidate(0f, 1.2f, 3f, 0.5f), "上下限の逆転は不正");
    }

    // ── 天井をラッチしてしまった場合の自己回復 ──────────────────────────────

    [Test]
    public void 天井を掴んだ床は破棄して掴み直す()
    {
        var t = new GroundFloorTracker();
        t.Resolve(true, 2.6f, 0f, out _);          // 天井を床として確定してしまった
        Assert.IsTrue(t.HasFloor);

        Assert.IsTrue(t.InvalidateIfAboveCamera(1.2f), "カメラより上の床は破棄される");
        Assert.AreEqual(GroundFloorTracker.FloorSource.None, t.Source);
        Assert.IsFalse(t.HasFloor);
    }

    [Test]
    public void 正常な床は端末を頭上へ掲げても破棄されない()
    {
        var t = new GroundFloorTracker();
        t.Resolve(true, 0f, 0f, out _);            // 足元の床

        Assert.IsFalse(t.InvalidateIfAboveCamera(1.2f), "通常の保持高");
        Assert.IsFalse(t.InvalidateIfAboveCamera(2.2f), "頭上へ掲げても床は下のまま");
        Assert.IsFalse(t.InvalidateIfAboveCamera(0.05f), "端末を床すれすれに下ろしても破棄しない");
        Assert.IsTrue(t.HasFloor);
        Assert.AreEqual(GroundFloorTracker.FloorSource.Measured, t.Source);
    }

    [Test]
    public void 未確定や不正なカメラ高では破棄しない()
    {
        var t = new GroundFloorTracker();
        Assert.IsFalse(t.InvalidateIfAboveCamera(1.2f), "そもそも床が未確定");

        t.Resolve(true, 2.6f, 0f, out _);
        Assert.IsFalse(t.InvalidateIfAboveCamera(float.NaN), "カメラ高が不正なら判断しない");
        Assert.IsTrue(t.HasFloor);
    }
}
