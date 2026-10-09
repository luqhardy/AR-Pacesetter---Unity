# CLAUDE.md

このリポジトリの指針は全エージェント共通で [AGENTS.md](AGENTS.md) にある(下で読み込む)。
ルールの追加・変更は AGENTS.md に対して行い、ここには Claude Code 固有のことだけを書く。

@AGENTS.md

## Claude Code 固有

- E2E(`tools/verify.sh` の4ステップ目)は数分かかるので、バックグラウンドで実行し完了通知を待つ。
