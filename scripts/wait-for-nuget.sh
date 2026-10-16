#!/usr/bin/env bash
# Waits until nuget.org lists a package version. A pushed package is visible on its page at once, but the package
# index that the MCP registry reads takes a few minutes to catch up.
# Usage: scripts/wait-for-nuget.sh <package-id> <version> [attempts] [seconds-between-attempts]
set -euo pipefail

id=$(echo "${1:?package id}" | tr '[:upper:]' '[:lower:]')
version=${2:?version}
attempts=${3:-60}
delay=${4:-30}
url="https://api.nuget.org/v3-flatcontainer/$id/index.json"

for ((i = 1; i <= attempts; i++)); do
  if curl -fsS "$url" 2>/dev/null | grep -q "\"$version\""; then
    echo "$1 $version is on nuget.org"
    exit 0
  fi
  echo "attempt $i/$attempts: $1 $version is not listed yet"
  sleep "$delay"
done

echo "::error::$1 $version did not show up on nuget.org in time" >&2
exit 1
