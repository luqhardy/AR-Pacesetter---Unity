# Avatar Animator Setup & State Transitions Guide

This guide details how to configure the downloaded Mixamo/Adobe humanoid model (`avatar-adobe.fbx`) and construct the state machine inside the Unity Animator Controller to support smooth movement blending and state transitions.

---

## 1. Importing the Model & Assets

### A. Rig Configuration
1. Select `avatar-adobe.fbx` in the Unity **Project** panel.
2. In the **Inspector**, switch to the **Rig** tab.
3. Change **Animation Type** to **Humanoid** and click **Apply**.

### B. Extracting Textures & Materials
1. Switch to the **Materials** tab in the Inspector.
2. Click **Extract Textures...** and save them in `Assets/Textures`.
3. Click **Extract Materials...** and save them in `Assets/Materials`.
   * *This allows the bio-luminescence script to modify emission colors dynamically.*

### C. Scene Integration
1. Locate the `Avatar_Container` GameObject in the scene hierarchy.
2. Drag `avatar-adobe.fbx` onto `Avatar_Container` to make it a child.
3. Select the parent `Avatar_Container` GameObject.
4. In the **AvatarModelSwitcher** component inspector, assign this child GameObject to the **Custom VRChat Object** field.

---

## 2. Animator Parameters & Triggers

Create a new Animator Controller (e.g. `AvatarAnimatorController`), assign it to your avatar's Animator component, and add the following parameters in the **Parameters** tab:

| Name | Type | Purpose |
| :--- | :--- | :--- |
| `Speed` | **Float** | Real-time movement speed (blends Idle $\rightarrow$ Walk $\rightarrow$ Run) |
| `IsHalted` | **Bool** | Pushed by `GroundSnap` when a LiDAR obstacle or cliff is encountered |
| `Overtaken` | **Trigger** | Fired when the user runs faster than the companion for $\ge 1.5$s |
| `Sprint` | **Trigger** | Fired when the companion surges to avoid being passed |
| `RunResume` | **Trigger** | Restores locomotion once overtake events are completed |
| `Nod` | **Trigger** | Played once GPS signal re-establishes and accuracy settles |
| `Beckon` | **Trigger** | Fired when the user falls $\ge 10$m behind — avatar holds position and beckons (resumes at 7m) |

---

## 3. Designing the States & Transitions

### State A: `Locomotion` (1D Blend Tree)
Create a 1D Blend Tree driven by the `Speed` parameter with these motion thresholds:
* **Speed = 0.0**: Idle Animation
* **Speed = 1.5**: Walk Animation
* **Speed = 3.0**: Run / Jog Animation
* **Speed = 4.5+**: Sprint Animation
* *Ensure **Foot IK** is enabled on all motions to prevent foot sliding.*

### State B: `InPlaceHalt` (In-Place Jog/Step)
Plays an in-place jog or step cycle when forward progression is blocked by a wall or cliff.
* **Locomotion $\rightarrow$ InPlaceHalt**:
  * **Has Exit Time:** Uncheck
  * **Transition Duration:** `0.2`s
  * **Conditions:** `IsHalted` == `true`
* **InPlaceHalt $\rightarrow$ Locomotion**:
  * **Has Exit Time:** Uncheck
  * **Transition Duration:** `0.25`s
  * **Conditions:** `IsHalted` == `false`

### State C: `BeingOvertaken` (Yield / Side-Look)
Plays the yield / look-at-user animation when the user passes the companion.
* **Locomotion $\rightarrow$ BeingOvertaken**:
  * **Has Exit Time:** Uncheck
  * **Transition Duration:** `0.25`s
  * **Conditions:** `Overtaken` (Trigger)
* **BeingOvertaken $\rightarrow$ Locomotion**:
  * **Has Exit Time:** Uncheck
  * **Transition Duration:** `0.3`s
  * **Conditions:** `RunResume` (Trigger)

### State D: `SprintSurge` (Overtake Surge)
Plays a rapid sprint animation during catch-up bursts or avoidance surges.
* **Locomotion $\rightarrow$ SprintSurge**:
  * **Has Exit Time:** Uncheck
  * **Transition Duration:** `0.2`s
  * **Conditions:** `Sprint` (Trigger)
* **SprintSurge $\rightarrow$ Locomotion**:
  * **Has Exit Time:** Uncheck
  * **Transition Duration:** `0.3`s
  * **Conditions:** `RunResume` (Trigger)

### State E: `Nod` (GPS Confirmation Nod)
Plays a single nod gesture when GPS accuracy finishes re-accumulating.
* **Any State $\rightarrow$ Nod**:
  * **Has Exit Time:** Uncheck
  * **Transition Duration:** `0.1`s
  * **Conditions:** `Nod` (Trigger)
* **Nod $\rightarrow$ Locomotion**:
  * **Has Exit Time:** **Check** (Plays full animation)
  * **Exit Time:** `0.9` (Transitions at 90% completion)
  * **Transition Duration:** `0.25`s
  * **Conditions:** *None*
