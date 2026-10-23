<#
Build the OpenNest.Server image from this checkout with the local Docker engine
and redeploy it to a remote Docker Compose stack over SSH.

Steps: docker build -> docker save -> scp -> docker load on the host -> stop the
running container -> back up its data volume -> point the stack's compose.yml at
the new tag -> docker compose up -d -> wait for the container's health check.

The image is a development build (VERSION stays 0.0.0-dev); its tag is
yyyyMMdd-<short sha>, with "-dirty" when server sources have uncommitted
changes, and SOURCE_REVISION records the full commit. A new server can migrate
the database schema, and older servers refuse a newer schema, so roll back by
restoring the volume backup together with the previous tag (both are printed).

The host and stack paths are parameters so no private address lands in source.

Examples:
  powershell -ExecutionPolicy Bypass -File scripts/Deploy-Server.ps1 `
      -DeployHost myhost -StackDir ~/docker/opennest
  powershell -ExecutionPolicy Bypass -File scripts/Deploy-Server.ps1 `
      -DeployHost myhost -StackDir ~/docker/opennest `
      -LocalComposeFile D:\path\to\config-repo\opennest\compose.yml
#>

Param(
    [Parameter(Mandatory)]
    [string]$DeployHost,
    [string]$DeployUser = "aj",
    # Directory on the host that holds the stack's compose.yml.
    [Parameter(Mandatory)]
    [string]$StackDir,
    [string]$ImageName = "opennest-server",
    # Defaults to yyyyMMdd-<short sha>[-dirty].
    [string]$Tag,
    [string]$ContainerName = "opennest",
    [string]$VolumeName = "opennest-data",
    # Volume backups go to <BackupRoot>/opennest-<tag>/backup/ on the host.
    [string]$BackupRoot = "~/deploy",
    # Optional local copy of the stack's compose.yml (e.g. a config repo) to
    # update with the new tag; it is edited, not committed.
    [string]$LocalComposeFile,
    [int]$HealthTimeoutSeconds = 120,
    [switch]$SkipBackup,
    # Skip --pull so cached base images are used.
    [switch]$NoPull
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $PSCommandPath
$RepoRoot = if ((Split-Path -Leaf $ScriptDir) -eq 'scripts') { Split-Path -Parent $ScriptDir } else { $ScriptDir }
Write-Host "Repository root: $RepoRoot"

$Dockerfile = Join-Path $RepoRoot 'OpenNest.Server\Dockerfile'
if (-not (Test-Path -LiteralPath $Dockerfile)) { throw "Dockerfile not found at: $Dockerfile" }

foreach ($tool in 'docker', 'git', 'ssh', 'scp') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool was not found on PATH" }
}

$Revision = (& git -C $RepoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw "git rev-parse failed" }
$ShortRevision = $Revision.Substring(0, 8)
$Branch = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD).Trim()

# Only the directories the Dockerfile copies affect the image.
$ServerSources = 'OpenNest.Server', 'OpenNest.Data', 'OpenNest.Core'
$Dirty = & git -C $RepoRoot status --porcelain -- $ServerSources
if ($Dirty) {
    Write-Warning "Uncommitted changes in server sources are included in this build:"
    $Dirty | ForEach-Object { Write-Warning "  $_" }
}

if (-not $Tag) {
    $Tag = "{0}-{1}" -f (Get-Date -Format 'yyyyMMdd'), $ShortRevision
    if ($Dirty) { $Tag += "-dirty" }
}
$Image = "${ImageName}:$Tag"
$Remote = "${DeployUser}@${DeployHost}"
Write-Host "Building $Image from $Branch @ $ShortRevision"

$buildArgs = @('build', '-f', $Dockerfile, '-t', $Image, '--build-arg', "SOURCE_REVISION=$Revision")
if (-not $NoPull) { $buildArgs += '--pull' }
$buildArgs += $RepoRoot
& docker @buildArgs
if ($LASTEXITCODE -ne 0) { throw "docker build failed with exit code $LASTEXITCODE" }

$WorkDir = Join-Path ([IO.Path]::GetTempPath()) "opennest-server-deploy-$Tag"
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$ImageTar = Join-Path $WorkDir "$ImageName-$Tag.tar"
$RemoteScriptPath = Join-Path $WorkDir 'deploy.sh'

# PowerShell 5.1 pipes between native commands as text, so save to a file
# instead of streaming docker save into ssh.
Write-Host "Saving image to $ImageTar..."
& docker save -o $ImageTar $Image
if ($LASTEXITCODE -ne 0) { throw "docker save failed with exit code $LASTEXITCODE" }

$remoteScript = @'
set -euo pipefail
tar_file=$1; image_name=$2; tag=$3; stack_dir=$4; container=$5; volume=$6
backup_root=$7; skip_backup=$8; health_timeout=$9
image="$image_name:$tag"
expand() { case $1 in "~"|"~/"*) printf '%s\n' "$HOME${1#\~}";; *) printf '%s\n' "$1";; esac; }
stack_dir=$(expand "$stack_dir"); backup_root=$(expand "$backup_root")

echo "Loading $image..."
docker load -i "$tar_file"
rm -f "$tar_file"

cd "$stack_dir"
previous=$(sed -nE "s/^[[:space:]]*image:[[:space:]]*$image_name:([^[:space:]]+).*/\1/p" compose.yml | head -n1)
[ -n "$previous" ] || { echo "No 'image: $image_name:<tag>' line in $stack_dir/compose.yml" >&2; exit 1; }
echo "Current tag: $previous"

backup_dir=
if [ "$skip_backup" != "1" ]; then
    backup_dir="$backup_root/opennest-$tag/backup"
    mkdir -p "$backup_dir"
    cp compose.yml "$backup_dir/compose.yml.before"
fi

echo "Stopping $container..."
docker compose stop

if [ -n "$backup_dir" ]; then
    echo "Backing up volume $volume to $backup_dir..."
    docker run --rm --user 0 --entrypoint tar \
        -v "$volume":/data:ro -v "$backup_dir":/backup \
        "$image" czf "/backup/$volume.tar.gz" -C /data .
    ls -lh "$backup_dir"
fi

sed -i -E "s|^([[:space:]]*image:[[:space:]]*)$image_name:[^[:space:]]+|\1$image|" compose.yml
docker compose up -d

echo "Waiting for $container to report healthy..."
deadline=$((SECONDS + health_timeout))
status=
while [ $SECONDS -lt $deadline ]; do
    status=$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container" 2>/dev/null || true)
    [ "$status" = healthy ] && break
    sleep 2
done

if [ "$status" != healthy ]; then
    echo "$container did not become healthy (status: ${status:-unknown}). Recent logs:" >&2
    docker logs --tail 50 "$container" >&2 || true
    echo >&2
    echo "Roll back: docker compose stop; restore $backup_dir/$volume.tar.gz into $volume;" >&2
    echo "set the compose image back to $image_name:$previous; docker compose up -d" >&2
    exit 1
fi

echo "Deployed $image (previous: $previous)."
[ -z "$backup_dir" ] || echo "Volume backup: $backup_dir/$volume.tar.gz"
'@
# bash needs LF line endings; write without a BOM.
[IO.File]::WriteAllText($RemoteScriptPath, ($remoteScript -replace "`r`n", "`n"), (New-Object Text.UTF8Encoding $false))

