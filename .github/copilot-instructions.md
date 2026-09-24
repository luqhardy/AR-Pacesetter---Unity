# GitHub Copilot instructions

The instructions for this repository live in **[AGENTS.md](../AGENTS.md)** at the repo root — the single source
shared by every AI agent and developer. **Read AGENTS.md before answering questions about this code or making changes.**
This file only points there; do not add rules here (edit AGENTS.md instead).

The essentials, in case AGENTS.md is not in your context:

- **What this is**: an AR running pacer. A Unity project (repo root, `Assets/_Project/Scripts/`) embedded as a library in a SwiftUI iOS app (`ios/`). Phase 1 scope is iPhone + XREAL AR glasses only; Apple Watch / HealthKit / ghost features exist but are out of scope.
- **Verify every change**: run `tools/verify.sh` (Windows Git Bash or macOS) and finish only when it prints `ALL CHECKS PASSED` (exit code 0). It runs the C# compile check, unit tests, Swift syntax check and the Unity E2E scenario. Close the Unity editor first — E2E fails if the project is open.
- **Before committing**: add an entry of at most 5 lines to the top of `CHANGELOG.md`; write the commit message in Japanese and include the verification results.
- **Code conventions**: put pure logic in dependency-free static classes (like `PaceMath`) with unit tests in `Tests/UnitTests`; get Animators via `AvatarRigLocator.FindBestAnimator`; use `FrameSmoothing.Factor` instead of `Lerp(a, b, Time.deltaTime * k)`.
- **Open team decisions** (AGENTS.md §7): do not resolve them on your own.
