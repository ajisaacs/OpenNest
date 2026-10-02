#!/usr/bin/env bash
# Isolated local-only smoke; never accepts an existing container, volume, or URL.
set -Eeuo pipefail
umask 077

# A whole-run bound also leaves time for the exit trap before an outer CI watchdog.
if [[ ${OPENNEST_SMOKE_WATCHDOG:-} != 1 ]]; then
    command -v timeout >/dev/null || { printf 'Missing prerequisite: timeout\n' >&2; exit 2; }
    exec timeout --signal=TERM --kill-after=30s 600s env OPENNEST_SMOKE_WATCHDOG=1 \
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
for command in docker dotnet curl timeout mktemp tar; do
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
# Two service generations on one data volume, a stopped-service backup and restore into
# a second volume, its service, then two storage locations that must fail startup.
container_roles=(first replacement backup restore restored readonly unwritable)
container_names=()
cidfiles=()
container_attempted=()
for role in "${container_roles[@]}"; do
    container_names+=("$token-$role")
    cidfiles+=("$scratch/$role.cid")
    container_attempted+=(false)
done
volumes=("$token-data" "$token-restore-data")
volume_attempted=(false false)

# Explicit local default context, ignoring environment overrides for remote daemons.
docker_local() { timeout 20s env -u DOCKER_HOST -u DOCKER_CONTEXT docker --context default "$@"; }
owned_container() {
    [[ $(docker_local inspect --format "{{index .Config.Labels \"$owner_label\"}}" "$1") == "$token" ]]
}
generated_name() {
    local name
    for name in "${container_names[@]}"; do
        [[ "$1" == "$name" ]] && return 0
    done
    return 1
}
cleanup_absent() {
    local kind=$1 target=$2 remaining filter
    local -a inventory
    if [[ "$kind" == container ]]; then
        filter="id=$target"
        if generated_name "$target"; then
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
            if [[ ! "$resource" =~ ^[0-9a-f]{64}$ ]] \
                || { ! generated_name "$target" && [[ "$resource" != "$target" ]]; }; then
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
    for index in "${!container_names[@]}"; do
        [[ ${container_attempted[$index]} == true ]] || continue
        cid=''
        if [[ -s ${cidfiles[$index]} ]]; then
            read -r cid < "${cidfiles[$index]}" || [[ -n "$cid" ]]
        fi
        cleanup_resource container "${cid:-${container_names[$index]}}" "$index" || cleanup_failed=1
    done
    # Docker volume create may return a preexisting name: delete only a matching owner label.
    for index in "${!volumes[@]}"; do
        [[ ${volume_attempted[$index]} == true ]] || continue
        cleanup_resource volume "${volumes[$index]}" || cleanup_failed=1
    done
    rm -rf -- "$scratch"
    if ((cleanup_failed != 0 && status == 0)); then status=1; fi
    printf 'exit=%s cleanup_failed=%s\n' "$status" "$cleanup_failed" > "$run_results/outcome.log"
    printf 'Smoke exit=%s; logs: %s\n' "$status" "$run_results"
    exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

fail() { printf '%s\n' "$1" >&2; exit 1; }

endpoint=$(docker_local context inspect default --format '{{.Endpoints.docker.Host}}')
[[ "$endpoint" == 'unix:///var/run/docker.sock' ]] || { printf 'Refusing a non-local default Docker endpoint.\n' >&2; exit 2; }
docker_local info --format 'daemon={{.Name}} os={{.OSType}} server={{.ServerVersion}}' > "$run_results/daemon.log"

# Image contract: numeric non-root user, the HTTP healthcheck, and the source label.
docker_local image inspect --format 'image={{.Id}}' "$image" > "$run_results/image.log"
image_user=$(docker_local image inspect --format '{{.Config.User}}' "$image")
healthcheck=$(docker_local image inspect --format '{{.Config.Healthcheck.Test}} {{.Config.Healthcheck.Interval}} {{.Config.Healthcheck.Timeout}} {{.Config.Healthcheck.StartPeriod}} {{.Config.Healthcheck.StartInterval}} {{.Config.Healthcheck.Retries}}' "$image")
labels=$(docker_local image inspect --format '{{index .Config.Labels "org.opencontainers.image.source"}} {{index .Config.Labels "org.opencontainers.image.version"}} {{index .Config.Labels "org.opencontainers.image.revision"}}' "$image")
printf 'user=%s\nhealthcheck=%s\nsource/version/revision=%s\n' "$image_user" "$healthcheck" "$labels" >> "$run_results/image.log"
[[ "$image_user" =~ ^[1-9][0-9]*$ ]] || fail 'Image must declare a numeric non-root USER.'
[[ "$healthcheck" == '[CMD curl --fail --silent --show-error http://127.0.0.1:8090/healthz] 30s 5s 10s 2s 3' ]] \
    || fail 'Image HEALTHCHECK does not match the documented probe.'
[[ "$labels" == 'https://github.com/ajisaacs/OpenNest '?*' '?* ]] || fail 'Image OCI source/version/revision labels are missing.'

printf 'host_sdk=%s\n' "$(dotnet --version)" > "$run_results/toolchain.log"
printf 'owner=%s volumes=%s\n' "$token" "${volumes[*]}" > "$run_results/ownership.log"
timeout 180s dotnet build "$root/scripts/Server.Tests/Server.Tests.csproj" -c Release \
    --artifacts-path "$scratch/build" -m:1 /nodeReuse:false > "$run_results/build.log" 2>&1
dll="$scratch/build/bin/Server.Tests/release/Server.Tests.dll"
[[ -f "$dll" ]] || fail 'Smoke build did not produce the expected DLL.'

create_volume() {
    local index=$1 name=${volumes[$1]}
    # Refuse collisions before creation; the post-create label is the authoritative ownership proof.
    if docker_local volume inspect "$name" >/dev/null 2>&1; then
        printf 'Refusing a preexisting volume name.\n' >&2; return 1
    fi
    volume_attempted[$index]=true
    docker_local volume create --label "$owner_label=$token" "$name" >> "$run_results/volume.log"
    [[ $(docker_local volume inspect --format "{{index .Labels \"$owner_label\"}}" "$name") == "$token" ]] \
        || { printf 'Volume ownership could not be established.\n' >&2; return 1; }
}

# Sets the global cid. Every container gets the Compose example's privilege hardening.
create_container() {
    local index=$1
    shift
    if docker_local container inspect "${container_names[$index]}" >/dev/null 2>&1; then
        printf 'Refusing a preexisting container name.\n' >&2; return 1
    fi
    container_attempted[$index]=true
    docker_local create --name "${container_names[$index]}" --cidfile "${cidfiles[$index]}" \
        --label "$owner_label=$token" --cap-drop ALL --security-opt no-new-privileges:true \
        "$@" > "$run_results/create-$index.log"
    cid=''
    read -r cid < "${cidfiles[$index]}" || [[ -n "$cid" ]]
    owned_container "$cid" || { printf 'Container ownership could not be established.\n' >&2; return 1; }
    printf 'index=%s name=%s id=%s\n' "$index" "${container_names[$index]}" "$cid" >> "$run_results/ownership.log"
}

# PID 1 itself, not merely an exec session, must be the image user without capabilities.
check_runtime() {
    local index=$1 key a b c d uids='' cap_eff='' no_new_privs='' db_owner
    docker_local exec "$cid" cat /proc/1/status > "$scratch/status-$index"
    while IFS=$'\t' read -r key a b c d; do
        case "$key" in
            Uid:) uids="$a $b $c $d" ;;
            CapEff:) cap_eff=$a ;;
            NoNewPrivs:) no_new_privs=$a ;;
        esac
    done < "$scratch/status-$index"
    db_owner=$(docker_local exec "$cid" stat -c '%u' /app/data/nests.db)
    printf 'index=%s uids=%s cap_eff=%s no_new_privs=%s db_owner=%s\n' \
        "$index" "$uids" "$cap_eff" "$no_new_privs" "$db_owner" >> "$run_results/runtime.log"
    [[ "$uids" == "$image_user $image_user $image_user $image_user" ]] || fail 'Server process is not the image user.'
    [[ "$cap_eff" =~ ^0+$ && "$no_new_privs" == 1 ]] || fail 'Server process retained capabilities or privilege escalation.'
    [[ "$db_owner" == "$image_user" ]] || fail 'Database is not owned by the image user.'
    docker_local exec "$cid" sh -c 'test -w /app/data && test ! -w /app && test ! -w /app/OpenNest.Server.dll' \
        || fail 'Within /app, only the data directory may be writable by the server user.'
}

