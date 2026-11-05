using NUnit.Framework;

/// <summary>
/// ペースセッターの挙動(Docs/PACESETTER_BEHAVIOR_SPEC.md)。テスト名の S-xx / T-xx / R-xx は仕様の番号
/// </summary>
[TestFixture]
public class PacesetterModelTests
{
    private const double Speed = 3.6;
    private const double Dt = 1.0 / 60.0;

    /// <summary>走り出して、加速が終わるまで走者がアバターと同じ速さで付いていく</summary>
    private static PacesetterModel Running(out double user)
    {
        var model = new PacesetterModel();
        model.Begin(Speed, 0.0, PacesetterModel.InitialGapMeters);
        user = 0.0;
        // T-01: 走り出す
        while (model.State == PacesetterState.Start)
        {
            user += 0.05;
            model.Tick(Dt, user, Speed);
        }
        while (model.CurrentSpeedMetersPerSecond < Speed)
        {
            user += model.CurrentSpeedMetersPerSecond * Dt;
            model.Tick(Dt, user, Speed);
        }
        return model;
    }

    private static void Simulate(PacesetterModel model, ref double user, double userSpeed, double seconds)
    {
        int frames = (int)(seconds / Dt);
        for (int i = 0; i < frames; i++)
        {
            user += userSpeed * Dt;
            model.Tick(Dt, user, Speed);
        }
    }

    private static void SimulateUntil(PacesetterModel model, ref double user, double userSpeed, PacesetterState state)
    {
        for (int i = 0; i < 60 * 300 && model.State != state; i++)
        {
            user += userSpeed * Dt;
            model.Tick(Dt, user, Speed);
        }
        Assert.AreEqual(state, model.State);
    }

    [Test]
    public void R01_開始時はユーザーの3m先に立って待つ()
    {
        var model = new PacesetterModel();
        model.Begin(Speed, 0.0, PacesetterModel.InitialGapMeters);
        for (int i = 0; i < 600; i++) model.Tick(Dt, 0.0, Speed);

        Assert.AreEqual(PacesetterState.Start, model.State);
        Assert.AreEqual(3.0, model.Gap, 1e-9);
        Assert.AreEqual(0.0, model.CurrentSpeedMetersPerSecond, 1e-9);
    }

    [Test]
    public void T01_走り出すとRUNになり数歩かけて加速する()
    {
        var model = new PacesetterModel();
        model.Begin(Speed, 0.0, PacesetterModel.InitialGapMeters);
        model.Tick(Dt, PacesetterModel.StartMoveMeters, Speed);

        Assert.AreEqual(PacesetterState.Run, model.State);
        Assert.Less(model.CurrentSpeedMetersPerSecond, Speed * 0.1, "いきなり set_speed にならない");

        double accelSeconds = PacesetterModel.AccelSteps / PacesetterSound.StepsPerSecond((float)Speed);
        double user = PacesetterModel.StartMoveMeters;
        Simulate(model, ref user, 0.0, accelSeconds * 0.5);
        Assert.AreEqual(Speed * 0.5, model.CurrentSpeedMetersPerSecond, 0.1);
        Simulate(model, ref user, 0.0, accelSeconds * 0.6);
        Assert.AreEqual(Speed, model.CurrentSpeedMetersPerSecond, 1e-9);
    }

    [Test]
    public void S01_同じ速さで走れば距離は変わらない()
    {
        var model = Running(out double user);
        double gap = model.Gap;
        Simulate(model, ref user, Speed, 30.0);

        Assert.AreEqual(PacesetterState.Run, model.State);
        Assert.AreEqual(gap, model.Gap, 0.05);
    }

    [Test]
    public void S02_遅ければ離れていき10mでWAIT()
    {
        var model = Running(out double user);
        SimulateUntil(model, ref user, 2.0, PacesetterState.Wait);

        Assert.AreEqual(10.0, model.Gap, 0.1);
        Assert.AreEqual(0.0, model.CurrentSpeedMetersPerSecond, 1e-9);
    }

