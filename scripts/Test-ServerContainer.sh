#!/usr/bin/env bash
# Isolated local-only smoke; never accepts an existing container, volume, or URL.
set -Eeuo pipefail
umask 077

# A whole-run bound also leaves time for the exit trap before an outer CI watchdog.
if [[ ${OPENNEST_SMOKE_WATCHDOG:-} != 1 ]]; then
    command -v timeout >/dev/null || { printf 'Missing prerequisite: timeout\n' >&2; exit 2; }
    exec timeout --signal=TERM --kill-after=30s 450s env OPENNEST_SMOKE_WATCHDOG=1 \
        bash "${BASH_SOURCE[0]}" "$@"
fi

usage() {
    printf 'Usage: %s --image <local-image> [--results <directory>]\n' "$0" >&2
}
image=''
results=''
while (($#)); do
    case "$1" in
        --image) [[ $# -ge 2 && -z "$image" ]] || { usage; exit 2; }; image=$2; shift 2 ;;
        --results) [[ $# -ge 2 && -z "$results" ]] || { usage; exit 2; }; results=$2; shift 2 ;;
        *) usage; exit 2 ;;
    esac
done
[[ -n "$image" && "$image" != -* ]] || { usage; exit 2; }
for command in docker dotnet curl timeout mktemp; do
    command -v "$command" >/dev/null || { printf 'Missing prerequisite: %s\n' "$command" >&2; exit 2; }
done
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
# Honor TMPDIR, including all dotnet build outputs and transient synthetic state.
tmp_root=${TMPDIR:-"$HOME/.cache"}
mkdir -p -- "$tmp_root"
results=${results:-"$root/.hermes/progress/server-container-smoke"}
mkdir -p -- "$results"
# Set up logs before allocating scratch, so results-path failures cannot leak it.
# Each invocation gets a private child directory; existing result files are never overwritten.
run_results=$(mktemp -d "$results/run.XXXXXXXXXX")
scratch=$(mktemp -d "$tmp_root/opennest-server-smoke.XXXXXXXXXX")
token=${scratch##*/}
owner_label='com.opennest.server-smoke.owner'
volume="$token-data"
container_names=("$token-first" "$token-replacement")
cidfiles=("$scratch/first.cid" "$scratch/replacement.cid")
volume_attempted=false
container_attempted=(false false)

# Explicit local default context, ignoring environment overrides for remote daemons.
docker_local() { timeout 20s env -u DOCKER_HOST -u DOCKER_CONTEXT docker --context default "$@"; }
owned_container() {
    [[ $(docker_local inspect --format "{{index .Config.Labels \"$owner_label\"}}" "$1") == "$token" ]]
}
cleanup_absent() {
    local kind=$1 target=$2 remaining filter
    local -a inventory
    if [[ "$kind" == container ]]; then
        filter="id=$target"
        if [[ "$target" == "${container_names[0]}" || "$target" == "${container_names[1]}" ]]; then
            # Generated names contain only alphanumerics, hyphens, and dots.
            filter="name=^/${target//./\\.}$"
        fi
        inventory=(container ls --all --quiet --no-trunc --filter "$filter")
    else
        inventory=(volume ls --quiet --filter "name=$target")
    fi
    # A failed inspect is not absence. Only a successful, empty inventory proves it.
    if remaining=$(docker_local "${inventory[@]}" 2>> "$run_results/cleanup.log"); then
        if [[ -z "$remaining" ]]; then
            printf 'Confirmed absent: %s %s\n' "$kind" "$target" >> "$run_results/cleanup.log"
            return 0
        fi
        printf 'Cleanup unknown/incomplete: %s %s remains in inventory: %s\n' \
            "$kind" "$target" "$remaining" >> "$run_results/cleanup.log"
    else
        printf 'Cleanup unknown: %s %s inventory failed status=%s\n' \
            "$kind" "$target" "$?" >> "$run_results/cleanup.log"
    fi
    return 1
}
cleanup_resource() {
    local kind=$1 target=$2 index=${3:-} metadata resource owner
    local -a inspect remove
    if [[ "$kind" == container ]]; then
        inspect=(inspect --format "{{.Id}} {{index .Config.Labels \"$owner_label\"}}")
        remove=(rm -f)
    else
        inspect=(volume inspect --format "{{.Name}} {{index .Labels \"$owner_label\"}}")
        remove=(volume rm)
    fi
    if metadata=$(docker_local "${inspect[@]}" "$target" 2>> "$run_results/cleanup.log"); then
        read -r resource owner <<< "$metadata"
        if [[ -z "$resource" || "$owner" != "$token" ]]; then
            printf 'Cleanup unknown: %s %s owner could not be reconciled; refusing removal.\n' \
                "$kind" "$target" >> "$run_results/cleanup.log"
            return 1
        fi
        if [[ "$kind" == container ]]; then
            # Name recovery resolves a full owned ID before removal, even without a cidfile.
            if [[ ! "$resource" =~ ^[0-9a-f]{64}$ || ( "$target" != "${container_names[0]}" \
                && "$target" != "${container_names[1]}" && "$resource" != "$target" ) ]]; then
                printf 'Cleanup unknown: container %s identity mismatch; refusing removal.\n' \
                    "$target" >> "$run_results/cleanup.log"
                return 1
            fi
            docker_local logs "$resource" > "$run_results/container-$index.log" 2>&1
            docker_local inspect --format 'id={{.Id}} running={{.State.Running}} exit={{.State.ExitCode}}' \
                "$resource" > "$run_results/container-$index-state.log" 2>&1
        elif [[ "$resource" != "$target" ]]; then
            printf 'Cleanup unknown: volume %s identity mismatch; refusing removal.\n' \
                "$target" >> "$run_results/cleanup.log"
            return 1
        fi
        docker_local "${remove[@]}" "$resource" >> "$run_results/cleanup.log" 2>&1 || {
            printf 'Cleanup incomplete: %s %s removal failed status=%s\n' \
                "$kind" "$resource" "$?" >> "$run_results/cleanup.log"
            return 1
        }
        cleanup_absent "$kind" "$resource"
    else
        printf 'Cleanup lookup failed: %s %s status=%s; checking absence.\n' \
            "$kind" "$target" "$?" >> "$run_results/cleanup.log"
        cleanup_absent "$kind" "$target"
    fi
}
cleanup() {
    local status=$? cleanup_failed=0 cid index
    trap - EXIT INT TERM
    set +e
    for index in 0 1; do
        [[ ${container_attempted[$index]} == true ]] || continue
        cid=''
        if [[ -s ${cidfiles[$index]} ]]; then
            read -r cid < "${cidfiles[$index]}" || [[ -n "$cid" ]]
        fi
        cleanup_resource container "${cid:-${container_names[$index]}}" "$index" || cleanup_failed=1
    done
    # Docker volume create may return a preexisting name: delete only a matching owner label.
    if [[ "$volume_attempted" == true ]]; then
        cleanup_resource volume "$volume" || cleanup_failed=1
    fi
    rm -rf -- "$scratch"
    if ((cleanup_failed != 0 && status == 0)); then status=1; fi
    printf 'exit=%s cleanup_failed=%s\n' "$status" "$cleanup_failed" > "$run_results/outcome.log"
    printf 'Smoke exit=%s; logs: %s\n' "$status" "$run_results"
    exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

endpoint=$(docker_local context inspect default --format '{{.Endpoints.docker.Host}}')
[[ "$endpoint" == 'unix:///var/run/docker.sock' ]] || { printf 'Refusing a non-local default Docker endpoint.\n' >&2; exit 2; }
docker_local info --format 'daemon={{.Name}} os={{.OSType}}' > "$run_results/daemon.log"
docker_local image inspect --format 'image={{.Id}}' "$image" > "$run_results/image.log"
printf 'host_sdk=%s\n' "$(dotnet --version)" > "$run_results/toolchain.log"
printf 'owner=%s volume=%s\n' "$token" "$volume" > "$run_results/ownership.log"
timeout 180s dotnet build "$root/scripts/Server.Tests/Server.Tests.csproj" -c Release \
    --artifacts-path "$scratch/build" -m:1 /nodeReuse:false > "$run_results/build.log" 2>&1
dll="$scratch/build/bin/Server.Tests/release/Server.Tests.dll"
[[ -f "$dll" ]] || { printf 'Smoke build did not produce the expected DLL.\n' >&2; exit 1; }
# Refuse collisions before creation; the post-create label is the authoritative ownership proof.
if docker_local volume inspect "$volume" >/dev/null 2>&1; then
    printf 'Refusing a preexisting volume name.\n' >&2; exit 1
fi
volume_attempted=true
docker_local volume create --label "$owner_label=$token" "$volume" > "$run_results/volume.log"
[[ $(docker_local volume inspect --format "{{index .Labels \"$owner_label\"}}" "$volume") == "$token" ]] \
    || { printf 'Volume ownership could not be established.\n' >&2; exit 1; }

start_container() {
    local index=$1 cid binding
    if docker_local inspect "${container_names[$index]}" >/dev/null 2>&1; then
        printf 'Refusing a preexisting container name.\n' >&2; return 1
    fi
    container_attempted[$index]=true
    docker_local create --name "${container_names[$index]}" --cidfile "${cidfiles[$index]}" \
        --label "$owner_label=$token" --publish 127.0.0.1::8090 \
        --mount "type=volume,src=$volume,dst=/app/data" "$image" > "$run_results/create-$index.log"
    read -r cid < "${cidfiles[$index]}" || [[ -n "$cid" ]]
    owned_container "$cid" || { printf 'Container ownership could not be established.\n' >&2; return 1; }
    docker_local start "$cid" > "$run_results/start-$index.log"
    binding=$(docker_local port "$cid" 8090/tcp)
    [[ "$binding" =~ ^127\.0\.0\.1:([0-9]+)$ ]] || { printf 'Unexpected port binding.\n' >&2; return 1; }
    url="http://127.0.0.1:${BASH_REMATCH[1]}"
    printf 'id=%s binding=%s\n' "$cid" "$binding" >> "$run_results/ownership.log"
    local deadline=$((SECONDS + 45))
    while ((SECONDS < deadline)); do
        [[ $(docker_local inspect --format '{{.State.Running}}' "$cid") == true ]] \
            || { printf 'Container exited before readiness.\n' >&2; return 1; }
        if curl --noproxy '*' --fail --silent --show-error --connect-timeout 1 --max-time 2 \
            "$url/healthz" > "$scratch/health.json" 2>> "$run_results/readiness-$index.log"; then
            [[ $(< "$scratch/health.json") == '{"status":"ok"}' ]] && return 0
        fi
        sleep 1
    done
    printf 'Container readiness timed out.\n' >&2; return 1
}

start_container 0
timeout 100s dotnet "$dll" seed --url "$url" --state "$scratch/state.json" > "$run_results/seed.log" 2>&1
timeout 100s dotnet "$dll" reject --url "$url" > "$run_results/reject.log" 2>&1
read -r first_cid < "${cidfiles[0]}" || [[ -n "$first_cid" ]]
docker_local logs "$first_cid" > "$run_results/container-0.log" 2>&1
docker_local inspect --format 'id={{.Id}} running={{.State.Running}} exit={{.State.ExitCode}}' \
    "$first_cid" > "$run_results/container-0-state.log"
docker_local stop --time 10 "$first_cid" > "$run_results/stop-0.log"
docker_local rm "$first_cid" > "$run_results/remove-0.log"
# The replacement gets another Docker-allocated loopback port; only the volume is reused.
start_container 1
timeout 100s dotnet "$dll" verify --url "$url" --state "$scratch/state.json" > "$run_results/verify.log" 2>&1
printf 'PASS: real-client create/update/copy, rejections, and persistent recreation.\n'
