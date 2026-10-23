<#
Put the OpenNest help site into a package's Help folder.

By default this downloads the opennest-help release pinned in scripts/help.json and
refuses it unless its SHA-256 matches. Pass -HelpDirectory to use a locally built
site/ folder instead (e.g. while writing new help pages). Returns a short
description of the help version for build-info.json.

Example:
  ./scripts/Install-Help.ps1 -Destination artifacts/OpenNest/Help
  ./scripts/Install-Help.ps1 -Destination bin/publish/Help -HelpDirectory ../opennest-help/site
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Destination,
    [string] $HelpDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (Test-Path $Destination) { throw "Refusing to overwrite existing help folder: $Destination" }

if ($HelpDirectory) {
    if (-not (Test-Path (Join-Path $HelpDirectory 'index.html'))) { throw "No help site in $HelpDirectory (index.html missing)." }
    New-Item -ItemType Directory -Path $Destination | Out-Null
    Copy-Item (Join-Path $HelpDirectory '*') $Destination -Recurse
    return "local:$([IO.Path]::GetFullPath($HelpDirectory))"
}

$pin = Get-Content (Join-Path $PSScriptRoot 'help.json') -Raw | ConvertFrom-Json
$version = "$($pin.repository)@$($pin.tag)"
$zip = Join-Path ([IO.Path]::GetTempPath()) "opennest-help-$([guid]::NewGuid().ToString('N')).zip"
try {
    Invoke-WebRequest "https://github.com/$($pin.repository)/releases/download/$($pin.tag)/opennest-help.zip" -OutFile $zip -UseBasicParsing
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $pin.sha256) { throw "Help $version hash $hash does not match scripts/help.json." }
    Expand-Archive -Path $zip -DestinationPath $Destination
}
finally { Remove-Item $zip -ErrorAction SilentlyContinue }
return $version
