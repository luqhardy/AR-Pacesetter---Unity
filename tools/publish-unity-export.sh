#!/usr/bin/env bash
# Unity の iOS エクスポートを作り、GitHub Release として公開する(Windows Git Bash / macOS 共通)。
# 公開したエクスポートは iOS ビルド CI(ios-build.yml)が既定で使い、Mac は USB 無しで取得できる:
#
#   tools/fetch-unity-export.sh
#
#   tools/publish-unity-export.sh             エクスポート → 梱包 → 公開
#   tools/publish-unity-export.sh --dry-run   エクスポート → 梱包まで(公開しない)
#   tools/publish-unity-export.sh --skip-export  既存の ios/UnityExport を梱包・公開(HEAD から作ったものに限る)
#
# 前提: 作業ツリーがクリーン・HEAD が origin へ push 済み・gh にログイン済み・
#       Unity エディタでプロジェクトを開いていない(バッチモードのエクスポートが失敗する)。
# 命名は CI の取り決めに合わせる: タグ unity-export-<HEADの8桁>・プレリリース・アセット UnityExport.tar.gz
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_VERSION="6000.3.17f1"
REPO="${GH_REPO:-luqhardy/AR-Pacesetter---Unity}"
DRY_RUN=0
SKIP_EXPORT=0
for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=1 ;;
    --skip-export) SKIP_EXPORT=1 ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) UNITY="${UNITY_PATH:-/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe}" ;;
  Darwin)               UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity}" ;;
  *)                    UNITY="${UNITY_PATH:-$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity}" ;;
esac

step() { printf '\n== %s ==\n' "$1"; }
fail() { printf '\nFAILED: %s\n' "$1" >&2; exit 1; }
# 内容の変更だけを見る。Unity は改行コードだけ違うファイル(.prefab 等)を書き戻すことがあり、
# git status はそれを「変更あり」と出すが、.gitattributes で正規化すると差分は無い
clean_tree() {
  git update-index -q --refresh >/dev/null 2>&1 || true
  git diff --quiet HEAD -- && [ -z "$(git ls-files --others --exclude-standard)" ]
}

cd "$ROOT"

step "前提の確認"
. "$ROOT/tools/lib/unity-editor-lock.sh"
if [ "$SKIP_EXPORT" = 0 ] && unity_project_open "$ROOT"; then
  fail "Unity エディタでこのプロジェクトが開いています — エディタを閉じてから実行してください"
fi
clean_tree || { git status --short; fail "作業ツリーに未コミットの変更があります(エクスポートとコミットを一致させるため)"; }
SHA="$(git rev-parse HEAD)"
SHA8="$(git rev-parse --short=8 HEAD)"
TAG="unity-export-$SHA8"
if [ "$DRY_RUN" = 0 ]; then
  command -v gh >/dev/null || fail "gh が見つかりません"
  git fetch -q origin
  git branch -r --contains "$SHA" | grep -q . || fail "HEAD ($SHA8) が origin に push されていません"
  if gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then fail "$TAG は公開済みです"; fi
fi
echo "HEAD $SHA8 → $TAG"

if [ "$SKIP_EXPORT" = 0 ]; then
  step "Unity エクスポート(-buildTarget iOS・数分)"
  [ -f "$UNITY" ] || fail "Unity $UNITY_VERSION が見つかりません: $UNITY (UNITY_PATH=... で指定)"
  "$UNITY" -batchmode -quit -buildTarget iOS -projectPath "$ROOT" \
    -executeMethod IOSBuildExporter.ExportIOS -logFile "$ROOT/export.log" \
    || fail "エクスポート(ログ: export.log の [iOS EXPORT] 行)"
  # エクスポータは preloadedAssets を自分で戻すが、他の設定が変わっていれば公開しない
  clean_tree || { git status --short; fail "エクスポートがコミット済みのファイルを変更しました"; }
fi
[ -f ios/UnityExport/Unity-iPhone.xcodeproj/project.pbxproj ] || fail "ios/UnityExport がありません"

step "梱包"
OUT_DIR="$ROOT/Builds/iOS"
mkdir -p "$OUT_DIR"
ARCHIVE="$OUT_DIR/UnityExport.tar.gz"
rm -f "$ARCHIVE"
# Burst のデバッグ情報は Xcode プロジェクトから参照されない(DoNotShip)
( cd ios && tar --exclude='UnityExport/*_BurstDebugInformation_DoNotShip' -czf "$ARCHIVE" UnityExport )
echo "$ARCHIVE ($(du -h "$ARCHIVE" | cut -f1))"

if [ "$DRY_RUN" = 1 ]; then
  echo; echo "--dry-run: 公開はしていません"
  exit 0
fi

step "リリースノート"
PREV_TAG="$(gh release list --repo "$REPO" --limit 30 --json tagName \
  --jq '[.[] | select(.tagName | startswith("unity-export-"))][0].tagName' || true)"
NOTES="$(mktemp)"
{
  echo "master \`$SHA8\` からの iOS エクスポート。iOS ビルド CI(\`ios-build.yml\`)が既定で使う最新エクスポート。"
  echo
  if [ -n "$PREV_TAG" ]; then
    PREV_SHA8="${PREV_TAG#unity-export-}"
    echo "前回公開 \`$PREV_TAG\` 以降の Unity 側の変更:"
    git log --no-merges --format='- %s (%h)' "$PREV_SHA8..$SHA" -- Assets Packages ProjectSettings 2>/dev/null \
      | head -40 || true
    echo
  fi
  echo "エクスポート条件: Unity $UNITY_VERSION・\`-buildTarget iOS\`(\`IOSBuildExporter.ExportIOS\`)。"
  echo "\`Data\` フォルダはエクスポート後処理が UnityFramework へ付け替え済み — 展開後の Xcode 側の手作業は不要。"
  echo
  echo "Mac での使い方(リポジトリのフォルダで。展開と実行権限の修復まで行う):"
  echo
  echo "    git pull && tools/fetch-unity-export.sh"
  echo
  echo "手で入れる場合は、既存の ios/UnityExport を消してから展開し、"
  echo "\`chmod +x ios/UnityExport/usymtool ios/UnityExport/usymtoolarm64 ios/UnityExport/*.sh\` を実行する"
  echo "(Windows で固めたアーカイブは実行権限が落ちている)。"
} > "$NOTES"
cat "$NOTES"

step "公開: $TAG"
gh release create "$TAG" "$ARCHIVE" --repo "$REPO" --prerelease --target "$SHA" \
  --title "Unity iOS export (commit $SHA8)" --notes-file "$NOTES"
rm -f "$NOTES" "$ARCHIVE"
echo; echo "公開しました: https://github.com/$REPO/releases/tag/$TAG"
