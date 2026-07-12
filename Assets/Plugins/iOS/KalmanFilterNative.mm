// AR Vision — C++カルマンフィルタ (iOSネイティブ実装)
//
// AvatarEngine.cs の DllImport("__Internal") が要求するシンボルの実体。
// これが無いと iOS ビルドはリンクエラー(undefined symbol)で失敗する。
//
// 仕様 (AGENTS.md §3 / 要件定義 6.1):
//   - 3軸独立のスカラーカルマンフィルタ(予測→更新)
//   - 既定パラメータ: processNoise Q=0.05, measurementNoise R=0.8
//   - lteWeight: 線形トレンド(速度)推定のブレンド係数。ジッター時の
//     予測補間(1フレーム先読み)を滑らかにする
//
// 呼び出し規約: C# 側 `out float` は float* として渡される。

#import <Foundation/Foundation.h>

namespace {

struct AxisState {
    float x = 0.0f;   // 状態推定値
    float p = 1.0f;   // 誤差共分散
    float v = 0.0f;   // 速度(線形トレンド)推定
    bool  seeded = false;
};

float g_processNoise = 0.05f;
float g_measurementNoise = 0.80f;
float g_lteWeight = 0.12f;
AxisState g_axes[3];

inline float StepAxis(AxisState &s, float measurement)
{
    if (!s.seeded) {
        // 初回は測定値で初期化(起動直後の大きな引き込みを防ぐ)
        s.x = measurement;
        s.p = 1.0f;
        s.v = 0.0f;
        s.seeded = true;
        return s.x;
    }

    float previous = s.x;

    // 予測: 線形トレンドで1ステップ先読みし、プロセスノイズを加算
    float predicted = s.x + s.v;
    s.p += g_processNoise;

    // 更新: カルマンゲインで測定値を取り込む
    float gain = s.p / (s.p + g_measurementNoise);
    s.x = predicted + gain * (measurement - predicted);
    s.p *= (1.0f - gain);

    // 速度推定を lteWeight でゆっくり追従させる(高周波ジッターを渡さない)
    s.v = s.v + g_lteWeight * ((s.x - previous) - s.v);

    return s.x;
}

} // namespace

extern "C" {

void InitKalmanFilter(float processNoise, float measurementNoise, float lteWeight)
{
    g_processNoise = processNoise > 0.0f ? processNoise : 0.05f;
    g_measurementNoise = measurementNoise > 0.0f ? measurementNoise : 0.80f;
    g_lteWeight = (lteWeight >= 0.0f && lteWeight <= 1.0f) ? lteWeight : 0.12f;

    for (int i = 0; i < 3; i++) {
        g_axes[i] = AxisState();
    }

    NSLog(@"[KalmanNative] Initialized (Q=%.3f R=%.3f LTE=%.3f)",
          g_processNoise, g_measurementNoise, g_lteWeight);
}

void UpdateKalmanFilter(float rawX, float rawY, float rawZ,
                        float *smoothX, float *smoothY, float *smoothZ)
{
    if (smoothX) *smoothX = StepAxis(g_axes[0], rawX);
    if (smoothY) *smoothY = StepAxis(g_axes[1], rawY);
    if (smoothZ) *smoothZ = StepAxis(g_axes[2], rawZ);
}

} // extern "C"
