#!/bin/sh
# Unityエクスポート後の再リンク — Data フォルダの Target Membership を UnityFramework へ移す
#
# なぜ必要か:
#   `ios/UnityExport/` はエクスポートのたびに丸ごと作り直される生成物であり、
#   Xcode上で手作業したことは全て消える。中でも `Data` フォルダの
#   Target Membership は既定で **Unity-iPhone**(Unity単体アプリのターゲット)に
#   付いており、UaaL では **UnityFramework** に付け替えないと
#   ホストアプリのリンクが undefined symbols で落ちる。
#
#   毎回Xcodeでクリックし直す運用は忘れやすく、失敗の症状(リンクエラー)からは
#   原因が分かりにくいため、ここで機械的に適用する。
#
# 使い方:  ./tools/relink-unity-export.sh [project.pbxproj のパス]
# 冪等: 何度実行しても結果は同じ。
#
# UUIDはハードコードせず名前から解決する(Unityがテンプレートを変えても追従するため)。

set -eu

PROJ="${1:-ios/UnityExport/Unity-iPhone.xcodeproj/project.pbxproj}"

if [ ! -f "$PROJ" ]; then
  echo "ERROR: $PROJ が見つかりません。先にUnityのエクスポートを実行してください" >&2
  exit 1
fi

# `Data in Resources` の PBXBuildFile UUID
DATA_BF=$(awk '/\/\* Data in Resources \*\/ = \{isa = PBXBuildFile/ { print $1; exit }' "$PROJ")

# 指定ターゲットの Resources ビルドフェーズ UUID を返す
phase_of() {
  # 同名のPBXGroupが同じコメントを持つため、isa = PBXNativeTarget を確認するまで確定しない
  awk -v want="$1" '
    /^		\};$/ { intarget = 0; native = 0; inphases = 0 }
    /^		[^	]/ && index($0, "/* " want " */") > 0 && index($0, "= {") > 0 { intarget = 1; native = 0 }
    intarget && index($0, "isa = PBXNativeTarget") > 0 { native = 1 }
    intarget && native && index($0, "buildPhases = (") > 0 { inphases = 1; next }
    inphases && index($0, ");") > 0 { inphases = 0 }
    inphases && index($0, "/* Resources */") > 0 { print $1; exit }
  ' "$PROJ"
}

APP_RES=$(phase_of "Unity-iPhone")
UF_RES=$(phase_of "UnityFramework")

if [ -z "$DATA_BF" ] || [ -z "$APP_RES" ] || [ -z "$UF_RES" ]; then
  echo "ERROR: pbxproj の構造を解決できません (Data=$DATA_BF app=$APP_RES fw=$UF_RES)" >&2
  echo "       Unityのプロジェクトテンプレートが変わった可能性があります。Xcodeで手動設定してください" >&2
  exit 1
fi

TMP="$PROJ.relink.tmp"
awk -v bf="$DATA_BF" -v from="$APP_RES" -v to="$UF_RES" '
  /^\t\t\};$/                                  { cur = "" }
  $1 == from && index($0, "= {") > 0           { cur = "from" }
  $1 == to   && index($0, "= {") > 0           { cur = "to" }
  cur != "" && $1 == bf                        { next }   # 両フェーズから一旦外す(冪等化)
  cur == "to" && index($0, "files = (") > 0    { print; print "\t\t\t\t" bf " /* Data in Resources */,"; next }
                                               { print }
' "$PROJ" > "$TMP"

mv "$TMP" "$PROJ"

# 適用結果を確かめる — 黙って失敗させない
VERIFY=$(awk -v bf="$DATA_BF" -v to="$UF_RES" '
  /^\t\t\};$/                        { cur = "" }
  $1 == to && index($0, "= {") > 0   { cur = "to" }
  cur == "to" && $1 == bf            { found = 1 }
  END                                { print found + 0 }
' "$PROJ")

if [ "$VERIFY" != "1" ]; then
  echo "ERROR: 再リンクに失敗しました" >&2
  exit 1
fi

echo "OK: Data フォルダを UnityFramework ($UF_RES) の Resources へ移しました"
echo "    UnityFramework の Embed & Sign は AR_Runner_UI.xcodeproj に commit 済みのため不要です"
