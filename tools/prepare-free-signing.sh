#!/usr/bin/env bash
#
# 無料Apple IDでビルドするための下準備 (借りたMacでの1回限りのビルド向け)
#
#   使い方:  ./tools/prepare-free-signing.sh com.<あなたのID>.pacesetter
#   元に戻す: git checkout -- ios/AR_Runner_UI
#
# ⚠ このスクリプトの変更は**絶対にコミットしないこと**。
#   ローカルの署名事情に合わせた一時的な改変であり、リポジトリの正の設定ではない。
#   実際に一度、実行結果がそのままコミットされて Bundle ID が
#   プレースホルダのまま共有される事故が起きている (2026-09-05 の合体で検出)。
#
# 何をするか:
#   1. HealthKit ケイパビリティを外す
#        無料のPersonal Teamでは HealthKit のプロビジョニングができず、
#        署名が通らない。HealthKit(心拍・ワークアウト保存)は Apple Watch 連携で、
#        第1期スコープ(F-01〜F-11)の**対象外**なので外しても検証内容に影響しない。
#        無効化されるのは HeartRateMonitor と HealthKitWorkoutSaver のみ。
#   2. DEVELOPMENT_TEAM を空にする
#        リポジトリには別アカウントのチームIDが埋まっており、他人のMacでは解決できない。
#        空にしておくと Xcode の Signing & Capabilities で自分のチームを選ぶだけで済む。
#   3. Bundle Identifier を差し替える
#        Bundle ID は全世界で一意。既定の com.pacesetterUI は他アカウントが
#        使用済みの可能性があるため、自分専用のものに変える。
#
# macOS標準の perl を使う (BSD sed と GNU sed の -i の差異を避けるため)。

set -euo pipefail

BUNDLE_ID="${1:-}"

if [[ -z "$BUNDLE_ID" ]]; then
  echo "使い方: $0 <bundle-id>" >&2
  echo "  例:   $0 com.taro-yamada.pacesetter   ← 自分専用の一意な値にすること" >&2
  exit 1
fi

# 例文をそのまま貼ったケースを弾く。過去にこれが commit まで到達している
case "$BUNDLE_ID" in
  *yourname*|*YOURNAME*|*example*|*EXAMPLE*|*'<'*|*'>'*|com.pacesetterUI)
    echo "エラー: '$BUNDLE_ID' はプレースホルダ(または既定値)です。" >&2
    echo "        Bundle ID は全世界で一意である必要があります。" >&2
    echo "        自分のIDを含む値を指定してください (例: com.taro-yamada.pacesetter)" >&2
    exit 1 ;;
esac

if [[ ! "$BUNDLE_ID" =~ ^[A-Za-z0-9.-]+$ ]]; then
  echo "エラー: Bundle ID に使えるのは英数字・ドット・ハイフンのみです: $BUNDLE_ID" >&2
  exit 1
fi

# リポジトリルートへ移動 (どこから実行しても動くように)
cd "$(dirname "$0")/.."

PBXPROJ="ios/AR_Runner_UI/AR_Runner_UI.xcodeproj/project.pbxproj"
ENTITLEMENTS="ios/AR_Runner_UI/AR_Runner_UI/AR_Runner_UI.entitlements"

for f in "$PBXPROJ" "$ENTITLEMENTS"; do
  [[ -f "$f" ]] || { echo "エラー: $f が見つかりません。リポジトリのルートで実行していますか?" >&2; exit 1; }
done

# 既に改変済みなら重ねない。元の値が失われて git checkout で戻せなくなるため
if git diff --quiet HEAD -- "$PBXPROJ" "$ENTITLEMENTS" 2>/dev/null; then
  :
else
  echo "警告: これらのファイルには既に未コミットの変更があります:" >&2
  git diff --stat HEAD -- "$PBXPROJ" "$ENTITLEMENTS" >&2
  echo "      重ねて実行すると元の値が失われます。先に 'git checkout -- ios/AR_Runner_UI' を実行してください" >&2
  exit 1
fi

echo "▶ 1/3  HealthKit ケイパビリティを除去します"
cat > "$ENTITLEMENTS" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<!-- 無料Apple IDでは HealthKit をプロビジョニングできないため空にしている。
	     復帰させる場合: git checkout -- ios/AR_Runner_UI -->
</dict>
</plist>
PLIST

echo "▶ 2/3  DEVELOPMENT_TEAM を空にします (Xcodeで自分のチームを選択できるように)"
perl -pi -e 's/DEVELOPMENT_TEAM = [A-Z0-9]+;/DEVELOPMENT_TEAM = "";/g' "$PBXPROJ"

echo "▶ 3/3  Bundle Identifier を $BUNDLE_ID に変更します"
perl -pi -e "s/PRODUCT_BUNDLE_IDENTIFIER = [^;]+;/PRODUCT_BUNDLE_IDENTIFIER = $BUNDLE_ID;/g" "$PBXPROJ"

echo
echo "── 確認 ──────────────────────────────────────────────"
echo "HealthKit の記述が残っていないか (0件が正常):"
echo "  $(grep -c 'healthkit' "$ENTITLEMENTS" || true) 件"
echo "DEVELOPMENT_TEAM:"
grep -o 'DEVELOPMENT_TEAM = [^;]*;' "$PBXPROJ" | sort -u | sed 's/^/  /'
echo "PRODUCT_BUNDLE_IDENTIFIER:"
grep -o 'PRODUCT_BUNDLE_IDENTIFIER = [^;]*;' "$PBXPROJ" | sort -u | sed 's/^/  /'
echo "─────────────────────────────────────────────────────"
echo
echo "完了。次は ios/ARRunner.xcworkspace を開き、AR_Runner_UI ターゲットの"
echo "Signing & Capabilities で自分のApple IDのチームを選んでください。"
echo
echo "╔══════════════════════════════════════════════════════════════╗"
echo "║  ⚠  この変更はコミットしないでください                        ║"
echo "║     ローカルの署名事情に合わせた一時的な改変です。            ║"
echo "║     共有すると全員のアプリIDが変わり、端末上で別アプリ扱いに  ║"
echo "║     なって過去のCSVログが読めなくなります。                   ║"
echo "║                                                              ║"
echo "║     戻す:  git checkout -- ios/AR_Runner_UI                   ║"
echo "╚══════════════════════════════════════════════════════════════╝"
