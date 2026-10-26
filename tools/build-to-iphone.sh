#!/usr/bin/env bash
# Mac に挿した iPhone へ、最新のアプリを1コマンドで入れる(チーム全員用・無料の Apple ID でよい)。
#
#   tools/build-to-iphone.sh                 pull → Unity エクスポート取得 → ビルド → インストール → 起動
#   tools/build-to-iphone.sh --no-pull       今のチェックアウトのまま
#   tools/build-to-iphone.sh --release       Release 構成(実地テストの計測向け。既定は CI と同じ Debug)
#   tools/build-to-iphone.sh --team ABCDE12345 --bundle-id com.taro.pacesetter --device "Taro の iPhone"
#
# 初回だけ: Xcode 26 → Settings → Accounts に自分の Apple ID を追加し、gh にログイン(gh auth login)。
# チームID・Bundle ID・端末は ios/.build-to-iphone.local に覚える(git 管理外)。2回目からは引数なし。
# 無料の Apple ID で入れたアプリは7日で起動しなくなる — そのときはもう一度実行する。
#
# 無料署名の改変(tools/prepare-free-signing.sh: HealthKit を外す・チームを空にする・Bundle ID)は
# ビルドの間だけ当て、終わったら(失敗・Ctrl+C でも)元に戻す — コミットに混ざる事故が起きない。
# 手順を手でやる場合と CI の配布は Docs/BUILD_ON_BORROWED_MAC.md / Docs/TESTFLIGHT.md。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIG_FILE="$ROOT/ios/.build-to-iphone.local"
PBXPROJ="ios/AR_Runner_UI/AR_Runner_UI.xcodeproj/project.pbxproj"
ENTITLEMENTS="ios/AR_Runner_UI/AR_Runner_UI/AR_Runner_UI.entitlements"
DERIVED="$ROOT/ios/build/DerivedData"

TEAM_ID=""
BUNDLE_ID=""
DEVICE=""
PULL=1
CONFIGURATION="Debug"

# 前回の値(引数が優先)
if [ -f "$CONFIG_FILE" ]; then
  # shellcheck disable=SC1090
  . "$CONFIG_FILE"
fi

while [ $# -gt 0 ]; do
  case "$1" in
    --team) TEAM_ID="${2:-}"; shift ;;
    --bundle-id) BUNDLE_ID="${2:-}"; shift ;;
    --device) DEVICE="${2:-}"; shift ;;
    --no-pull) PULL=0 ;;
    --release) CONFIGURATION="Release" ;;
    -h|--help) sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done

step() { printf '\n== %s ==\n' "$1"; }
fail() { printf '\nFAILED: %s\n' "$1" >&2; exit 1; }

cd "$ROOT"
[ "$(uname)" = "Darwin" ] || fail "macOS で実行してください(iPhone へのビルドには Xcode が要る)"

step "1/6 道具の確認"
command -v xcodebuild >/dev/null || fail "Xcode が見つかりません(App Store から Xcode 26 を入れる)"
XCODE_MAJOR="$(xcodebuild -version | awk 'NR==1 {split($2, v, "."); print v[1]}')"
[ "${XCODE_MAJOR:-0}" -ge 26 ] || fail "Xcode 26 以上が要ります(今: $(xcodebuild -version | head -1))。Deployment Target が iOS 26.0"
command -v gh >/dev/null || fail "gh が見つかりません(brew install gh → gh auth login)"
xcodebuild -version | head -1

# ── チームID ────────────────────────────────────────────────────────────────
# Apple Development 証明書の OU がチームID。無ければ Xcode のアカウント設定から
find_team_ids() {
  local tmp; tmp="$(mktemp -d)"
  security find-certificate -a -c "Apple Development" -p 2>/dev/null \
    | awk -v dir="$tmp" '/BEGIN CERTIFICATE/ {n++} n {print > (dir "/c" n ".pem")}'
  for pem in "$tmp"/c*.pem; do
    [ -f "$pem" ] || continue
    openssl x509 -in "$pem" -noout -checkend 0 >/dev/null 2>&1 || continue   # 期限切れは除く
    openssl x509 -in "$pem" -noout -subject 2>/dev/null | grep -oE 'OU ?= ?[A-Z0-9]{10}' | grep -oE '[A-Z0-9]{10}$' || true
  done
  rm -rf "$tmp"
  defaults read com.apple.dt.Xcode IDEProvisioningTeamByIdentifier 2>/dev/null | grep -oE 'teamID = "?[A-Z0-9]{10}' | grep -oE '[A-Z0-9]{10}$' || true
  defaults read com.apple.dt.Xcode IDEProvisioningTeams 2>/dev/null | grep -oE 'teamID = "?[A-Z0-9]{10}' | grep -oE '[A-Z0-9]{10}$' || true
}

