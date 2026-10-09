using System.Collections.Generic;

/// <summary>
/// 直近 N 秒の水平移動から平均速度ベクトルを出す(F-09: GPSロスト時は「直前1秒の平均速度・方向」で
/// 慣性移動する)。Unity非依存の純ロジック。<see cref="AvatarEngine"/> が委譲する。
///
/// <para>以前の慣性移動は目標ペース(とエラスティックバンドの倍率)で進んでいたため、
/// ロスト直前に止まっていても・遅れていても、アバターだけ目標ペースで走り去っていた。</para>
/// </summary>
public sealed class RecentVelocityWindow
{
    /// <summary>平均を出すのに必要な最短の観測区間(秒)。これ未満は「分からない」。</summary>
    public const float MinSpanSeconds = 0.2f;

    private struct Sample
    {
        public float T, X, Z;
    }

    private readonly Queue<Sample> _samples = new Queue<Sample>();
    private readonly float _windowSeconds;

    public RecentVelocityWindow(float windowSeconds = 1f)
    {
        _windowSeconds = windowSeconds > 0f ? windowSeconds : 1f;
    }

    public float WindowSeconds => _windowSeconds;

    /// <summary>時刻 t(秒)の位置を追加する。窓より古いサンプルは捨てる。</summary>
    public void Add(float t, float x, float z)
    {
        _samples.Enqueue(new Sample { T = t, X = x, Z = z });
        while (_samples.Count > 1 && t - _samples.Peek().T > _windowSeconds)
            _samples.Dequeue();
    }

    /// <summary>窓内の平均速度(m/s)。観測区間が短すぎれば false。</summary>
    public bool TryGetAverageVelocity(out float vx, out float vz)
    {
        vx = vz = 0f;
        if (_samples.Count < 2) return false;

        Sample oldest = _samples.Peek();
        Sample newest = default;
        foreach (Sample s in _samples) newest = s;

        float span = newest.T - oldest.T;
        if (span < MinSpanSeconds) return false;

        vx = (newest.X - oldest.X) / span;
        vz = (newest.Z - oldest.Z) / span;
        return true;
    }

    public void Clear() => _samples.Clear();
}
