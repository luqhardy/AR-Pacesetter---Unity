#include <cmath>

// Ensures C++ function names aren't mangled so C# can find them via P/Invoke
extern "C" {

    // Placeholder variables for the Kalman state
    static float estimateX = 0.0f;
    static float estimateY = 0.0f;
    static float estimateZ = 0.0f;
    static float processNoise = 0.1f;

    void UpdateKalmanFilter(float accelX, float accelY, float accelZ, float* smoothX, float* smoothY, float* smoothZ)
    {
        // Simple low-pass smoothing placeholder acting as our ultra-low latency C++ pipeline.
        // This will be replaced with your full state-space GPS + IMU matrix algorithm.
        estimateX += (accelX - estimateX) * processNoise;
        estimateY += (accelY - estimateY) * processNoise;
        estimateZ += (accelZ - estimateZ) * processNoise;

        // Output coordinates back to Unity C#
        *smoothX = estimateX;
        *smoothY = estimateY;
        *smoothZ = estimateZ;
    }
}
