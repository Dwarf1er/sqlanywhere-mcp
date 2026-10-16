#!/usr/bin/env bash
# Fails unless the version in the project file and both versions in .mcp/server.json equal the expected one.
# Usage: scripts/check-version.sh <version>
set -euo pipefail

expected=${1:?version, e.g. 0.1.0}
root=$(cd "$(dirname "$0")/.." && pwd)

project=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/src/SqlAnywhereMcp/SqlAnywhereMcp.csproj")
[ "$project" = "$expected" ] || { echo "::error::SqlAnywhereMcp.csproj has Version '$project', expected '$expected'" >&2; exit 1; }

versions=$(sed -n 's#.*"version": "\(.*\)".*#\1#p' "$root/src/SqlAnywhereMcp/.mcp/server.json")
[ "$(echo "$versions" | wc -l | tr -d ' ')" = 2 ] || { echo "::error::expected two versions in .mcp/server.json" >&2; exit 1; }
for v in $versions; do
  [ "$v" = "$expected" ] || { echo "::error::.mcp/server.json has version '$v', expected '$expected'" >&2; exit 1; }
done
echo "versions match $expected"
