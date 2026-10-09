/// <summary>
/// F-11 走行ログCSV(§5.2)の timestamp 列の採番。<b>単調増加を保証する</b>。
/// Unity非依存の純ロジック。<see cref="RunTelemetryLogger"/> が委譲する。
///
/// <para>行の時刻には2系統ある。実機のネイティブ100Hz IMUはサンプル自身の観測時刻、
/// それ以外(エディタ等)は10ms刻みの合成時刻。<b>以前の不具合</b>: 合成時刻は
/// 「ログ開始 + 合成行の連番×10ms」だったが、連番は合成行を書いたときしか進まない。
/// 実機でネイティブのサンプルが0件のフレームに合成行へ落ちると、走行の途中に
/// <b>開始直後の時刻</b>を持つ行が混ざった。解析ツールはその次の行を「数十分の欠落」と
/// 数えるため、T2のギャップ評価が壊れていた。</para>
///
/// <para>ここでは合成行を必ず直前に書いた行の後ろへ置き、ネイティブ行も前の行より
/// 古ければ捨てる(数えて残す)。</para>
/// </summary>
public sealed class TelemetryTimeline
{
    public const long IntervalMs = 10; // 100Hz

    private long _nextSyntheticMs;
    private long _lastWrittenMs;
    private bool _hasWritten;

    /// <summary>直前に書いた行の時刻(epoch ms)。未記録なら0。</summary>
    public long LastWrittenMs => _hasWritten ? _lastWrittenMs : 0;

    /// <summary>前の行より古いため捨てたネイティブサンプル数。</summary>
    public long RejectedNativeCount { get; private set; }

    public void Reset(long startEpochMs)
    {
        _nextSyntheticMs = startEpochMs;
        _lastWrittenMs = 0;
        _hasWritten = false;
        RejectedNativeCount = 0;
    }

    /// <summary>合成行の時刻を払い出す。10ms刻みで、既に書いた行より必ず後ろ。</summary>
    public long NextSynthetic()
    {
        long ts = _nextSyntheticMs;
        if (_hasWritten && ts <= _lastWrittenMs) ts = _lastWrittenMs + IntervalMs;
        _nextSyntheticMs = ts + IntervalMs;
        Mark(ts);
        return ts;
    }

    /// <summary>
    /// ネイティブサンプルの時刻を受け入れるか。前の行以前なら false(書かない)。
    /// 受け入れた場合は以後の合成行もこれより後ろへ置く。
    /// </summary>
    public bool TryAcceptNative(long ts)
    {
        if (_hasWritten && ts <= _lastWrittenMs)
        {
            RejectedNativeCount++;
            return false;
        }
        Mark(ts);
        if (_nextSyntheticMs <= ts) _nextSyntheticMs = ts + IntervalMs;
        return true;
    }

    private void Mark(long ts)
    {
        _lastWrittenMs = ts;
        _hasWritten = true;
    }
}
