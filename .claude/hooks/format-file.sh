#!/usr/bin/env bash
# PostToolUse hook for Edit|Write|MultiEdit.
# Claude Code passes the tool call as JSON on stdin; we extract the file path,
# then prettier/eslint --fix the file in place when relevant.
#
# Behaviour:
#   - No-op if jq missing, file_path missing, or local node_modules/.bin/<tool> missing
#     (so the hook stays safe before `npm install`).
#   - On non-zero exit (e.g. eslint can't auto-fix something), the stderr is shown
#     back to Claude as feedback — no need to suppress it.

set -u

file=$(jq -r '.tool_input.file_path // empty' 2>/dev/null || true)
[ -z "$file" ] && exit 0
[ -f "$file" ] || exit 0

case "$file" in
  *.ts|*.tsx|*.js|*.jsx)
    [ -x ./node_modules/.bin/prettier ] && ./node_modules/.bin/prettier --write "$file"
    [ -x ./node_modules/.bin/eslint ]   && ./node_modules/.bin/eslint   --fix   "$file"
    ;;
  *.json|*.md|*.yaml|*.yml)
    [ -x ./node_modules/.bin/prettier ] && ./node_modules/.bin/prettier --write "$file"
    ;;
esac