if [ -z "$TEAM_ID" ]; then
  TEAMS="$(find_team_ids | sort -u)"
  COUNT="$(printf '%s' "$TEAMS" | grep -c . || true)"
  if [ "$COUNT" = "1" ]; then
    TEAM_ID="$TEAMS"
  elif [ "$COUNT" = "0" ]; then
    fail "Apple ID のチームが見つかりません。Xcode → Settings → Accounts で自分の Apple ID を追加し、
        Manage Certificates… → + → Apple Development を作ってから、もう一度実行してください
        (チームIDが分かっていれば --team <10文字> でも可)"
  else
    echo "チームが複数あります。--team で選んでください:"
    printf '%s\n' "$TEAMS" | sed 's/^/  /'
    exit 2
  fi
fi
[[ "$TEAM_ID" =~ ^[A-Z0-9]{10}$ ]] || fail "チームIDの形式が違います(英大文字・数字10文字): $TEAM_ID"
echo "チーム: $TEAM_ID"

# Bundle ID は全世界で一意。チームIDから作れば他の人と被らず、毎回同じになる
# (変わると iPhone 上は別アプリになり、溜まった RunLogs/*.csv へ届かなくなる)
if [ -z "$BUNDLE_ID" ]; then
  BUNDLE_ID="jp.arvision.dev.$(printf '%s' "$TEAM_ID" | tr 'A-Z' 'a-z')"
fi
echo "Bundle ID: $BUNDLE_ID"

# ── iPhone ──────────────────────────────────────────────────────────────────
step "2/6 iPhone を探す"
DEVICES_JSON="$(mktemp)"
trap 'rm -f "$DEVICES_JSON"' EXIT
xcrun devicectl list devices --json-output "$DEVICES_JSON" >/dev/null 2>&1 \
  || fail "devicectl で端末一覧を取れません(Xcode を一度起動して追加コンポーネントを入れる)"

field() { plutil -extract "result.devices.$1.$2" raw -o - "$DEVICES_JSON" 2>/dev/null || true; }
N="$(plutil -extract result.devices raw -o - "$DEVICES_JSON" 2>/dev/null || echo 0)"
CANDIDATES=()   # "udid<TAB>名前<TAB>接続"
for ((i = 0; i < N; i++)); do
  [ "$(field "$i" hardwareProperties.platform)" = "iOS" ] || continue
  [ "$(field "$i" hardwareProperties.reality)" != "virtual" ] || continue
  TUNNEL="$(field "$i" connectionProperties.tunnelState)"
  [ "$TUNNEL" != "unavailable" ] || continue   # 以前つないだが今は居ない端末
  CANDIDATES+=("$(field "$i" hardwareProperties.udid)	$(field "$i" deviceProperties.name)	$(field "$i" connectionProperties.transportType)")
done

pick=()
for c in ${CANDIDATES[@]+"${CANDIDATES[@]}"}; do
  IFS=$'\t' read -r udid name transport <<< "$c"
  if [ -n "$DEVICE" ]; then
    { [ "$DEVICE" = "$udid" ] || [ "$DEVICE" = "$name" ]; } && pick+=("$c")
  else
    pick+=("$c")
  fi
done
# 指定が無く複数あれば、USB でつながっている方
if [ -z "$DEVICE" ] && [ "${#pick[@]}" -gt 1 ]; then
  wired=()
  for c in "${pick[@]}"; do [ "$(printf '%s' "$c" | cut -f3)" = "wired" ] && wired+=("$c"); done
  [ "${#wired[@]}" -eq 1 ] && pick=("${wired[@]}")
fi
if [ "${#pick[@]}" -ne 1 ]; then
  if [ "${#CANDIDATES[@]}" -eq 0 ]; then
    fail "iPhone が見つかりません。USB で挿し、iPhone で「このコンピュータを信頼」→ 設定 → プライバシーとセキュリティ → デベロッパモードを ON"
  fi
  echo "iPhone を --device で選んでください(名前か UDID):"
  for c in "${CANDIDATES[@]}"; do printf '  %s\n' "$(printf '%s' "$c" | tr '\t' ' ')"; done
  exit 2
