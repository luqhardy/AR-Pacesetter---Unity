import Foundation

var assertions = 0
func expect(_ actual: Double?, _ expected: Double, _ label: String) {
    guard let actual, abs(actual - expected) < 0.000001 else {
        fatalError("\(label): expected \(expected), got \(String(describing: actual))")
    }
    assertions += 1
}
let identity: [SIMD3<Double>] = [SIMD3(1, 0, 0), SIMD3(0, 1, 0), SIMD3(0, 0, 1)]
let down = SIMD3<Double>(0, 0, -1)
expect(HeadTrackingProbeMath.heading(rows: identity, axis: SIMD3(1, 0, 0), gravity: down), 0, "reference forward")
// Reference-to-device matrix for a clockwise 90-degree physical turn.
let right: [SIMD3<Double>] = [SIMD3(0, -1, 0), SIMD3(1, 0, 0), SIMD3(0, 0, 1)]
expect(HeadTrackingProbeMath.heading(rows: right, axis: SIMD3(1, 0, 0), gravity: down), 90, "right positive")
let left: [SIMD3<Double>] = [SIMD3(0, 1, 0), SIMD3(-1, 0, 0), SIMD3(0, 0, 1)]
expect(HeadTrackingProbeMath.heading(rows: left, axis: SIMD3(1, 0, 0), gravity: down), -90, "left negative")
// Upright phone: local +Y is reference up; camera -Z points along reference +X.
let chest: [SIMD3<Double>] = [SIMD3(0, -1, 0), SIMD3(0, 0, 1), SIMD3(-1, 0, 0)]
expect(HeadTrackingProbeMath.heading(rows: chest, axis: SIMD3(0, 0, -1), gravity: SIMD3(0, -1, 0)), 0, "upright phone camera")
// Gravity projection discards the vertical component of a tilted axis.
expect(HeadTrackingProbeMath.heading(rows: identity, axis: SIMD3(1, 0, 1), gravity: down), 0, "tilt invariant")
assert(HeadTrackingProbeMath.heading(rows: identity, axis: SIMD3(0, 0, 1), gravity: down) == nil)
assert(HeadTrackingProbeMath.heading(rows: identity, axis: SIMD3(1, 0, 0), gravity: .zero) == nil)
expect(HeadTrackingProbeMath.wrap(181), -179, "wrap upper")
expect(HeadTrackingProbeMath.wrap(-181), 179, "wrap lower")
expect(HeadTrackingProbeMath.wrap(540), -180, "wrap multiple turns")
expect(HeadTrackingProbeMath.wrap((-170 - 170) - (15 - 5)), 10, "neck wrap through boundary")
expect(HeadTrackingProbeMath.wrap((90 - 20) - (100 - 30)), 0, "head and body turn together")
let stats = HeadTrackingProbeMath.intervalStatistics([0.01, 0.02, 0.03])
expect(stats.maximum, 0.03, "maximum interval")
expect(stats.deviation, sqrt(0.0002 / 3), "population standard deviation")
assert(HeadTrackingProbeMath.intervalStatistics([]).maximum == nil)
print("HeadTrackingProbe math: \(assertions) numeric checks and 3 invalid/empty checks passed")
