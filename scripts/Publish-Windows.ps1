[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string] $Version,
    [string] $OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Packaging includes a desktop smoke test and requires Windows.' }

$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path $output) { throw "Refusing to overwrite existing output: $output" }
    $commit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source commit.' }
    New-Item -ItemType Directory -Path $output | Out-Null
    $package = Join-Path $output 'OpenNest'
    $common = @('-c', 'Release', '-r', 'win-x64', "-p:Version=$Version", '-p:DebugType=None', '-p:DebugSymbols=false')

    dotnet publish OpenNest/OpenNest.csproj @common --self-contained true -o $package
    if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }

    # Build hooks copy to bin/, not PublishDir. Explicitly package every shipped post.
    $posts = Join-Path $package 'Posts'
    New-Item -ItemType Directory -Path $posts -Force | Out-Null
    foreach ($name in @('Cincinnati', 'CincinnatiCIFiber', 'GravographIS')) {
        $project = "OpenNest.Posts.$name"
        $staging = Join-Path $output "post-build/$name"
        dotnet publish "Posts/$project/$project.csproj" @common --self-contained false -o $staging
        if ($LASTEXITCODE -ne 0) { throw "$project publish failed." }
        Copy-Item "$staging/$project.dll" $posts
        if (Test-Path "$staging/$project.json") { Copy-Item "$staging/$project.json" $posts }
        # Gravograph's serial-port runtime is not in the desktop project's dependency graph.
        if ($name -eq 'GravographIS') { Copy-Item "$staging/System.IO.Ports.dll" $package }
    }

    New-Item -ItemType Directory -Path (Join-Path $package 'Schemes') -Force | Out-Null
    Copy-Item LICENSE $package
    @{
        version = $Version
        sourceCommit = $commit
        runtime = 'win-x64'
        selfContained = $true
    } | ConvertTo-Json | Set-Content (Join-Path $package 'build-info.json') -Encoding utf8NoBOM

    $required = @(
        'OpenNest.exe', 'OpenNest.dll', 'OpenNest.Core.dll', 'OpenNest.Engine.dll',
        'OpenNest.runtimeconfig.json', 'coreclr.dll', 'System.Windows.Forms.dll',
        'onnxruntime.dll', 'System.IO.Ports.dll', 'Configurations/PipeFlangeShape.json',
        'Posts/OpenNest.Posts.Cincinnati.dll', 'Posts/OpenNest.Posts.Cincinnati.json',
        'Posts/OpenNest.Posts.CincinnatiCIFiber.dll', 'Posts/OpenNest.Posts.CincinnatiCIFiber.json',
        'Posts/OpenNest.Posts.GravographIS.dll', 'LICENSE', 'build-info.json'
    )
    foreach ($file in $required) {
        if (-not (Test-Path (Join-Path $package $file) -PathType Leaf)) { throw "Package missing $file" }
    }
    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $package 'OpenNest.dll')).Version
    if ($assemblyVersion.ToString() -ne "$Version.0") { throw "Wrong assembly version: $assemblyVersion" }
    $runtime = Get-Content (Join-Path $package 'OpenNest.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (-not $runtime.runtimeOptions.includedFrameworks) { throw 'Package is not self-contained.' }

    $zipName = "OpenNest.v$Version.win-x64.zip"
    $zip = Join-Path $output $zipName
    Compress-Archive -Path "$package/*" -DestinationPath $zip

    # Exercise the actual ZIP, not the build tree. This checks startup and post discovery,
    # not interactive CAD workflows or machine/serial/GPU operation.
    $expanded = Join-Path $output 'smoke-test'
    Expand-Archive -Path $zip -DestinationPath $expanded
    foreach ($file in $required) {
        if (-not (Test-Path (Join-Path $expanded $file) -PathType Leaf)) { throw "ZIP missing $file" }
    }
    # Built-in engines ship inside OpenNest.Engine.dll: check the packaged registry, not the build tree.
    $smokeProject = Join-Path $PSScriptRoot 'ReleaseSmoke/ReleaseSmoke.csproj'
    dotnet build $smokeProject -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Engine registry checker build failed.' }
    dotnet run --project $smokeProject -c Release --no-build -- $expanded
    if ($LASTEXITCODE -ne 0) { throw 'Packaged engine registry check failed.' }

    $process = Start-Process (Join-Path $expanded 'OpenNest.exe') -WorkingDirectory $expanded -PassThru
    try {
        if (-not $process.WaitForInputIdle(30000)) { throw 'Desktop did not become idle within 30 seconds.' }
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            $process.Refresh()
            if ($process.HasExited) { throw "Desktop exited during startup: $($process.ExitCode)" }
            if ($process.MainWindowHandle -ne 0 -and $process.MainWindowTitle -match '^OpenNest(?: - \[.*\])?$') { break }
            Start-Sleep -Milliseconds 250
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($process.MainWindowHandle -eq 0 -or $process.MainWindowTitle -notmatch '^OpenNest(?: - \[.*\])?$') {
            throw "Expected OpenNest main window, found '$($process.MainWindowTitle)'."
        }
        Write-Host 'PASS: packaged OpenNest main window opened on Windows.'
    }
    finally {
        if (-not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
        }
        $process.Dispose()
    }
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $zipName" | Set-Content "$zip.sha256" -Encoding ascii
    Write-Host "Verified $zipName ($hash), source $commit"
}
finally { Pop-Location }
