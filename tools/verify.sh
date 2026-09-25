#!/usr/bin/env bash
# 変更の検証を AGENTS.md §6 の順で実行する(Windows Git Bash / macOS 共通)。
#
#   tools/verify.sh            コンパイル → ユニットテスト → Swift構文 → E2E
#   tools/verify.sh --fast     E2E を省く(数十秒。E2Eは2〜10分かかる)
#
# 全ステップ成功で終了コード0。どこかで失敗したらそこで止まり、非0で終わる。
# E2E はUnityでプロジェクトを開いていると即失敗するので、先にエディタを閉じること。
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_VERSION="6000.3.17f1"
RUN_E2E=1
[ "${1:-}" = "--fast" ] && RUN_E2E=0

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    UNITY="${UNITY_PATH:-/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe}"
    SWIFTC="${SWIFTC:-$LOCALAPPDATA/Programs/Swift/Toolchains/6.3.3+Asserts/usr/bin/swiftc.exe}" ;;
  Darwin)
    UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity}"
    SWIFTC="${SWIFTC:-$(command -v swiftc || echo xcrun-swiftc)}" ;;
  *)
    UNITY="${UNITY_PATH:-$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity}"
    SWIFTC="${SWIFTC:-$(command -v swiftc || true)}" ;;
esac

step() { printf '\n== %s ==\n' "$1"; }
fail() { printf '\nFAILED: %s\n' "$1"; exit 1; }

step "1/4 C# compile (tools/compile-check)"
dotnet build "$ROOT/tools/compile-check" -nologo -v:q || fail "C# compile"

step "2/4 Unit tests (Tests/UnitTests)"
dotnet test "$ROOT/Tests/UnitTests" -nologo -v:q || fail "unit tests"
dotnet build "$ROOT/tools/analyze-run-log" -nologo -v:q || fail "run-log analyzer build"

step "3/4 Swift syntax (ios/)"
if [ -z "$SWIFTC" ] || { [ "$SWIFTC" != "xcrun-swiftc" ] && [ ! -x "$SWIFTC" ]; }; then
  echo "swiftc not found — skipped (set SWIFTC=<path> to enable)"
else
  n=0
  while IFS= read -r f; do
    n=$((n+1))
    if [ "$SWIFTC" = "xcrun-swiftc" ]; then xcrun swiftc -parse "$f"; else "$SWIFTC" -parse "$f"; fi \
      || fail "swift parse: $f"
  done < <(find "$ROOT/ios" -name '*.swift' -not -path '*/UnityExport/*')
  echo "parsed $n files (syntax only — type checking needs Xcode)"
fi

if [ "$RUN_E2E" = 1 ]; then
  step "4/4 E2E (Unity batchmode — takes a few minutes)"
  [ -x "$UNITY" ] || [ -f "$UNITY" ] || fail "Unity $UNITY_VERSION not found at $UNITY (set UNITY_PATH=...)"
  LOG="$ROOT/e2e.log"
  "$UNITY" -batchmode -projectPath "$ROOT" -executeMethod E2EScenarioRunner.Run -logFile "$LOG"
  code=$?
  grep -E '\[E2E\] (FAIL|SUMMARY)' "$LOG" || echo "(no [E2E] markers — see $LOG)"
  [ "$code" -eq 0 ] || fail "E2E (exit $code, log: $LOG)"
else
  step "4/4 E2E — skipped (--fast)"
fi

printf '\nALL CHECKS PASSED\n'