start_service() {
    local index=$1 volume_name=$2 binding state deadline
    create_container "$index" --publish 127.0.0.1::8090 \
        --mount "type=volume,src=$volume_name,dst=/app/data" "$image"
    docker_local start "$cid" > "$run_results/start-$index.log"
    binding=$(docker_local port "$cid" 8090/tcp)
    [[ "$binding" =~ ^127\.0\.0\.1:([0-9]+)$ ]] || { printf 'Unexpected port binding.\n' >&2; return 1; }
    url="http://127.0.0.1:${BASH_REMATCH[1]}"
    printf 'index=%s binding=%s\n' "$index" "$binding" >> "$run_results/ownership.log"
    # Ready means Docker's own in-image probe reports healthy and the host sees the same body.
    deadline=$((SECONDS + 60))
    while ((SECONDS < deadline)); do
        state=$(docker_local inspect --format '{{.State.Running}} {{.State.Health.Status}}' "$cid")
        [[ "$state" == 'true '* ]] || { printf 'Container exited before readiness.\n' >&2; return 1; }
        [[ "$state" != 'true unhealthy' ]] || { printf 'Container reported unhealthy.\n' >&2; return 1; }
        if [[ "$state" == 'true healthy' ]] && curl --noproxy '*' --fail --silent --show-error \
            --connect-timeout 1 --max-time 2 "$url/healthz" > "$scratch/health.json" \
            2>> "$run_results/readiness-$index.log"; then
            if [[ $(< "$scratch/health.json") == '{"status":"ok"}' ]]; then
                check_runtime "$index"
                return 0
            fi
        fi
        sleep 1
    done
    printf 'Container readiness timed out.\n' >&2; return 1
}

