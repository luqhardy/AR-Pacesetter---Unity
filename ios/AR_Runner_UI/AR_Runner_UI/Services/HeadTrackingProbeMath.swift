import Foundation

/// Pure measurement math, independent of Unity and Core Motion objects.
enum HeadTrackingProbeMath {
    static func wrap(_ degrees: Double) -> Double {
        let value = (degrees + 180).truncatingRemainder(dividingBy: 360)
        return (value < 0 ? value + 360 : value) - 180
    }

    /// Core Motion's reference-to-device matrix is inverted (transposed) to
    /// express a device-local axis in reference coordinates. Rows are passed in.
    static func heading(rows: [SIMD3<Double>], axis: SIMD3<Double>,
                        gravity: SIMD3<Double>) -> Double? {
        func transform(_ v: SIMD3<Double>) -> SIMD3<Double> {
            rows[0] * v.x + rows[1] * v.y + rows[2] * v.z
        }
        func dot(_ a: SIMD3<Double>, _ b: SIMD3<Double>) -> Double {
            a.x * b.x + a.y * b.y + a.z * b.z
        }
        func unit(_ v: SIMD3<Double>) -> SIMD3<Double>? {
            let length = sqrt(dot(v, v))
            guard length.isFinite, length > 0.001 else { return nil }
            return v / length
        }
        guard rows.count == 3, let up = unit(-transform(gravity)) else { return nil }
        let forward = transform(axis)
        let projected = forward - up * dot(forward, up)
        guard let horizontal = unit(projected) else { return nil }
        // Establish a horizontal basis even if the reference's Z is not vertical.
        let seed = abs(up.x) < 0.9 ? SIMD3<Double>(1, 0, 0) : SIMD3<Double>(0, 1, 0)
        guard let x = unit(seed - up * dot(seed, up)) else { return nil }
        let y = SIMD3(up.y * x.z - up.z * x.y,
                      up.z * x.x - up.x * x.z,
                      up.x * x.y - up.y * x.x)
        // Clockwise viewed from above is positive (turning right).
        return wrap(-atan2(dot(horizontal, y), dot(horizontal, x)) * 180 / .pi)
    }

    static func intervalStatistics(_ values: [Double]) -> (maximum: Double?, deviation: Double?) {
        guard !values.isEmpty else { return (nil, nil) }
        let mean = values.reduce(0, +) / Double(values.count)
        let variance = values.reduce(0) { $0 + ($1 - mean) * ($1 - mean) } / Double(values.count)
        return (values.max(), sqrt(variance))
    }
}
