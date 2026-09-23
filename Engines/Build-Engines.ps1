<#
.SYNOPSIS
    Builds every plugin engine under Engines/ and deploys it to the benchmark.

.DESCRIPTION
    OpenNest.Benchmark loads plugin engines from an Engines/ folder next to its own
    build output. This builds the benchmark plus each Engines/OpenNest.Engine.*/ project
    (test subprojects are skipped) and copies each engine DLL into that folder.

.EXAMPLE
    ./Engines/Build-Engines.ps1
    ./Engines/Build-Engines.ps1 -Engines Opus55,Terra -Configuration Debug
#>
param(
    [string]$Configuration = 'Release',
    # Engine names without the OpenNest.Engine. prefix; default is all of them.
    [string[]]$Engines
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

dotnet build (Join-Path $repoRoot 'OpenNest.Benchmark/OpenNest.Benchmark.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'OpenNest.Benchmark build failed.' }

$deployDir = Join-Path $repoRoot "OpenNest.Benchmark/bin/$Configuration/net8.0/Engines"
New-Item -ItemType Directory -Force $deployDir | Out-Null

$projects = Get-ChildItem $PSScriptRoot -Directory -Filter 'OpenNest.Engine.*' |
    Where-Object { -not $Engines -or $Engines -contains $_.Name.Substring('OpenNest.Engine.'.Length) }

foreach ($dir in $projects) {
    $csproj = Join-Path $dir.FullName "$($dir.Name).csproj"
    dotnet build $csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "$($dir.Name) build failed." }

    $dll = Join-Path $dir.FullName "bin/$Configuration/net8.0/$($dir.Name).dll"
    Copy-Item $dll $deployDir -Force
    Write-Host "Deployed $($dir.Name) -> $deployDir"
}