    [Test]
    public void S03_WAITから3mまで近づくと走り出す()
    {
        var model = Running(out double user);
        SimulateUntil(model, ref user, 2.0, PacesetterState.Wait);

        Simulate(model, ref user, Speed, 1.0);
        Assert.AreEqual(PacesetterState.Wait, model.State, "3mまでは待つ");

        SimulateUntil(model, ref user, Speed, PacesetterState.Run);
        Assert.AreEqual(3.0, model.Gap, 0.1);
        Assert.Less(model.CurrentSpeedMetersPerSecond, Speed * 0.1, "T-03 は加速から");
    }

    [Test]
    public void WAIT中のアバターは止まりtarget_lineは進む()
    {
        var model = Running(out double user);
        SimulateUntil(model, ref user, 2.0, PacesetterState.Wait);
        double avatar = model.AvatarPos;
        double target = model.TargetLine;

        Simulate(model, ref user, 0.0, 5.0);

        Assert.AreEqual(avatar, model.AvatarPos, 1e-9);
        Assert.AreEqual(target + Speed * 5.0, model.TargetLine, 0.05);
    }

    [Test]
    public void S04_追い抜くとOVERTAKENになり走り続ける()
    {
        var model = Running(out double user);
        SimulateUntil(model, ref user, Speed + 2.0, PacesetterState.Overtaken);
        Assert.Less(model.Gap, 0.0);

        double avatar = model.AvatarPos;
        Simulate(model, ref user, Speed + 2.0, 2.0);
        Assert.AreEqual(avatar + Speed * 2.0, model.AvatarPos, 0.05, "OVERTAKEN でも set_speed で進む");
    }

    [Test]
    public void S05_減速してアバターが前に出るとRUNへ戻る()
    {
        var model = Running(out double user);
        SimulateUntil(model, ref user, Speed + 2.0, PacesetterState.Overtaken);
        SimulateUntil(model, ref user, Speed - 1.0, PacesetterState.Run);
        Assert.GreaterOrEqual(model.Gap, 0.0);
    }

    [Test]
    public void S06_R07_長く止まるとアバターは10m先で待ち続ける()
    {
        var model = Running(out double user);
        SimulateUntil(model, ref user, 0.0, PacesetterState.Wait);
        Simulate(model, ref user, 0.0, 60.0);

        Assert.AreEqual(PacesetterState.Wait, model.State);
        Assert.AreEqual(10.0, model.Gap, 0.1);
    }

    [Test]
    public void S07_R12_走り出したアバターは遅れを取り戻さない()
    {
        var model = Running(out double user);
        SimulateUntil(model, ref user, 0.0, PacesetterState.Wait);
        Simulate(model, ref user, 0.0, 30.0);
        SimulateUntil(model, ref user, Speed + 1.0, PacesetterState.Run);
        double missing = model.TargetLine - model.AvatarPos;
        Assert.Greater(missing, 100.0);

        Simulate(model, ref user, Speed, 30.0);

        Assert.GreaterOrEqual(model.TargetLine - model.AvatarPos, missing - 0.01, "差は縮まらない");
        Assert.Greater(model.LagSeconds, 25.0, "lag は残る");
    }

    [Test]
    public void S08_R24_補正は数秒かけて寄せる()
    {
        var model = Running(out double user);
        Simulate(model, ref user, Speed, 5.0);
        double gap = model.Gap;

        model.ApplyCheckpointCorrection(6.0); // 走者は実は6m先にいた
        Simulate(model, ref user, Speed, Dt * 2);
        Assert.AreEqual(gap, model.Gap, 0.1, "瞬間移動しない");

        Simulate(model, ref user, Speed, PacesetterModel.CorrectionSeconds * 0.5);
        Assert.AreEqual(gap - 3.0, model.Gap, 0.2, "半分の時間で半分");

        Simulate(model, ref user, Speed, PacesetterModel.CorrectionSeconds);
        Assert.AreEqual(gap - 6.0, model.Gap, 0.05, "数秒で寄り切る");
    }

