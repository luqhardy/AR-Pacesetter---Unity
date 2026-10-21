#!/usr/bin/env bash
# 変更の検証を AGENTS.md §6 の順で実行する(Windows Git Bash / macOS 共通)。
#
#   tools/verify.sh            コンパイル → ユニットテスト → Swift構文 → E2E
#   tools/verify.sh --fast     E2E を省く(数十秒。E2Eは2〜10分かかる)
#
# 全ステップ成功で終了コード0。どこかで失敗したらそこで止まり、非0で終わる。
# E2E はUnityでプロジェクトを開いていると失敗するので、先にエディタを閉じること(開いていれば最初に止まる)。
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

# E2E まで数分かけてから分かりにくく失敗するのを避け、最初に止める
. "$ROOT/tools/lib/unity-editor-lock.sh"
if [ "$RUN_E2E" = 1 ] && unity_project_open "$ROOT"; then
  fail "Unity エディタでこのプロジェクトが開いています — エディタを閉じてから実行してください(E2E を省くなら --fast)"
fi

step "1/4 C# compile (tools/compile-check)"
dotnet build "$ROOT/tools/compile-check" -nologo -v:q || fail "C# compile"
# #if UNITY_EDITOR の中(E2E シナリオ等)は上では読まれない。エディタの定義でもう一度通す
dotnet build "$ROOT/tools/compile-check" -nologo -v:q -p:CompileCheckEditor=true || fail "C# compile (UNITY_EDITOR)"

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

# Combine の import 漏れ(Xcode 26 は SwiftUI 経由で Combine を再エクスポートしない)。構文検査では見えず、
# Mac の CI(iOS build)で初めて落ちる — 2026-10-19 に Timer.publish().autoconnect() で3コミット続けて落とした
missing=$(grep -rlE '\.autoconnect\(\)|@Published|: *ObservableObject|, *ObservableObject|AnyCancellable|\.sink *\{|(Passthrough|CurrentValue)Subject' \
            --include='*.swift' "$ROOT/ios/AR_Runner_UI" | while IFS= read -r f; do
            grep -qE '^import Combine' "$f" || echo "$f"; done)
[ -z "$missing" ] || fail "Combine を使うのに 'import Combine' が無い: $missing"
echo "Combine imports OK"

# iOS アプリの純ロジック(ios/AppLogic)のテスト。リンクに macOS のツールチェーンが要るので Mac でだけ走らせる
# (Windows の Swift は Visual Studio の C ランタイムが無いとリンクできない)。CI は swift-logic-tests.yml
if [ "$(uname -s)" = "Darwin" ] && command -v swift >/dev/null 2>&1; then
  step "3b/4 Swift logic tests (swift test --package-path ios)"
  swift test --package-path "$ROOT/ios" || fail "swift logic tests"
else
  echo "Swift logic tests — skipped on this OS (run on macOS / CI: .github/workflows/swift-logic-tests.yml)"
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