retire_service() {
    local index=$1 retired
    read -r retired < "${cidfiles[$index]}" || [[ -n "$retired" ]]
    docker_local logs "$retired" > "$run_results/container-$index.log" 2>&1
    docker_local inspect --format 'id={{.Id}} running={{.State.Running}} exit={{.State.ExitCode}}' \
        "$retired" > "$run_results/container-$index-state.log"
    docker_local stop --time 10 "$retired" > "$run_results/stop-$index.log"
    docker_local rm "$retired" > "$run_results/remove-$index.log"
}

# An unusable data location must stop the service, never leave a healthy but unusable one.
expect_startup_failure() {
    local index=$1 state='' running exit_code health deadline
    shift
    create_container "$index" --network none "$@" "$image"
    docker_local start "$cid" > "$run_results/start-$index.log"
    deadline=$((SECONDS + 45))
    while ((SECONDS < deadline)); do
        state=$(docker_local inspect --format '{{.State.Running}} {{.State.ExitCode}} {{.State.Health.Status}}' "$cid")
        [[ "$state" != *' healthy' ]] || fail 'A service with unusable storage reported healthy.'
        [[ "$state" == 'false '* ]] && break
        sleep 1
    done
    read -r running exit_code health <<< "$state"
    docker_local logs "$cid" > "$run_results/startup-failure-$index.log" 2>&1
    printf 'index=%s running=%s exit=%s health=%s\n' "$index" "$running" "$exit_code" "$health" \
        >> "$run_results/startup-failures.log"
    [[ "$running" == false && "$exit_code" != 0 && "$health" != healthy ]] \
        || fail 'A service with unusable storage did not fail startup.'
    [[ $(< "$run_results/startup-failure-$index.log") == *SqliteException* ]] \
        || fail 'Startup failed for a reason other than storage.'
}

run_tool() {
    local mode=$1
    shift
    timeout 100s dotnet "$dll" "$mode" --url "$url" "$@" > "$run_results/$mode-$service.log" 2>&1
}

# A fresh named volume must be writable by the non-root image user.
create_volume 0
service=first
start_service 0 "${volumes[0]}"
run_tool seed --state "$scratch/state.json"
run_tool reject
run_tool limits
retire_service 0
# The replacement gets another Docker-allocated loopback port; only the volume is reused.
service=replacement
start_service 1 "${volumes[0]}"
run_tool verify --state "$scratch/state.json"
retire_service 1

# Back up the whole data directory from the stopped service (SQLite WAL makes a live
# nests.db copy insufficient), then restore into a separate volume and verify it.
create_container 2 --rm --network none --entrypoint tar \
    --mount "type=volume,src=${volumes[0]},dst=/app/data,readonly" "$image" -C /app/data -cf - .
docker_local start --attach "$cid" > "$scratch/data-backup.tar" 2> "$run_results/backup.log"
tar -tvf "$scratch/data-backup.tar" > "$run_results/backup-contents.log"
backup_entries=$(tar -tf "$scratch/data-backup.tar")
[[ $'\n'"$backup_entries"$'\n' == *$'\n./nests.db\n'* ]] || fail 'Backup does not contain nests.db.'
expect_startup_failure 5 --mount "type=volume,src=${volumes[0]},dst=/app/data,readonly"
expect_startup_failure 6 --mount type=tmpfs,dst=/app/data,tmpfs-mode=0755
create_volume 1
create_container 3 --rm --interactive --network none --entrypoint tar \
    --mount "type=volume,src=${volumes[1]},dst=/app/data" "$image" -C /app/data -xf -
docker_local start --attach --interactive "$cid" < "$scratch/data-backup.tar" > "$run_results/restore.log" 2>&1
service=restored
start_service 4 "${volumes[1]}"
run_tool verify --state "$scratch/state.json"
printf 'PASS: non-root healthy runtime, real-client round trips, rejections, upload limit, persistent recreation, stopped-service backup/restore, and storage startup failures.\n'