    [Test]
    public void R21_細かい差は補正しない()
    {
        var model = Running(out double user);
        double gap = model.Gap;
        Assert.IsFalse(model.ApplyCheckpointCorrection(PacesetterModel.MinCorrectionMeters - 0.1));
        Simulate(model, ref user, Speed, PacesetterModel.CorrectionSeconds + 1.0);
        Assert.AreEqual(gap, model.Gap, 0.05);
    }

    [Test]
    public void S10_10m付近で揺れてもWAITに入ったら3mまで戻らない()
    {
        var model = Running(out double user);
        SimulateUntil(model, ref user, 2.0, PacesetterState.Wait);
        for (int i = 0; i < 600; i++)
        {
            user += (i % 2 == 0 ? 0.2 : -0.2);
            model.Tick(Dt, user, Speed);
            Assert.AreEqual(PacesetterState.Wait, model.State);
        }
    }

    [Test]
    public void 加速中に追い抜かれたらOVERTAKENになる()
    {
        var model = new PacesetterModel();
        model.Begin(Speed, 0.0, PacesetterModel.InitialGapMeters);
        double user = PacesetterModel.StartMoveMeters;
        model.Tick(Dt, user, Speed);
        SimulateUntil(model, ref user, 8.0, PacesetterState.Overtaken);
        Assert.Less(model.CurrentSpeedMetersPerSecond, Speed, "まだ加速中");
    }

    [Test]
    public void target_lineとlagは仕様の式どおり()
    {
        var model = Running(out double user);
        double target = model.TargetLine;
        Simulate(model, ref user, 0.0, 1.0);
        Assert.AreEqual(target + Speed, model.TargetLine, 0.05);
        Assert.AreEqual(model.TargetLine - user, model.LagMeters, 1e-9);
        Assert.AreEqual(model.LagMeters / Speed, model.LagSeconds, 1e-9);
    }

    [Test]
    public void 非表示で数えられなかった長い時間でも10mを超えて走り去らない()
    {
        var model = Running(out double user);
        model.Tick(120.0, user, Speed);
        Assert.AreEqual(PacesetterState.Wait, model.State);
        Assert.AreEqual(10.0, model.Gap, 1e-9);
    }

    [Test]
    public void 数でない入力は無視する()
    {
        var model = Running(out double user);
        double avatar = model.AvatarPos;
        model.Tick(double.NaN, user, Speed);
        model.Tick(-1.0, user, Speed);
        model.Tick(Dt, double.NaN, Speed);
        Assert.AreEqual(avatar, model.AvatarPos, 1e-9);
        Assert.IsFalse(model.ApplyCheckpointCorrection(double.NaN));
    }

    [Test]
    public void 数えられなかった時間だけ壁時計で補う()
    {
        Assert.AreEqual(0.0, PacesetterModel.MissedSeconds(0.0167, 0.0167, 1.0), 1e-9);
        Assert.AreEqual(0.0, PacesetterModel.MissedSeconds(0.5, 0.1, 1.0), 1e-9, "1秒未満の揺れは補わない");
        Assert.AreEqual(29.98, PacesetterModel.MissedSeconds(30.0, 0.02, 1.0), 1e-9);
        Assert.AreEqual(0.0, PacesetterModel.MissedSeconds(0.1, 0.3, 3.0), 1e-9, "E2E の早回し");
    }

    [Test]
    public void 通過のときは横を通る()
    {
        Assert.AreEqual(0.8, PacesetterModel.PassingLateralMeters(0.0, 0.8), 1e-9);
        Assert.AreEqual(0.8, PacesetterModel.PassingLateralMeters(-1.0, 0.8), 1e-9);
        Assert.AreEqual(0.4, PacesetterModel.PassingLateralMeters(2.0, 0.8), 1e-9);
        Assert.AreEqual(0.0, PacesetterModel.PassingLateralMeters(3.0, 0.8), 1e-9);
        Assert.AreEqual(0.0, PacesetterModel.PassingLateralMeters(-5.0, 0.8), 1e-9);
    }
}

