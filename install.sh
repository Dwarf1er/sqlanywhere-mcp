#!/bin/bash
set -e

TOOL_NAME="sqlanywhere-mcp"
CONFIGURATION="Release"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/src/SqlAnywhereMcp"
PUBLISH_DIR="$HOME/.local/bin"

usage() {
    echo "Usage: install.sh [-c <Debug|Release>] [-h]"
    exit 0
}

while getopts "c:h" opt; do
    case $opt in
        c) CONFIGURATION="$OPTARG" ;;
        h) usage ;;
        *) usage ;;
    esac
done

case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) RID="linux-x64" ;;
    Linux-aarch64 | Linux-arm64) RID="linux-arm64" ;;
    Darwin-x86_64) RID="osx-x64" ;;
    Darwin-arm64) RID="osx-arm64" ;;
    *)
        echo "Unsupported platform: $(uname -s) $(uname -m)" >&2
        exit 1
        ;;
esac

echo "Publishing $TOOL_NAME ($CONFIGURATION, $RID)..."
dotnet publish "$PROJECT_DIR" \
    -r "$RID" \
    -c "$CONFIGURATION" \
    --self-contained true \
    /p:PublishSingleFile=true \
    /p:IncludeAllContentForSelfExtract=true \
    -o "$PUBLISH_DIR"

chmod +x "$PUBLISH_DIR/$TOOL_NAME"

echo ""
echo "Installation complete."
echo ""
echo "Next steps:"
echo "  1. Edit your config: \${XDG_CONFIG_HOME:-\$HOME/.config}/sqlanywhere-mcp/config.json"
echo "     (created automatically on first run if missing)"
echo "  2. Set \"passwordCommand\" in the config to a credential helper that prints the database"
echo "     password (see README; keyturn, https://github.com/Dwarf1er/keyturn, is a good fit)"
echo "  3. Make sure the SQL Anywhere 17 ODBC driver and unixODBC are installed (see README)"
echo "  4. Register with Claude Code:"
echo "       claude mcp add --transport stdio --scope user sqlanywhere '$PUBLISH_DIR/$TOOL_NAME'"
