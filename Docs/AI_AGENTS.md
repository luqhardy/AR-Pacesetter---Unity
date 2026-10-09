# Using AI coding agents on this repo

Every agent — Claude Code, OpenAI Codex, Cursor, GitHub Copilot, Gemini CLI, and anything else — follows the
same rules, which live in one file: **[AGENTS.md](../AGENTS.md)**. The tool-specific files only point to it,
so a rule changed in AGENTS.md reaches every tool at once.

## Which file each tool reads

| Tool | Reads | Setup needed |
|---|---|---|
| OpenAI Codex (CLI / cloud) | `AGENTS.md` | none |
| Cursor | `AGENTS.md` | none |
| GitHub Copilot (VS Code chat, coding agent) | `.github/copilot-instructions.md` → points to `AGENTS.md`; the coding agent also reads `AGENTS.md` directly | none |
| Claude Code | `CLAUDE.md` → imports `AGENTS.md` (`@AGENTS.md`) | none |
| Gemini CLI | `GEMINI.md` → imports `AGENTS.md` (`@./AGENTS.md`) | none |
| Windsurf, Zed, Jules and other tools that support the AGENTS.md convention | `AGENTS.md` | none |
| Aider | nothing by default | run `aider --read AGENTS.md`, or add `read: AGENTS.md` to your own `.aider.conf.yml` |

Tool support changes quickly. To confirm your agent actually loaded the rules, ask it
*"How do I verify a change in this repo?"* — it should answer `tools/verify.sh`. If it doesn't, point it at
`AGENTS.md` explicitly at the start of the session.

## What your machine needs so the agent can verify its work

Agents are told to finish only when `tools/verify.sh` prints `ALL CHECKS PASSED`. That script needs:

| Requirement | Windows | macOS |
|---|---|---|
| Shell | Git Bash (comes with Git for Windows) | Terminal |
| .NET SDK 8 | `winget install Microsoft.DotNet.SDK.8` | `brew install --cask dotnet-sdk` |
| Unity 6000.3.17f1 (via Unity Hub, default install location) | ✓ | ✓ |
| Project opened in Unity at least once (creates `Library/`) | ✓ | ✓ |
| Swift (syntax check only) | `winget install Swift.Toolchain` | Xcode or Command Line Tools |

If Unity or Swift is installed somewhere else, run e.g. `UNITY_PATH=/path/to/Unity tools/verify.sh`
(or `SWIFTC=...`). The script skips the Swift step with a message if no compiler is found.

**Close the Unity editor before running the full check** — the E2E step opens the project in batch mode and
fails immediately if the editor already has it open. Use `tools/verify.sh --fast` to skip E2E while iterating
(seconds instead of minutes), then run the full script before committing.

The unit tests also run automatically on every pull request (`.github/workflows/unit-tests.yml`).

## What agents cannot check

- **iOS type checking, linking and device behaviour** need a Mac with Xcode. `ios-build.yml` in CI compiles and
  links without signing; running on an iPhone needs the one-time Mac setup in
  [SWIFT_INTEGRATION.md](../SWIFT_INTEGRATION.md) ② and a person.
- **Real latency (M2P), GPS behaviour and how the avatar looks** can only be judged on a device or in the editor
  by a person. See [FIELD_TEST_PLAN.md](FIELD_TEST_PLAN.md).
- **Open team decisions** (AGENTS.md §7) are for the team. Agents are told not to resolve them on their own.

## Changing the rules

1. Edit **AGENTS.md** only. `CLAUDE.md` and `GEMINI.md` import it, so they update automatically.
2. `.github/copilot-instructions.md` carries a 5-point summary for Copilot chat sessions that don't open
   AGENTS.md. If you change one of those points (what the project is, `tools/verify.sh`, the commit rules,
   the code conventions listed there, or the open decisions), update that summary too.
3. Keep tool-specific quirks out of AGENTS.md; put them in the tool's own file (e.g. the Claude Code section
   at the bottom of `CLAUDE.md`).
