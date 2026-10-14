// swift-tools-version: 5.9
//
// iOS アプリの UI に依存しない純ロジック(AR_Runner_UI/AR_Runner_UI/AppLogic)のテスト用パッケージ。
// アプリ本体は Xcode が同じファイルをそのままコンパイルする(このパッケージはテストのためだけ)。
//
//   swift test --package-path ios
//
// Foundation だけで書いてあるので macOS / Linux / Windows のどれでも動く。CI は
// .github/workflows/swift-logic-tests.yml、ローカルは tools/verify.sh が実行する。
import PackageDescription

let package = Package(
    name: "ARRunnerAppLogic",
    targets: [
        .target(
            name: "ARRunnerAppLogic",
            path: "AR_Runner_UI/AR_Runner_UI/AppLogic"
        ),
        .testTarget(
            name: "ARRunnerAppLogicTests",
            dependencies: ["ARRunnerAppLogic"],
            path: "AppLogicTests"
        ),
    ]
)