[TestFixture]
public class RunnerDistanceEstimatorTests
{
    private const double Dt = 1.0 / 60.0;

    [Test]
    public void GPSが1秒ごとでも距離は滑らかに進む()
    {
        var estimator = new RunnerDistanceEstimator();
        estimator.Reset(0.0);
        double authoritative = 0.0;
        double previous = 0.0;
        double maxStep = 0.0;

        for (int i = 1; i <= 60 * 30; i++)
        {
            if (i % 60 == 0) authoritative = i * Dt * 3.6;
            double position = estimator.Update(Dt, 3.6 * Dt, authoritative);
            maxStep = System.Math.Max(maxStep, position - previous);
            previous = position;
        }

        Assert.Less(maxStep, 0.1, "1秒ごとの3.6mの段差を出さない");
        Assert.AreEqual(3.6 * 30.0, previous, 0.5);
    }

    [Test]
    public void フレームの移動がずれていても正規の距離へ寄る()
    {
        var estimator = new RunnerDistanceEstimator();
        estimator.Reset(0.0);
        double position = 0.0;
        for (int i = 1; i <= 60 * 60; i++)
        {
            double authoritative = System.Math.Floor(i / 60.0) * 3.6;
            position = estimator.Update(Dt, 3.0 * Dt, authoritative);
        }
        Assert.AreEqual(3.6 * 60.0, position, 2.0);
    }

    [Test]
    public void テレポートは移動に数えない()
    {
        var estimator = new RunnerDistanceEstimator();
        estimator.Reset(10.0);
        Assert.AreEqual(10.0, estimator.Update(Dt, 50.0, 10.0), 1e-9);
    }

    [Test]
    public void 数でない値は無視する()
    {
        var estimator = new RunnerDistanceEstimator();
        estimator.Reset(10.0);
        Assert.AreEqual(10.0, estimator.Update(double.NaN, 0.1, 10.0), 1e-9);
        Assert.AreEqual(10.0, estimator.Update(Dt, double.NaN, double.NaN), 1e-9);
    }
}

[TestFixture]
public class StartLineCheckpointTests
{
    private const double Lat = 34.69;
    private const double Lon = 135.19;
    private const double MetersPerDegree = 111320.0;

    /// <summary>スタート地点から北へ meters 離れた点</summary>
    private static double North(double meters) => Lat + meters / MetersPerDegree;

    [Test]
    public void スタート地点へ戻ったところで周の距離との差を出す()
    {
        var checkpoint = new StartLineCheckpoint(400.0);
        Assert.IsFalse(checkpoint.Report(Lat, Lon, 3f, 0.0, out _));

        // 距離の推定が 8m 長い: 実際の1周(スタート地点)を 408m と数えている
        double? correction = null;
        for (double user = 370.0; user <= 445.0; user += 1.0)
        {
            double fromLine = System.Math.Abs(user - 408.0);
            if (checkpoint.Report(North(fromLine), Lon, 3f, user, out double delta))
                correction = delta;
        }

        Assert.IsTrue(correction.HasValue);
        Assert.AreEqual(-8.0, correction.Value, 0.01);
    }

    [Test]
    public void 周ごとに1回だけ出す()
    {
        var checkpoint = new StartLineCheckpoint(400.0);
        checkpoint.Report(Lat, Lon, 3f, 0.0, out _);

        int count = 0;
        for (double user = 1.0; user <= 900.0; user += 1.0)
        {
            double lapOffset = user % 400.0;
            double fromLine = System.Math.Min(lapOffset, 400.0 - lapOffset);
            if (checkpoint.Report(North(fromLine), Lon, 3f, user, out _)) count++;
        }
        Assert.AreEqual(2, count);
    }

