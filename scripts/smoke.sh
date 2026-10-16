#!/usr/bin/env bash
# Starts the server and speaks MCP to it over stdio: initialize, then tools/list. No database is needed, so this
# runs anywhere. Checks the reported version, the tool list, and that every tool declares an output schema.
# Usage: scripts/smoke.sh <path-to-executable> <expected-version>
set -euo pipefail

exe=${1:?path to the sqlanywhere-mcp executable}
version=${2:?expected version, e.g. 0.1.0}

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cat > "$work/config.json" <<'JSON'
{
  "databases": {
    "smoke": { "host": "localhost", "database": "smoke", "username": "u", "passwordCommand": "echo unused" }
  }
}
JSON
config=$work/config.json
# Git Bash on Windows hands native programs MSYS paths, which .NET can't read.
if command -v cygpath >/dev/null 2>&1; then config=$(cygpath -w "$config"); fi

{
  printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}'
  printf '%s\n' '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  printf '%s\n' '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
  printf '%s\n' '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_databases","arguments":{}}}'
  # The server exits when its input ends, so hold it open long enough to answer.
  sleep 5
} | SQLANYWHERE_MCP_CONFIG=$config "$exe" > "$work/out.txt" 2> "$work/err.txt" || true

fail() { echo "smoke test failed: $1" >&2; echo "--- stdout" >&2; cat "$work/out.txt" >&2; echo "--- stderr" >&2; cat "$work/err.txt" >&2; exit 1; }

grep -q "\"serverInfo\":{\"name\":\"sqlanywhere\",\"version\":\"$version\"}" "$work/out.txt" \
  || fail "serverInfo is not sqlanywhere $version"
tools=$(grep -o '"name":"[a-z_]*","description"' "$work/out.txt" | wc -l | tr -d ' ')
[ "$tools" -ge 30 ] || fail "expected at least 30 tools, found $tools"
schemas=$(grep -o '"outputSchema"' "$work/out.txt" | wc -l | tr -d ' ')
[ "$schemas" -ge "$tools" ] || fail "only $schemas of $tools tools have an output schema"
grep -q '"readOnlyHint":true' "$work/out.txt" || fail "no tool is annotated read-only"
grep -q '"structuredContent":{"configPath"' "$work/out.txt" || fail "list_databases returned no structured content"

echo "smoke test passed: $exe ($version, $tools tools)"