fi
IFS=$'\t' read -r DEVICE_UDID DEVICE_NAME _ <<< "${pick[0]}"
echo "iPhone: $DEVICE_NAME ($DEVICE_UDID)"

# 次回のために覚える(コミットされない)
cat > "$CONFIG_FILE" <<EOF
# tools/build-to-iphone.sh が書く。git 管理外。消せば次回また自動で探す
TEAM_ID=$TEAM_ID
BUNDLE_ID=$BUNDLE_ID
DEVICE="$DEVICE_UDID"
EOF

# ── ソースと Unity エクスポート ───────────────────────────────────────────────
step "3/6 最新のソースと Unity エクスポート"
if [ "$PULL" = 1 ]; then
  git pull --ff-only || fail "git pull できません(手元の変更やブランチを確認する。今のまま作るなら --no-pull)"
fi
git log --oneline -1
bash "$ROOT/tools/fetch-unity-export.sh"

# ── 無料署名(ビルドの間だけ)──────────────────────────────────────────────────
step "4/6 署名の準備(ビルドが終われば元に戻す)"
RESTORE=0
restore_signing() {
  if [ "$RESTORE" = 1 ]; then
    git checkout -- "$PBXPROJ" "$ENTITLEMENTS"
    echo "署名の一時改変を戻しました"
  fi
  rm -f "$DEVICES_JSON"
}
trap restore_signing EXIT
trap 'exit 130' INT TERM

if git diff --quiet HEAD -- "$PBXPROJ" "$ENTITLEMENTS"; then
  bash "$ROOT/tools/prepare-free-signing.sh" "$BUNDLE_ID" >/dev/null
  RESTORE=1
elif grep -q 'DEVELOPMENT_TEAM = "";' "$PBXPROJ"; then
  # 以前に手で prepare-free-signing.sh を実行したまま — その Bundle ID を使い、戻さない
  BUNDLE_ID="$(grep -oE 'PRODUCT_BUNDLE_IDENTIFIER = [^;]+;' "$PBXPROJ" | head -1 | sed -E 's/.*= (.*);/\1/')"
  echo "手で当てた無料署名の改変をそのまま使います(Bundle ID: $BUNDLE_ID)"
else
  fail "$PBXPROJ か entitlements に未コミットの変更があります。片付けてから実行してください"
fi

# ── ビルド ──────────────────────────────────────────────────────────────────
step "5/6 ビルド($CONFIGURATION・初回は10分ほど)"
LOG="$ROOT/ios/build/xcodebuild.log"
mkdir -p "$(dirname "$LOG")"
if ! xcodebuild build \
      -workspace ios/ARRunner.xcworkspace \
      -scheme AR_Runner_UI \
      -configuration "$CONFIGURATION" \
      -destination "id=$DEVICE_UDID" \
      -derivedDataPath "$DERIVED" \
      -allowProvisioningUpdates -allowProvisioningDeviceRegistration \
      DEVELOPMENT_TEAM="$TEAM_ID" \
      > "$LOG" 2>&1; then
  echo "── 失敗箇所($LOG)──"
  grep -E "error:|duplicate symbol|Undefined symbol|Signing|provisioning" "$LOG" | head -30
  fail "ビルドに失敗しました。よくある原因は Docs/BUILD_ON_BORROWED_MAC.md §6"
fi
APP="$DERIVED/Build/Products/$CONFIGURATION-iphoneos/AR_Runner_UI.app"
[ -d "$APP" ] || fail "ビルドしたアプリが見つかりません: $APP"
echo "ビルド完了"

# ── インストールと起動 ────────────────────────────────────────────────────────
step "6/6 iPhone へインストールして起動"
xcrun devicectl device install app --device "$DEVICE_UDID" "$APP" >/dev/null \
  || fail "インストールに失敗しました(iPhone のロックを解除し、デベロッパモードが ON か確かめる)"
echo "インストール完了"
if ! xcrun devicectl device process launch --device "$DEVICE_UDID" --terminate-existing "$BUNDLE_ID" >/dev/null 2>&1; then
  echo
  echo "起動できませんでした — 初回は iPhone で 設定 → 一般 → VPNとデバイス管理 → 自分の Apple ID を「信頼」してから、"
  echo "ホーム画面のアプリを開いてください(無料の Apple ID の仕様。2回目からは要りません)"
fi

echo
echo "完了: $(git log --oneline -1) を $DEVICE_NAME に入れました。"
echo "無料の Apple ID では7日で起動しなくなります — そのときはもう一度 tools/build-to-iphone.sh"
