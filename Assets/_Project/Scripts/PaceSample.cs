using System;

/// <summary>
/// ゴースト機能のペース推移サンプル(企画書§3): ある時刻の累積距離。
/// Unity非依存のPOCO([Serializable]はSystem)。純ロジック(PaceMath)と
/// ユニットテストから参照できるよう独立ファイルに置く。
/// </summary>
[Serializable]
public class PaceSample
{
    public float t;      // 走行開始からの秒数
    public float meters; // その時点の累積距離
}