try {
    $RemoteTar = "/tmp/$ImageName-$Tag.tar"
    $RemoteScript = "/tmp/opennest-server-deploy-$Tag.sh"
    Write-Host "Copying image to ${Remote}..."
    & scp $ImageTar "${Remote}:$RemoteTar"
    if ($LASTEXITCODE -ne 0) { throw "scp of the image failed" }
    & scp $RemoteScriptPath "${Remote}:$RemoteScript"
    if ($LASTEXITCODE -ne 0) { throw "scp of the deploy script failed" }

    $skip = if ($SkipBackup) { '1' } else { '0' }
    & ssh $Remote bash $RemoteScript $RemoteTar $ImageName $Tag "'$StackDir'" $ContainerName $VolumeName "'$BackupRoot'" $skip $HealthTimeoutSeconds
    $deployExit = $LASTEXITCODE
    & ssh $Remote rm -f $RemoteScript
    if ($deployExit -ne 0) { throw "Remote deploy failed with exit code $deployExit (see output above)" }
}
finally {
    Remove-Item -LiteralPath $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
}

if ($LocalComposeFile) {
    $compose = Get-Content -LiteralPath $LocalComposeFile -Raw
    $updated = [regex]::Replace($compose, "(?m)^(\s*image:\s*)$([regex]::Escape($ImageName)):\S+", "`${1}$Image")
    if ($updated -eq $compose) {
        Write-Warning "No '$ImageName' image line changed in $LocalComposeFile"
    }
    else {
        [IO.File]::WriteAllText($LocalComposeFile, $updated, (New-Object Text.UTF8Encoding $false))
        Write-Host "Updated $LocalComposeFile to $Image (not committed)."
    }
}

Write-Host "Done: $Image is running on $DeployHost."
