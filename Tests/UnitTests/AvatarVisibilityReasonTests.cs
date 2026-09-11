using NUnit.Framework;

/// <summary>
/// 「アバターが見えない理由」判定の検証。
/// 要点は優先順位 — より根本的な原因(GameObject無効・GPSロスト)を、
/// 派生的な観測(視野外・距離)より先に報告すること。
/// </summary>
[TestFixture]
public class AvatarVisibilityReasonTests
{
    private static AvatarVisibilityReason.Inputs Healthy() => new AvatarVisibilityReason.Inputs
    {
        GameObjectActive = true,
        AnyRendererEnabled = true,
        FsmState = "Normal",
        InCameraView = true,
        DistanceMeters = 3.0f,
        HorizontalAngleDegrees = 0f,
    };

    [Test]
    public void 通常追従中は見えている()
    {
        Assert.IsTrue(AvatarVisibilityReason.Resolve(Healthy(), out string reason));
        Assert.AreEqual("visible", reason);
    }

    [Test]
    public void GPSロスト後のスタンバイで無効化されたことを理由に出す()
    {
        var i = Healthy();
        i.GameObjectActive = false;
        i.FsmState = "Standby";

        Assert.IsFalse(AvatarVisibilityReason.Resolve(i, out string reason));
        StringAssert.Contains("GPSロスト", reason);
        StringAssert.Contains("Standby", reason);
    }

    [Test]
    public void FSMがNormalなのに無効なら外部要因として報告する()
    {
        var i = Healthy();
        i.GameObjectActive = false;

        Assert.IsFalse(AvatarVisibilityReason.Resolve(i, out string reason));
        StringAssert.Contains("GameObjectが無効", reason);
        StringAssert.DoesNotContain("GPS", reason);
    }

    [Test]
    public void フェードアウト中は視野内でも見えない扱い()
    {
        var i = Healthy();
        i.FsmState = "FadeOut";

        Assert.IsFalse(AvatarVisibilityReason.Resolve(i, out string reason));
        StringAssert.Contains("フェードアウト", reason);
    }

    [Test]
    public void 慣性移動中はまだ見えているが警告を添える()
    {
        var i = Healthy();
        i.FsmState = "InertialMovement";

        Assert.IsTrue(AvatarVisibilityReason.Resolve(i, out string reason));
        StringAssert.Contains("慣性移動", reason);
    }

    [Test]
    public void 描画抑止はGameObject無効の次に優先される()
    {
        var i = Healthy();
        i.AnyRendererEnabled = false;
        i.InCameraView = false; // 視野外でもあるが、こちらは報告しない

        Assert.IsFalse(AvatarVisibilityReason.Resolve(i, out string reason));
        StringAssert.Contains("Renderer", reason);
    }

    [Test]
    public void 背後にいるときは角度と原因候補を添える()
    {
        var i = Healthy();
        i.InCameraView = false;
        i.HorizontalAngleDegrees = 150f;
        i.DistanceMeters = 4.2f;
        i.Halted = true;

        Assert.IsFalse(AvatarVisibilityReason.Resolve(i, out string reason));
        StringAssert.Contains("背後", reason);
        StringAssert.Contains("150°", reason);
        StringAssert.Contains("障害物停止", reason);
    }

    [Test]
    public void 視野外で停止も待機もしていなければ進行方向のずれを疑う()
    {
        var i = Healthy();
        i.InCameraView = false;
        i.HorizontalAngleDegrees = 70f;

        Assert.IsFalse(AvatarVisibilityReason.Resolve(i, out string reason));
        StringAssert.Contains("視野外", reason);
        StringAssert.Contains("進行方向", reason);
    }

    [Test]
    public void 遠すぎる場合は距離を理由に出す()
    {
        var i = Healthy();
        i.DistanceMeters = 22f;
        i.WaitingForUser = true;

        Assert.IsFalse(AvatarVisibilityReason.Resolve(i, out string reason));
        StringAssert.Contains("遠すぎる", reason);
        StringAssert.Contains("離隔待機", reason);
    }

    [Test]
    public void 平面遮蔽ONなら見えていても注記する()
    {
        var i = Healthy();
        i.PlaneOcclusionEnabled = true;

        Assert.IsTrue(AvatarVisibilityReason.Resolve(i, out string reason));
        StringAssert.Contains("平面遮蔽ON", reason);
    }

    [TestCase("Normal", false)]
    [TestCase("InertialMovement", true)]
    [TestCase("FadeOut", true)]
    [TestCase("Standby", true)]
    [TestCase("Reaccumulation", true)]
    public void GPSロスト系の状態判定(string state, bool expected)
    {
        Assert.AreEqual(expected, AvatarVisibilityReason.IsGpsLostState(state));
    }
}
