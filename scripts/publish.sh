#!/usr/bin/env bash
# Publishes the server for one runtime as a self-contained single file into a staging directory, with the
# README and LICENSE beside it. It is not trimmed: the MCP SDK and the JSON serialization use reflection.
# Usage: scripts/publish.sh <rid> <staging-dir>
# Run it on the target OS when you want to run the result (CI smoke-tests it); it also cross-builds.
set -euo pipefail

rid=${1:?runtime identifier, e.g. linux-x64}
out=${2:?staging directory}
root=$(cd "$(dirname "$0")/.." && pwd)

rm -rf "$out"
mkdir -p "$out"

dotnet publish "$root/src/SqlAnywhereMcp" -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=true --nologo -o "$out"

# Debug symbols are embedded already; a stray .pdb is not for end users.
find "$out" -name '*.pdb' -delete

cp "$root/README.md" "$root/LICENSE" "$out/"

echo "==> staged in $out:"
ls -la "$out"