    [Test]
    public void スタート地点に近づかなかった周は補正しない()
    {
        var checkpoint = new StartLineCheckpoint(400.0);
        checkpoint.Report(Lat, Lon, 3f, 0.0, out _);

        for (double user = 370.0; user <= 445.0; user += 1.0)
            Assert.IsFalse(checkpoint.Report(North(StartLineCheckpoint.AcceptMeters + 20.0), Lon, 3f, user, out _));
    }

    [Test]
    public void 精度の悪い測位は使わない()
    {
        var checkpoint = new StartLineCheckpoint(400.0);
        Assert.IsFalse(checkpoint.Report(Lat, Lon, StartLineCheckpoint.MaxAccuracyMeters + 1f, 0.0, out _));
        Assert.IsFalse(checkpoint.HasOrigin, "悪い測位をスタート地点にしない");
    }

    [Test]
    public void 周の長さが無ければ何もしない()
    {
        var checkpoint = new StartLineCheckpoint(0.0);
        Assert.IsFalse(checkpoint.Report(Lat, Lon, 3f, 0.0, out _));
        Assert.IsFalse(checkpoint.Report(Lat, Lon, 3f, 500.0, out _));
    }
}

[TestFixture]
public class PacesetterSoundTests
{
    [Test]
    public void R33_足音のテンポは速さで決まる()
    {
        Assert.AreEqual(1.4f + 3.6f * 0.35f, PacesetterSound.StepsPerSecond(3.6f), 1e-5f);
        Assert.AreEqual(1.6f, PacesetterSound.StepsPerSecond(0f), 1e-5f);
        Assert.AreEqual(3.2f, PacesetterSound.StepsPerSecond(20f), 1e-5f);
    }

    [Test]
    public void すぐ前は足音がはっきり呼吸は控えめ()
    {
        var mix = PacesetterSound.Evaluate(PacesetterState.Run, 2f);
        Assert.AreEqual(1f, mix.Footstep, 1e-5f);
        Assert.Less(mix.Breath, 0.5f);
        Assert.Greater(mix.Breath, 0f);
    }

    [Test]
    public void R31_3mから10mは連続して小さくなる()
    {
        float previous = PacesetterSound.Evaluate(PacesetterState.Run, 3f).Footstep;
        Assert.AreEqual(1f, previous, 1e-5f);
        for (float gap = 3.5f; gap < 10f; gap += 0.5f)
        {
            float current = PacesetterSound.Evaluate(PacesetterState.Run, gap).Footstep;
            Assert.Less(current, previous);
            Assert.Greater(previous - current, 0f);
            Assert.Less(previous - current, 0.1f, "段差が出ない");
            previous = current;
        }
    }

    [Test]
    public void WAIT中は足音が止まる()
    {
        var mix = PacesetterSound.Evaluate(PacesetterState.Wait, 10f);
        Assert.AreEqual(0f, mix.Footstep, 1e-5f);
        Assert.Greater(mix.Breath, 0f);
    }

    [Test]
    public void 開始前は鳴らない()
    {
        var mix = PacesetterSound.Evaluate(PacesetterState.Start, 3f);
        Assert.AreEqual(0f, mix.Footstep, 1e-5f);
        Assert.AreEqual(0f, mix.Breath, 1e-5f);
    }

    [Test]
    public void R30_追い抜いたあとは近いほど大きくやがて消える()
    {
        var near = PacesetterSound.Evaluate(PacesetterState.Overtaken, -0.5f);
        var far = PacesetterSound.Evaluate(PacesetterState.Overtaken, -8f);
        var gone = PacesetterSound.Evaluate(PacesetterState.Overtaken, -PacesetterSound.SilentBehindMeters);

        Assert.Greater(near.Footstep, 0.9f);
        Assert.Greater(near.Breath, 0.9f);
        Assert.Less(far.Footstep, near.Footstep);
        Assert.AreEqual(0f, gone.Footstep, 1e-5f);
        Assert.AreEqual(0f, gone.Breath, 1e-5f);
    }
}
