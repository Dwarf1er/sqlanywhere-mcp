param(
    [string]$Configuration = "Release",
    [switch]$Help
)

$ToolName    = "sqlanywhere-mcp"
$ProjectDir  = Join-Path $PSScriptRoot "src\SqlAnywhereMcp"
$PublishDir  = Join-Path $env:LOCALAPPDATA $ToolName
$ExePath     = Join-Path $PublishDir "$ToolName.exe"

if ($Help) {
    Write-Host "Usage: install.ps1 [-Configuration <Debug|Release>]"
    exit 0
}

Write-Host "Publishing $ToolName ($Configuration)..."
dotnet publish "$ProjectDir" `
    -r win-x64 `
    -c $Configuration `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeAllContentForSelfExtract=true `
    -o "$PublishDir"

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed."
    exit 1
}

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -notlike "*$PublishDir*") {
    [Environment]::SetEnvironmentVariable("Path", "$userPath;$PublishDir", "User")
    Write-Host "Added $PublishDir to user PATH."
}

# Compute the WSL2-accessible path (e.g. C:\Users\... -> /mnt/c/Users/...)
$driveLetter = $ExePath[0].ToString().ToLower()
$pathRest    = $ExePath.Substring(2).Replace('\', '/')
$WslPath     = "/mnt/$driveLetter$pathRest"

Write-Host ""
Write-Host "Installation complete."
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Edit your config:  $env:APPDATA\sqlanywhere-mcp\config.json"
Write-Host "     (created automatically on first run if missing)"
Write-Host "  2. Set `"passwordCommand`" in the config to a credential helper that prints the database"
Write-Host "     password (see README; keyturn, https://github.com/Dwarf1er/keyturn, is a good fit)"
Write-Host "  3. Register with Claude Code:"
Write-Host ""
Write-Host "     Claude Code on WSL2 (run from WSL2 terminal):"
Write-Host "       claude mcp add --transport stdio --scope user sqlanywhere '$WslPath'"
Write-Host ""
Write-Host "     Claude Code on Windows (run from any terminal):"
Write-Host "       claude mcp add --transport stdio --scope user sqlanywhere '$ExePath'"
