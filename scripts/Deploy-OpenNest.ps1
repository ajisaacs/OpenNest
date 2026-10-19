<#
Package a Velopack release of the desktop app and push it to a self-hosted
update feed (a static file server serving the `vpk pack` output). Installed
clients configured with that feed URL (Tools > Options > Auto-update feed URL) pull
and apply updates themselves.

This is separate from scripts/Publish-Windows.ps1 and the GitHub release
process (docs/releasing.md): it targets a privately hosted feed, not a
public GitHub Release, and never embeds that feed's host or path in source.

Bump <Version> in OpenNest/OpenNest.csproj before running this for a new
release.

Examples:
  powershell -ExecutionPolicy Bypass -File scripts/Deploy-OpenNest.ps1 `
      -FeedHost myhost -FeedUser me -RemoteDataDir ~/docker/opennest-releases/data
#>

Param(
    [string]$PublishConfiguration = "Release",
    [Parameter(Mandatory)]
    [string]$FeedHost,
    [string]$FeedUser = "aj",
    [Parameter(Mandatory)]
    [string]$RemoteDataDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $PSCommandPath
$RepoRoot = if ((Split-Path -Leaf $ScriptDir) -eq 'scripts') { Split-Path -Parent $ScriptDir } else { $ScriptDir }
Write-Host "Repository root: $RepoRoot"

$ProjectPath = Join-Path $RepoRoot 'OpenNest\OpenNest.csproj'
if (-not (Test-Path -LiteralPath $ProjectPath)) { throw "Project not found at: $ProjectPath" }

$csprojContent = Get-Content -LiteralPath $ProjectPath -Raw
if ($csprojContent -notmatch '<Version>(?<v>[\d.]+)</Version>') { throw "Could not find <Version> in $ProjectPath" }
$Version = $Matches.v
Write-Host "Packaging OpenNest v$Version"

# Ensure the vpk (Velopack) CLI is available
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "Installing vpk CLI (dotnet tool)..."
    dotnet tool install -g vpk
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool install vpk failed" }
}

$PublishDir = Join-Path $RepoRoot 'OpenNest\bin\publish-tmp'
$ReleasesDir = Join-Path $RepoRoot 'OpenNest\bin\releases'
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

Write-Host "Publishing self-contained build..."
& dotnet publish $ProjectPath -c $PublishConfiguration -r win-x64 --self-contained true -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Write-Host "Packing Velopack release v$Version..."
& vpk pack -u OpenNest -v $Version -p $PublishDir -e OpenNest.exe -o $ReleasesDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE" }

Write-Host "Pushing release feed to $FeedHost..."
& scp -r "$ReleasesDir\*" "${FeedUser}@${FeedHost}:${RemoteDataDir}/"
if ($LASTEXITCODE -ne 0) { throw "scp failed" }

Write-Host "Deployment complete. Machines already configured with this feed URL will pick up v$Version within 30 minutes."
Write-Host ""
Write-Host "First-time setup on a machine (one-time per machine):"
Write-Host "  1. Copy $ReleasesDir\OpenNest-win-Setup.exe to the machine and run it."
Write-Host "  2. In OpenNest, open Tools > Options and set 'Auto-update feed URL' to"
Write-Host "     your feed's base URL (e.g. http://${FeedHost}:<port>), then restart OpenNest."
Write-Host "     This value is per-machine and never stored in source control."
