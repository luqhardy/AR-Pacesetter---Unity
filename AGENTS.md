### Component Roles
1. **iPhone (Core Engine)**: Handles all spatial processing, sensor fusion, state management, and command generation for rendering.
2. **AR Glass (Visual Output)**: XREAL One / Eye. Projects the spatial avatar and HUD overlay. Provides raw IMU data back to the core.
3. **Apple Watch (Biometric Sensing)**: Sources real-time heart rate (bpm) and running pitch.

### Language & Framework Breakdown
- **Swift**: Core UI/UX, device cross-communication (BLE, USB-C I/O), and high-level ARKit/Metal lifecycle orchestration.
- **C++ (Performance Layer)**: Computational geometry bottlenecks, Kalman filtering, and low-latency state extrapolation.

---

## 3. Motion-to-Photon Latency & Timing Budget

To prevent motion sickness and maintain absolute sync, a strict **$\\le 20\text{ms}$ Motion-to-Photon budget** is enforced. 

### Latency Allocation Breakdowns
- **IMU Data Acquisition (USB-C)**: $\\le 2\text{ms}$
- **Kalman Filter Sensor Fusion (C++)**: $\\le 4\text{ms}$
- **AR Frame Command Generation (Metal/ARKit)**: $\\le 6\text{ms}$
- **USB-C Frame Transmission to Glass**: $\\le 8\text{ms}$
- **Total Pipeline Target**: **$\\le 20\text{ms}$**

### Core Sampling and Rendering Constraints
- **IMU Sampling Rate**: $\\ge 100\text{Hz}$ over wired USB-C from the AR Glass (captures head pose and acceleration).
- **HUD Frame Rate**: Stable $60\text{fps}$ ($16.6\text{ms}$ per frame refresh).
- **Jitter Tolerance**: Consecutive frame-to-frame delta variation must be within $\\pm 5\text{ms}$. If jitter exceeds this threshold, the system must immediately discard raw measurements and prioritize predictive state interpolation via the C++ Kalman Filter.

---

## 4. Core Mathematical Formulae & Geometric Domain Logic

### 4.1 Avatar Position Calculation (Vector_Forward Purification)
To prevent the avatar from swaying side-to-side due to instantaneous head or eye movements (which causes motion sickness), the forward heading vector must be calculated using a moving average of the iPhone's tracking data rather than the glass's built-in compass.

$$\\text{Position}_{\\text{Avatar}} = \\text{Position}_{\\text{User}} + 3.0 \\times \\vec{V}_{\\text{Forward}}$$

**Algorithm Constraints:**
- $\\vec{V}_{\\text{Forward}}$ must be calculated by integrating the last **1.5 seconds** of the iPhone's GPS movement vector combined with IMU acceleration data.
- Apply a continuous **Moving Average** smoothing filter to eliminate high-frequency eye/head tremors.

### 4.2 Ground Snap & Vertical Smoothing (LiDAR Enhanced)
Ensures the avatar stays realistically stuck to the terrain surface.
- **Vertical Threshold**: If the calculated surface delta change $\\Delta z > \\pm 15\text{cm}$, do not snap instantly. The position must transition smoothly using an easing function over exactly **0.3 seconds**.
- **Cliff/Obstacle Exception**: If a vertical cliff or solid wall obstruction $\\ge 1.5\text{m}$ high is detected within **3.0 meters** ahead of the user via LiDAR, the avatar's forward progression must halt immediately. The avatar enters a face-to-face "In-Place Jog/Step" state, preventing it from clipping into geometry, and waits until the user returns to a clear route.

### 4.3 Analytics & Environmental Models
- **Synchronicity Rate ($S$)**: Calculated over increments of $1\text{km}$ and $5\text{km}$. Represents the approximation profile between the user's targeted pace and their actual current pace. Distance deviation of $\\ge 10\text{m}$ drops Synchronicity instantly to $0\\%$. At run completion, output a grade from **S to D**.
- **Fatigue Correction Coefficient ($C_f$)**: Modifies biometric strain assumptions based on ambient temperature:
  $$C_f = \\begin{cases} 1.0 & T < 28^\\circ\\text{C} \\\\ 1.5 & 28^\\circ\\text{C} \\le T < 31^\\circ\\text{C} \\\\ 2.0 & T \\ge 31^\\circ\\text{C} \\end{cases}$$

---

## 5. State Machine Lifecycle (GPS Fault Tolerance)

Autonomous implementation agents must build the core loop around the following finite state machine handling GPS dropouts and relocalization sequences.

```mermaid
stateDiagram-v2
    [*] --> Normal
    
    Normal --> InertialMovement : GPS Signal Lost
    InertialMovement --> Normal : GPS Signal Restored
    
    InertialMovement --> FadeOut : Timeout (Signal Lost for 5s)
    FadeOut --> Normal : GPS Signal Restored (Before 1s Complete)
    
    FadeOut --> Standby : Fade-Out Complete (1s Elapsed, Opacity = 0%)
    
    Standby --> ReAccumulation : GPS Signal Restored
    ReAccumulation --> Normal : Accuracy Radius <= 5m AND Position Settled (1.5s Animation)
---

## 6. Implementation Status & Working Agreements (updated 2026-07-14)

上記§3〜5の数式・FSM・レイテンシ予算は引き続き正であり、全て実装済み。
機能→実装→検証の対応表は [HANDOVER.md](HANDOVER.md)、Swift⇄Unity契約は
[SWIFT_INTEGRATION.md](SWIFT_INTEGRATION.md) が一次情報。

### 変更時の検証ワークフロー(必須)
1. **C#コンパイル**: Unityを開かず `dotnet build`(README更新履歴のcsproj生成手法)
2. **E2E回帰(37項目)**: `Unity.exe -batchmode -projectPath <repo> -executeMethod E2EScenarioRunner.Run -logFile e2e.log` → 終了コード0を確認。シナリオ追加は `E2EScenarioBehaviour.cs`
3. **Swift構文**: `swiftc -parse`(Windowsツールチェーン導入済み。型検査はMacでのみ可能)
4. コミット前にREADME更新履歴へ1エントリ追記

### 実装上の不変条件(破ると壊れる)
- **`AvatarEngine.IsHalted` は `GroundSnap` が毎フレーム上書きする**。恒久的な停止には `IsSessionEnded` を使う
- **距離の単一ソース原則**: Swift主導中(`ARSessionManagerBridge.ExternalMetricsActive`)はUnity内部計測をスプリット判定へ流さない。記録・ゴール判定は `RunSessionController.AuthoritativeDistanceMeters`(新鮮なGPS優先・5秒でUnity計測へフォールバック)を共用
- **アバターは常時「ユーザー+3m」アンカー追従**(自走ではない)。10m離隔待機は「アバター停止中にユーザーが離れる」場合にのみ発生する
- **Animator取得は必ず `AvatarRigLocator.FindBestAnimator`** — コンテナに無効化された旧Animatorが残っており、素の `GetComponentInChildren<Animator>` はそれを拾う
- **AnimatorControllerはジェネレータ管理**(`AvatarAnimatorControllerGenerator`)。手編集せず再生成する(同一パスならGUID維持でシーン参照は無傷)
- **UnitySendMessage対象のGameObject名は固定**: "ARSessionManager" / "DeviceManager"(Bootstrapが自動生成)
- 新規マネージャーは `ARVisionSystemsBootstrap` に登録すればシーン配線不要

### ペース単位の規約
Swift⇄ブリッジ境界は km/h、Unity内部は 分/km(変換: 分/km = 60 ÷ km/h)。
