# ARRunner — Swift Project Structure
# =====================================
# Xcode 15+  |  iOS 17+  |  Swift 5.9
# =====================================

#__How to set up unity__
#1.git clone from Lukuman's repository
#2.Unity->Projects->Add->Add from repository
#3.run download file
#__Then Unity Engine__
#1.file->Build Profile->iOS->Build
#2.run file named .xcodeproj
#3.Signing & capabilities-> Automatically box checked->Add Team name->Bundle identifier(give some name starting 'com.')
#================================================================


#
# File layout:
# ARRunner/
# ├── ARRunner.xcodeproj/   ← Open this in Xcode
# └── ARRunner/
#     ├── ContentView.swift         ← Root navigation + AppEntry (@main)
#     ├── DesignSystem.swift        ← Colors, ARButton, StatBadge, TabBar
#     ├── ConnectOnboardingView.swift ← Screen 1: XREAL接続, Screen 2: Onboarding
#     ├── CourseRunningView.swift   ← Screen 3: コース設定, Screen 4: 走行中AR
#     ├── StatsHistoryView.swift    ← Screen 5: 統計, Screen 6: 履歴
#     └── UnityBridge.swift         ← Unity ↔ Swift 双方向通信
#
# ── How to open ──────────────────────────────────────────
# 1. Open Xcode → File → New → Project → iOS App
# 2. Product Name: ARRunner  |  Interface: SwiftUI  |  Language: Swift
# 3. Delete the default ContentView.swift Xcode creates
# 4. Drag all .swift files from this folder into the project navigator
# 5. Build & Run on iPhone (iOS 17+)
#
# ── Unity as a Library integration ───────────────────────
# Follow: https://docs.unity3d.com/Manual/UnityasaLibrary-iOS.html
# 1. Unity Build Settings → iOS → enable "Unity as a Library"
# 2. Export Unity project → merge with this Xcode project
# 3. In UnityBridge.swift: uncomment the UnityFramework lines
# 4. Unity-side: add NativeCallProxy.mm and call
#    UnitySendMessage("UnityBridge", "onUnityMessage", jsonString)
#
# ── Required Xcode Capabilities ──────────────────────────
# • Location When In Use + Always (Info.plist descriptions required)
# • Motion & Fitness (CMMotionManager)
# • Bluetooth (NSBluetoothAlwaysUsageDescription)
# • HealthKit (Read: Heart Rate)
# • Background Modes: Location updates, External accessory communication
#
# ── Dependencies (Swift Package Manager) ─────────────────
# No external packages required for MVP.
# Optional: add swift-collections for performance-sensitive data structures.
