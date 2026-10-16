# shellcheck shell=bash
# Unity エディタがプロジェクトを開いているかを調べる(tools/verify.sh・tools/publish-unity-export.sh が source する)。
#
# エディタで開いたままバッチモードの Unity を起動すると、E2E・エクスポートは数分待たされた末に
# 分かりにくいエラーで失敗する。エディタは開いている間 Temp/UnityLockfile を掴んでいる
# (Windows は排他で開く・macOS は開いたまま)ので、それを最初に確かめる。
# エディタのクラッシュ等で残っただけのロックファイルは誰も掴んでいないので「開いていない」になる。
#
#   unity_project_open <プロジェクトのルート>   開いていれば 0、そうでなければ 1
unity_project_open() {
  local lock="$1/Temp/UnityLockfile"
  [ -f "$lock" ] || return 1
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*)
      # 排他ロック中は追記のために開くことすらできない(0バイト追記なので中身は変えない)
      ! ( : >> "$lock" ) 2>/dev/null ;;
    *)
      command -v lsof >/dev/null 2>&1 && lsof "$lock" >/dev/null 2>&1 ;;
  esac
}
