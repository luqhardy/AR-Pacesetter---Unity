#!/usr/bin/env bash
# 公開済みの Unity iOS エクスポート(GitHub Release)を ios/UnityExport へ入れる(macOS / Windows Git Bash 共通)。
# Mac で Unity を開かずに Xcode ビルドするための1コマンド。公開側は tools/publish-unity-export.sh。
#
#   tools/fetch-unity-export.sh               今のチェックアウトに含まれる最新のエクスポートを入れる
#   tools/fetch-unity-export.sh --latest      チェックアウトに関係なく一番新しいエクスポート
#   tools/fetch-unity-export.sh --tag unity-export-355bf7df   指定したもの
#   tools/fetch-unity-export.sh --force       入っているものと同じでも入れ直す
#
# 以前は rm → gh release download → tar → chmod の4手順を手で打っていた。
# 既存フォルダへの上書き(Finder の「両方とも残す」で出来る `Data 2` 等 → duplicate symbol)と、
# Windows で固めたアーカイブの実行権限の落ち(→ PhaseScriptExecution failed)はどちらもここで起きていた。
# 展開の手順は iOS ビルド CI(.github/workflows/ios-build.yml)と同じ。
#
# 前提: gh にログイン済み(`gh auth login`)。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPO="${GH_REPO:-luqhardy/AR-Pacesetter---Unity}"
EXPORT_DIR="$ROOT/ios/UnityExport"
MARKER="$EXPORT_DIR/.unity-export-tag"   # どのエクスポートが入っているか

TAG=""
LATEST=0
FORCE=0
while [ $# -gt 0 ]; do
  case "$1" in
    --tag) TAG="${2:-}"; [ -n "$TAG" ] || { echo "--tag にタグを指定してください" >&2; exit 2; }; shift ;;
    --latest) LATEST=1 ;;
    --force) FORCE=1 ;;
    -h|--help) sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done

step() { printf '\n== %s ==\n' "$1"; }
fail() { printf '\nFAILED: %s\n' "$1" >&2; exit 1; }

cd "$ROOT"
command -v gh >/dev/null || fail "gh が見つかりません(brew install gh → gh auth login)"
gh auth status >/dev/null 2>&1 || fail "gh にログインしていません(gh auth login)"

# タグ unity-export-<8桁> のコミット(手元に無ければ空)
commit_of() { git rev-parse --verify -q "${1#unity-export-}^{commit}" 2>/dev/null || true; }

step "エクスポートを選ぶ"
TAGS="$(gh release list --repo "$REPO" --limit 50 --json tagName \
        --jq '.[] | select(.tagName | startswith("unity-export-")) | .tagName')"
[ -n "$TAGS" ] || fail "unity-export-* の Release がありません(Windows で tools/publish-unity-export.sh を実行して公開する)"
NEWEST="$(printf '%s\n' "$TAGS" | head -1)"

if [ -z "$TAG" ] && [ "$LATEST" = 1 ]; then
  TAG="$NEWEST"
elif [ -z "$TAG" ]; then
  # 新しい順に見て、今のチェックアウトに含まれるコミットから作られた最初のもの
  while IFS= read -r t; do
    c="$(commit_of "$t")"
    if [ -n "$c" ] && git merge-base --is-ancestor "$c" HEAD 2>/dev/null; then TAG="$t"; break; fi
  done <<< "$TAGS"
  if [ -z "$TAG" ]; then
    echo "今のチェックアウトに含まれるエクスポートが見つかりません — 一番新しい $NEWEST を使います"
    TAG="$NEWEST"
  fi
fi
gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1 || fail "$TAG という Release はありません"
echo "使うエクスポート: $TAG"

# 鮮度: エクスポート後に Unity 側(Assets / ProjectSettings / Packages)が変わっていれば、その変更はアプリに入らない
EXPORT_COMMIT="$(commit_of "$TAG")"
if [ -n "$EXPORT_COMMIT" ]; then
  CHANGED="$(git diff --name-only "$EXPORT_COMMIT" HEAD -- Assets ProjectSettings Packages | wc -l | tr -d ' ')"
  if [ "$CHANGED" != "0" ]; then
    echo "注意: このエクスポートの後に Unity 側で ${CHANGED} ファイルが変わっています — その変更は実機に入りません。"
    echo "      Windows で tools/publish-unity-export.sh を実行して新しいエクスポートを公開してください:"
    git diff --name-only "$EXPORT_COMMIT" HEAD -- Assets ProjectSettings Packages | head -10 | sed 's/^/        /'
  else
    echo "Unity 側は今のチェックアウトと同じです"
  fi
else
  echo "注意: $TAG のコミットが手元にありません(git pull / git fetch で取得すると鮮度を確かめられます)"
fi
if [ "$TAG" != "$NEWEST" ] && [ "$LATEST" = 0 ]; then
  echo "(もっと新しい $NEWEST があります — git pull してから実行すると使えます)"
fi

# Finder のコピーで出来た重複(`ARRunner 2.xcworkspace` 等)は duplicate symbol や別プロジェクトを開く原因になる
DUPES="$(find ios -maxdepth 1 -name '* [0-9]*' 2>/dev/null || true)"
if [ -n "$DUPES" ]; then
  echo
  echo "注意: ios/ に Finder の重複コピーらしきものがあります(消すかどうかは確認してから):"
  printf '%s\n' "$DUPES" | sed 's/^/        /'
fi

fix_permissions() {
  # Windows で固めたアーカイブでは拡張子の無い Mach-O の実行ビットが落ちる(CI と同じ修復)
  chmod +x "$EXPORT_DIR/usymtool" "$EXPORT_DIR/usymtoolarm64" "$EXPORT_DIR"/*.sh 2>/dev/null || true
}

if [ "$FORCE" = 0 ] && [ -f "$MARKER" ] && [ "$(cat "$MARKER")" = "$TAG" ] \
   && [ -f "$EXPORT_DIR/Unity-iPhone.xcodeproj/project.pbxproj" ]; then
  fix_permissions
  echo
  echo "$TAG は入っています(入れ直すなら --force)"
  exit 0
fi

step "ダウンロード: $TAG(約300MB)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
gh release download "$TAG" --repo "$REPO" --pattern UnityExport.tar.gz --dir "$TMP"
[ -s "$TMP/UnityExport.tar.gz" ] || fail "ダウンロードに失敗しました"

step "ios/UnityExport へ展開"
# 上書き・マージはしない — 古いファイルが残ると duplicate symbol になる
rm -rf "$EXPORT_DIR"
tar xzf "$TMP/UnityExport.tar.gz" -C "$ROOT/ios"
[ -f "$EXPORT_DIR/Unity-iPhone.xcodeproj/project.pbxproj" ] || fail "展開に失敗しました(Unity-iPhone.xcodeproj が見つかりません)"
fix_permissions
printf '%s\n' "$TAG" > "$MARKER"

echo
echo "完了: $TAG を ios/UnityExport に入れました。"
echo "Xcode で ios/ARRunner.xcworkspace を開き、Product → Clean Build Folder → Run。"
