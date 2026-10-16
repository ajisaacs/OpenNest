# Nest storage: File mode vs. Database mode

The desktop app can save nests two ways:

- **File mode** (default, unchanged behavior): Save/Save As write a `.nest` ZIP
  archive to disk via the normal file dialog. See [nest-file-format.md](nest-file-format.md).
- **Database mode**: Open browses server records 100 at a time. The Find box
  searches name, customer, material, made by, comments and status on the server
  after a short typing pause (Enter in the box searches at once); clicking a
  column header sorts every match on the server (click again to reverse); the
  arrow buttons move between pages and the title and find bar show the range and
  total. Dates and numbers are not searched as text; sort their column instead.
  The selected sort column and direction are remembered for the current Windows
  user when the browser closes, including across app restarts. Until a selection
  is saved, nests appear newest-saved first.
  Below the list, the **Details** tabs show the highlighted nest's plates
  (duplicates, size, parts, drawings, utilization), drawings (required, nested,
  remaining, area), and **Versions** (bounded current and earlier snapshots).
  Plates and drawings come from the archive downloaded shortly after the
  highlight stops moving; Versions loads on selection and follows the selected
  nest without opening another window. Beside the tabs, a preview draws one
  plate of the highlighted nest; the arrow buttons below it step through the
  plates, and selecting a row on the Plates tab shows that plate. Enter on the
  nest list or a double-click opens the highlighted nest; Esc closes. Save creates a record on
  first save and updates that same record afterward. Save As creates a new record
  (a copy). A failed save keeps the previous record association; switching server
  URLs creates a record on the new server rather than updating an id from the
  previous one. On the Versions tab, **Open read-only...** previews one snapshot
  without binding an editable document, and **Download selected version...** saves
  its exact archived bytes.
  Select a nest and click **Nest Information...** above the list (or use the
  row's right-click menu) to edit the same information as **Nest > Nest Info**
  without opening the nest in the main editor. **OK** saves immediately to the
  same database ID as a new version, then refreshes the list, preview and history;
  **Cancel** makes no change. The dialog starts with the latest saved metadata.
  Plate defaults follow the same rules as the open editor: populated sheets are
  not resized or re-nested. This action requires a version-aware server and
  refuses to overwrite a newer save from another PC. After a conflict or network
  error, refresh and reopen the information dialog before trying again; a lost
  reply can mean the save already reached the server. An already-open editor is
  not modified; reload it before saving further changes to that same nest.
  A separate confirmed **Restore selected version...** creates a new current
  revision of the same ID and keeps all intervening versions. **Delete...** hides
  the record from ordinary browsing but retains every archive; **Deleted nests...**
  opens an explicit recovery view. Neither action purges data. An old desktop
  without a revision precondition cannot update or delete on an upgraded server.
  A separate **File > Export .nest...** command is always available (in both
  modes) for producing a local file to share or back up. Export does not change
  the document's file save path or database record association.

Dragging one or more `.nest` files onto the main window (or onto an open
plate) opens them from disk directly, in either mode — this is the only way
to open a local file while in Database mode, since Open there browses server
records instead of showing a file dialog. A document opened this way is not
bound to a server record, so a subsequent Save in Database mode creates a new
record on first save, same as any other unbound document.

The mode and server address are stored per-PC at `%APPDATA%\OpenNest\storage.json`
(`OpenNest.Data.NestStorageSettings`), defaulting to File mode so existing
installs are unaffected until an operator opts in via **File > Storage Mode...**.

## Wire contract

`OpenNest.Data.NestRecord` is the metadata DTO shared by the client
(`RemoteNestRepository`) and the server (`OpenNest.Server`). JSON is camelCase
with enums serialized as strings (`JsonSerializerDefaults.Web` +
`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`), matching the rest of
`OpenNest.Data`'s local JSON stores.

| Field | Type | Notes |
|---|---|---|
| `id` | guid | Client-generated on first save; kept on updates. |
| `name` | string | |
| `customer` | string | |
| `dateCreated` / `dateModified` | datetime | Set by the client from the nest's own metadata. |
| `material` | string | |
| `thickness` | number | |
| `status` | `"quote"` \| `"toBeCut"` \| `"hasBeenCut"` | Default `quote`. |
| `plateCount` / `partCount` | integer | Computed client-side at save time (non-cutoff parts only). |
| `comments` | string | |
| `madeBy` | string | |
| `fileSize` | integer | Server-computed; ignored on upload. |
| `savedAt` | datetime | Server-computed; ignored on upload. |
| `revision` | integer | Current immutable revision; zero on a legacy server without history. |
| `deleted` | boolean | Hidden from ordinary reads when true; retained in recovery/backup. |
| `restoredFromRevision` | nullable integer | Historical source promoted by Restore; not the preceding revision. |

## Endpoints

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/healthz` | — | 200 `{ "status": "ok" }` after a live database query; 503 `{ "status": "unavailable" }` on storage failure (no internal details) |
| GET | `/api/nests` | — | `NestRecord[]`, newest `savedAt` first. Full, unbounded enumeration for backup manifests, restore checks and the container smoke; browsing uses `/api/nests/query` |
| GET | `/api/nests/query?search=&sort=&order=&offset=&limit=` | — | `{ "items": NestRecord[], "total", "offset", "limit" }`: one bounded page filtered in SQL (see below), or 400 for an invalid parameter |
| GET | `/api/nests/{id}` | — | `NestRecord` or 404 |
| GET | `/api/nests/{id}/file` | — | `.nest` archive bytes (`application/zip`) or 404 |
| POST | `/api/nests` | multipart: `metadata` (JSON `NestRecord`) + `file` (`.nest` bytes) | `NestRecord` with server-assigned `id` when the client sends an empty guid |
| PUT | `/api/nests/{id}/file` | multipart: `metadata` + `file` | Updated `NestRecord`, or 404 if `id` is unknown |
| PUT | `/api/nests/{id}/metadata` | JSON `NestRecord` | Updated `NestRecord` (archive untouched), or 404 |
| DELETE | `/api/nests/{id}` | — | Updated hidden `NestRecord`, or 404 |
| GET | `/api/nests/{id}/revisions?offset=0&limit=100` | — | Metadata page, newest first; limit 1–200 |
| GET | `/api/nests/{id}/revisions/{revision}/file` | — | Exact immutable archive bytes |
| POST | `/api/nests/{id}/revisions/{revision}/restore` | — | New current revision of the same ID, with source metadata/bytes |
| GET | `/api/nests/deleted/query` | — | Bounded deleted-record page; same query parameters as ordinary browsing |
| GET | `/api/nests/deleted/{id}` / `/{id}/file` / `/{id}/revisions` / `/{id}/revisions/{revision}/file` | — | Recovery equivalents of current/history reads |
| POST | `/api/nests/deleted/{id}/recover` | — | New nondeleted revision of the same ID |

All existing-record mutations require `If-Match: "<current revision>"`.
Missing precondition returns 428 (upgrade/reload), malformed precondition 400,
stale precondition 409 with current metadata; none mutate data. Ordinary reads
of hidden records return 404. Changed file/metadata saves and visibility changes
append snapshots atomically; an identical save at the current revision is a no-op.
Restore appends a new snapshot, never rewrites its source or intervening history.

There is no authentication; this is a LAN-only service. Do not expose it
outside the shop network without adding one.

### Browsing query

`GET /api/nests/query` filters, orders and pages in SQLite, so a client receives
only the requested page of metadata (never archive bytes):

- `search` (optional): trimmed, at most 200 characters, no NUL characters;
  blank means no filter.
  Case-insensitive substring of the whole text in `name`, `customer`, `material`,
  `madeBy`, `comments` or the status (`ToBeCut` or the display name `To Be Cut`).
  `%`, `_` and `\` are literal. Dates and numbers are not matched as text.
  Case folding is ASCII-only (SQLite `LIKE`).
- `offset` (default 0, at least 0) and `limit` (default 100, 1 to 500).
- `sort` (default `savedAt`): one of `savedAt`, `name`, `customer`, `status`,
  `material`, `dateCreated`, `dateModified`, `thickness`, `plateCount`,
  `partCount`, `madeBy`, `comments`, `fileSize` (names, case-insensitive; no
  numbers). Text columns sort case-insensitively (ASCII); dates sort as their
  stored ISO text. `order` is `asc` or `desc` (default `desc`).
- Ties are broken by `id` in the same direction, so pages partition the matches
  deterministically. `total` counts every match and is read with the page in one
  database hold. A save between two page requests can move a row across a page
  boundary.
- Unknown, repeated, non-integer or out-of-range parameters return 400 instead of
  being ignored or clamped. `RemoteNestRepository.QueryAsync` checks the same
  bounds before sending, and reports an HTTP 404 from an older server as a
  server version that must be updated.

## Drawing library endpoints

The server also keeps a shared library of drawings (schema v4). The desktop
adds library drawings to a nest and saves nest drawings to the library (below).
A drawing's geometry travels as an opaque
**drawing package**: a `.nest` archive holding exactly one drawing and no plates,
written and read on the client by `OpenNest.IO.DrawingPackage`. The server never
parses it.

`OpenNest.Data.DrawingLibraryRecord` fields: `id` (client-allocated before the
first upload), `revision`, `name`, `customer`, `materialName`, `materialGrade`,
`thickness` (stored rounded to 4 decimals), `units` (`inches` | `millimeters`),
`cutType` (`laser` | `waterjet` | `plasma`), `notes`, `archived`, `savedBy`, and
the server-computed `packageSha256` (lower-case hex), `packageSize` and `savedAt`.

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/drawings` | multipart: `metadata` (JSON record with a nonempty `id`) + `package` | Revision 1; 409 with the current record when the id exists |
| GET | `/api/drawings/query?search=&customer=&materialName=&materialGrade=&thickness=&cutType=&includeArchived=&sort=&order=&offset=&limit=` | — | `{ items, total, offset, limit }` |
| GET | `/api/drawings/{id}` / `/{id}/file` | — | Current record / package bytes (archived drawings included), or 404 |
| GET | `/api/drawings/{id}/revisions?offset=0&limit=100` | — | Revision page, newest first; limit 1–200 |
| GET | `/api/drawings/{id}/revisions/{revision}/file` | — | Exact immutable package bytes |
| PUT | `/api/drawings/{id}/file` | multipart: `metadata` + `package` | New revision |
| PUT | `/api/drawings/{id}/metadata` | JSON record | New revision with the same package |
| POST | `/api/drawings/{id}/archive` / `/restore` | — | New revision with `archived` changed |

- Existing-drawing writes require `If-Match` exactly as for nests: 428 missing,
  400 malformed, 409 stale with the current record. Writing to an archived
  drawing, archiving an archived one or restoring an active one is also 409.
  None of these change data. There is no delete; archive hides a drawing from
  the query unless `includeArchived=true`.
- Every change appends an immutable revision in the same transaction that
  updates the current row, including metadata-only edits and archive/restore.
  An identical save at the current revision is a no-op.
- Validation (400, no state change): `cutType` and `units` must be single enum
  names (no numbers or combinations); `thickness` finite and at least 0; `name`
  nonblank; name, customer, material, grade and savedBy at most 200 characters,
  notes at most 4000; no NUL characters; package nonempty, at most 16 MiB and
  starting with a ZIP signature. Metadata `id` on a PUT must be empty or match
  the route.
- Query: `search` is a literal, case-insensitive (ASCII) substring of name,
  customer or notes (`%`, `_`, `\` literal). `customer`, `materialName` and
  `materialGrade` match exactly after `SharedListNames.Key` normalization
  (trimmed, collapsed spaces, case-insensitive); blank means any. `thickness`
  matches exactly after rounding to 4 decimals. `sort` is one of `savedAt`
  (default), `name`, `customer`, `materialName`, `materialGrade`, `thickness`,
  `cutType`, `revision`; ties break by `id`. Paging, `order` and the refusal of
  unknown, repeated or out-of-range parameters follow the nest query.
- An older server answers 404 on these routes; the client reports that the
  server must be updated and never falls back to another store.

Schema v4 adds the `drawings` and `drawing_revisions` tables to the same file
and leaves the nest tables untouched. A server older than v4 refuses to open a
v4 database, so rolling back after the upgrade needs the pre-upgrade backup (see
[Backup, restore, and upgrade](#backup-restore-and-upgrade)). The stopped-volume
`tar` backup includes the drawing tables, but `nest_manifest.py` and
`restore_check` do not verify drawings yet.

### Add from Drawing Library (desktop)

**Nest > Add from Drawing Library...** and the **Library** button above the
drawing list open a paged browser of active library drawings (Database mode
only; in File mode both are disabled with "Database mode only"). It starts
filtered to the nest's material, grade and thickness; **Show all** lifts that
filter. Selecting a drawing shows its notes. **Add to Nest** (or double-click /
Enter) downloads that exact revision, checks its SHA-256 against the record and
plans the insert (`OpenNest.Data.LibraryDrawingInsert`) before changing anything:

- Material, grade or thickness differing from the nest: a warning names each
  difference and the operator confirms. The drawing is added, but its cut pins
  will not apply; the nest's cutting settings do.
- Different units: refused (no conversion).
- The same revision from the same server already in the nest: only the
  quantity is raised. A different revision of that drawing: refused; change
  revisions through Update Drawings.
- Another drawing with the same name: refused, naming both.

The added drawing is an independent copy carrying a library link (server key,
id, revision and a snapshot of the library details) that is saved with the nest
(see [the nest file format](nest-file-format.md#drawing-library-links)). Editing it
in the nest never changes the library.

### Save to Drawing Library (desktop)

Right-click a drawing in the drawing list > **Save to Drawing Library...**
(Database mode only; disabled with "Database mode only" in File mode). The
dialog (`OpenNest.Data.DrawingLibrarySaveSession`) prefills name and customer
from the drawing (the nest's customer when the drawing has none), material,
grade, thickness and units from the nest (the drawing's material when the nest
has none), and the cut type and notes of the linked revision when the drawing
came from this server's library, otherwise the cut type last saved on this PC.
Units follow the nest and cannot be changed.

Whenever the details change, the dialog looks up active library drawings with
the same normalized customer, name, material and grade and the exact thickness,
units and cut type, and offers **Save as**:

- No match: a separate (new) library drawing.
- One match, or the drawing's own linked library drawing among several: the
  next revision of it, guarded by `If-Match` on the revision listed.
- Several matches: no default; the operator chooses one or a separate drawing.
- A linked drawing whose details no longer match is offered but never the
  default, so changing thickness cannot silently rewrite it.

Save stays disabled until the lookup for the current details has finished. A
409 (someone else saved first) is shown and the choices are refreshed. A save
that fails without a definite answer stays pending: the next Save first looks
for the revision it would have produced and adopts it when the package hash
and details match, so it is never stored twice and a retried new drawing keeps
its client-allocated id. Saving identical content at the current revision is a
no-op. On success only the nest drawing's library link changes, to the saved
revision; save the nest to keep it.

## Storage

`OpenNest.Server.NestDatabase` uses SQLite (`Microsoft.Data.Sqlite`) with
`journal_mode=WAL`. Each row holds the metadata columns plus the `.nest`
archive as a `BLOB`. The database file path comes from `--database=<path>`,
then `OPENNEST_DB`, defaulting to `./data/nests.db`. The server opens the
database at startup, so an unusable path fails startup rather than the first request.

Run **one service instance with its database on a local filesystem**. Multiple PCs
may use that instance, but do not share the SQLite file between replicas or place
it on SMB/NFS storage. A private reentrant lock serializes complete database
operations (including readers, write readbacks, health checks, and disposal);
HTTP upload/body reading happens outside that lock. `/healthz` executes `SELECT 1`
on the live connection; it is a connection check, not a backup, integrity scan,
or guarantee of future disk capacity.

Revision preconditions refuse stale edits; the database monitor is not a document
edit lock. After a conflict, explicitly reload the newer revision or Save As a
separate nest. After an ambiguous update failure, reload the current version before another save.
For an ambiguous first upload (including Save As or a server switch), OpenNest
keeps the client-generated pending ID and blocks another upload. The next Save
checks that exact ID on the original server without writing: if it exists with
the exact attempted archive and metadata, the document binds to its revision;
if it is absent, another explicit Save (or Save As for a pending copy) may
retry with the same ID. If it differs, reopen the server record to review it;
do not discard the pending identity or blindly create another record. Older
servers remain read-only for existing nests until upgraded.

## Server integration tests

```sh
dotnet test OpenNest.Server.Tests/OpenNest.Server.Tests.csproj
```

Cross-platform; runs in both CI workflows. Each test hosts the production routes in
memory (`WebApplicationFactory`) against its own temporary SQLite file and drives
them through the real `RemoteNestRepository` and `NestSaveSession`: create, list,
download, same-record update, copy, metadata-only update (archive and `fileSize`
unchanged), and delete. Missing/invalid upload parts must return 400 and unknown ids
404, both leaving every stored record and archive hash unchanged. Tests never open
`data/nests.db` or `OPENNEST_DB`; the temporary directory is removed when the host is
disposed. Concurrent HTTP clients and database-level readers/writers must retain
exact record membership, metadata, and archive bytes; a monitor-ownership test
checks every operation without relying on stress timing. Health tests check the
unchanged success response and a detail-free 503 after the database is disposed.
The [container smoke](#isolated-container-smoke) remains the image-level check.

## Running

Locally:

```sh
dotnet run --project OpenNest.Server/OpenNest.Server.csproj
```

Listens on `ASPNETCORE_URLS` (default Kestrel ports) unless overridden.

### Docker image

Docker must be built from the repository root: the server references
`OpenNest.Data`, which references `OpenNest.Core`. The Dockerfile copies all three
project files before restore and their source/resources before publish, including
Data's embedded `Defaults/CL-980.json`. The allowlist `.dockerignore` excludes
other projects, host `bin`/`obj`, local databases, environment files, and launch
settings. The server image uses the .NET 8 SDK to build and the .NET 8 ASP.NET
runtime to run; IO/Engine are used only by the host-side smoke tool, not the image.

```sh
docker build --pull -f OpenNest.Server/Dockerfile -t opennest-server:local .
# A release build stamps its version and exact source commit:
docker build --pull -f OpenNest.Server/Dockerfile --build-arg VERSION=X.Y.Z \
  --build-arg SOURCE_REVISION="$(git rev-parse HEAD)" -t opennest-server:X.Y.Z .
```

Without build arguments the image is labeled version `0.0.0-dev`, revision
`unknown`: a development build, never a release. The OCI labels are
`org.opencontainers.image.source`, `version`, `revision` and `base.name`.
`SDK_IMAGE`/`RUNTIME_IMAGE` build arguments accept digest-pinned base images;
for a release, record the base digests the build log resolved.

Runtime contract:

- Container port 8090 (`ASPNETCORE_URLS=http://+:8090`). Change the host port
  mapping, not the container port: the healthcheck probes 8090.
- Runs as the .NET base image's non-root `app` user (UID/GID 1654). Application
  files are root-owned and read-only to it; within `/app` only `/app/data` is
  writable, and a fresh named volume inherits that directory's ownership. The
  database stays `OPENNEST_DB=/app/data/nests.db`. The server also needs the
  container's writable `/tmp`: multipart uploads over 64 KiB are buffered there.
- `HEALTHCHECK` runs `curl --fail http://127.0.0.1:8090/healthz` (interval 30 s,
  timeout 5 s, start period 10 s, retries 3, and a 2 s start interval during the
  start period on Docker 25+). curl is in the image only for it.
- A read-only or unwritable data location fails startup: the process exits
  rather than serving requests it cannot store.
- Uploads: Kestrel's default 30,000,000-byte request limit covers the whole
  multipart request, so an archive must be slightly smaller (the smoke stores a
  29,000,000-byte one). A larger request gets 413 and nothing is stored. The
  desktop client streams without `Expect: 100-continue`, so it typically reports
  a closed connection instead of the 413 status. The save still fails.

Data written by a root-run image or a root-owned bind mount is not writable by
UID 1654, so startup fails. With the operator's approval, change ownership of
that data location only, once, for example
`docker run --rm --user 0 --entrypoint chown -v <volume>:/app/data <image> -R 1654:1654 /app/data`
(or `chown -R 1654:1654` on the bind-mounted directory itself, never its
parents). The image has no root entrypoint that changes ownership.

### Deploying with Compose

`compose.server.yaml` runs a published image (no local build) with
`no-new-privileges`, all capabilities dropped, and a named data volume. Copy it and
`OpenNest.Server/server.env.example` to a deployment directory, save the env file
under a local name such as `server.env`, and set:

- `OPENNEST_SERVER_IMAGE`: `ghcr.io/ajisaacs/opennest-server:X.Y.Z` or, preferably,
  `ghcr.io/ajisaacs/opennest-server@sha256:<verified-manifest-digest>` from the
  successful clean pulled-image verification job. Do not substitute Docker's
  store-dependent image `Id` or the config blob digest for this verified registry
  manifest digest; see [server image releases](releasing.md#server-image-separate-from-windows-candidates).
- `OPENNEST_BIND_ADDRESS`: `127.0.0.1` for testing on the host; this host's
  trusted LAN address for shop PCs. Do not use `0.0.0.0`. The bind address is only
  one layer: verify that the network/firewall admits only trusted clients.
- `OPENNEST_HOST_PORT` (default 8090) and `OPENNEST_DATA_VOLUME` (default `opennest-data`).

Publication requires an existing verified private package; first-package
initialization needs separate owner authorization outside the release workflow.
An authorized deployment operator must obtain a
`read:packages` token out of band and log in on the deployment host using
`docker login ghcr.io -u <github-user> --password-stdin`, piping the token from a
secret manager or a protected prompt (never a token literal in shell history).
Keep Docker's credentials protected on that host; do not put credentials in
`server.env`, Compose, the image, or source control. Login/pull success does not
authorize making the package public. Until an approved image has passed the
published-digest smoke, the reference below is only a template, not an available
verified image.

```sh
compose="docker compose --env-file server.env -f compose.server.yaml"
$compose pull || { printf 'STOP: image pull failed\n' >&2; exit 1; }
$compose up -d
$compose ps                                       # STATUS shows (healthy)
curl --fail http://<bind-address>:<port>/healthz  # {"status":"ok"}
```

In the desktop app choose **File > Storage Mode...**, Database, and enter the base
URL `http://<bind-address>:<port>`, without `/healthz` or `/api/nests`.
To set or change just the saved server address without changing File/Database mode,
use **Tools > Options > Nest server URL > Set / Change Server**. This saves the URL
immediately, independently of the Options Save button; it does not migrate nests
or enable Database mode. Canceling Options afterward does not undo that address.

### Backup, restore, and upgrade

SQLite runs in WAL mode, so copying `nests.db` from a running service is not a
backup. Back up during a maintenance period with ALL client writes disabled,
including metadata edits, delete, recover and restore, until capture finishes.
The HTTP manifest is not a transaction across requests; writes during capture
can invalidate it. Keep the entire stopped data directory, including any WAL/SHM
files, original image digest and deployment configuration. Copy
`scripts/Server.Tests/nest_manifest.py` from this source version to the deployment
directory as `nest_manifest.py` (Python 3 standard library only). It enumerates
bounded active AND deleted pages plus ALL revision pages, then streams every
current/historical archive through SHA-256. Canonical JSON includes complete
metadata, IDs, current pointers, visibility, timestamps, byte sizes and restore
source relationships. Missing pages, legacy endpoints, failed downloads,
inconsistent totals and invalid revision chains fail closed, not as empty history.
Protect manifests like the database: they contain names and comments.
Each step below is a Bash
function that checks every command and stops at the first failure, however it is
called; define them in the shell where `compose` is set:

```bash
url=http://<bind-address>:<port>

# Canonical complete manifest; stdout is emitted only after all reads succeed.
nest_manifest() (
  python3 ./nest_manifest.py "$1" || exit 1
)

# Records the expected manifest and metadata, then archives the stopped data directory.
# The service is restarted even if the archive fails.
backup_nests() (  # usage: backup_nests opennest-data-YYYY-MM-DD
  set -o pipefail
  fail() { echo "STOP: $*" >&2; exit 1; }
  for suffix in tar manifest metadata.json; do
    [[ ! -e "$1.$suffix" ]] || fail "$1.$suffix already exists"
  done
  nest_manifest "$url" > "$1.manifest" || fail "could not record the manifest"
  curl -fsS "$url/api/nests" > "$1.metadata.json" || fail "could not record the metadata"
  $compose stop || fail "could not stop the service"
  $compose run --rm --no-deps -T --entrypoint tar opennest-server -C /app/data -cf - . > "$1.tar"
  status=$?
  $compose start || fail "could not restart the service"
  curl -fs --retry 30 --retry-all-errors --retry-delay 1 "$url/healthz" > /dev/null \
    || fail "the restarted service is not healthy"
  [[ $status == 0 ]] || fail "the archive failed; $1.tar is incomplete"
  echo "Backed up all records and revisions to $1.tar"
)

backup_nests opennest-data-YYYY-MM-DD
```

Restore only into a volume created for that restore, never into an existing one
(least of all the active volume). `restore_check` refuses an existing volume,
extracts the backup into a new one, starts a temporary loopback-only container on
it, and requires complete active/deleted revision metadata and archive hashes to match. It
removes only the container it started; the new volume is kept either way:

```bash
restore_check() (  # usage: restore_check opennest-data-YYYY-MM-DD <image> [new-volume]
  set -o pipefail
  fail() { echo "STOP: $*" >&2; exit 1; }
  [[ -r "$1.tar" && -r "$1.manifest" && -r "$1.metadata.json" ]] || fail "backup is incomplete"
  restore=${3:-opennest-data-restore-$(date +%Y%m%d-%H%M%S)}
  existing=$(docker volume ls --format '{{.Name}}') || fail "could not inspect Docker volumes"
  while IFS= read -r volume; do
    [[ "$volume" != "$restore" ]] || fail "volume $restore already exists"
  done <<< "$existing"
  owner=$(python3 -c 'import secrets; print(secrets.token_hex(16))') || fail "could not create ownership token"
  docker volume create --label "com.opennest.restore.owner=$owner" "$restore" > /dev/null \
    || fail "could not create volume $restore"
  [[ $(docker volume inspect --format '{{index .Labels "com.opennest.restore.owner"}}' "$restore") == "$owner" ]] \
    || fail "restore volume ownership differs"
  echo "Restoring into new volume $restore"
  docker run --rm -i --network none --entrypoint tar -v "$restore:/app/data" "$2" \
    -C /app/data -xf - < "$1.tar" || fail "extraction into $restore failed"
  check=$(docker create --cap-drop ALL --security-opt no-new-privileges:true \
    -p 127.0.0.1::8090 -v "$restore:/app/data" "$2") \
    || fail "could not create the check container"
  trap 'docker rm -f "$check" > /dev/null' EXIT
  docker start "$check" > /dev/null || fail "the check container did not start"
  port=$(docker port "$check" 8090/tcp | cut -d: -f2) && [[ -n $port ]] || fail "no check port"
  curl -fs --retry 30 --retry-all-errors --retry-delay 1 "http://127.0.0.1:$port/healthz" > /dev/null \
    || fail "the restored service is not healthy"
  curl -fsS "http://127.0.0.1:$port/api/nests" | cmp - "$1.metadata.json" || fail "metadata differs"
  nest_manifest "http://127.0.0.1:$port" | diff - "$1.manifest" || fail "archives differ"
  echo "Verified $restore. Keep it isolated until the operator explicitly approves switching volumes."
)

restore_check opennest-data-YYYY-MM-DD <image>
```

Switch only after `restore_check` prints `Verified`. If it stops, inspect or remove
the new volume it named; the active volume is untouched.

To upgrade, take a stopped-service backup and keep a copy of the current
`server.env`. Prefer a digest in `OPENNEST_SERVER_IMAGE` so that file records
exactly what ran; for a tag, record
`docker image inspect --format '{{join .RepoDigests " "}}' <image>` first. Change
only `OPENNEST_SERVER_IMAGE` after migration/restore rehearsal on a disposable copy,
then run `$compose up -d` only with separate deployment authorization.
**Never run an old image against a migrated database**, even if its current-table
GETs still work: old writers can overwrite snapshots or physically delete rows
without updating history. Rollback requires a NEW volume restored from the verified
PRE-migration backup and the exact old image/configuration. Test its old current
metadata/byte manifest on that isolated copy before switching; the revision-aware
manifest deliberately refuses old endpoints. Archive the migrated volume unchanged
for recovery; reverting loses post-backup changes from the old server's view, so
reconcile them deliberately, never replay them automatically. A pre-version server
backup needs its own current-record metadata/byte oracle before migration; verify
those records become byte-identical revision 1 on the disposable upgraded copy.
Never run
`docker compose down -v`: it deletes the data volume.

#### Capacity and retention

Every revision currently stores a full archive BLOB, including metadata-only
and visibility revisions. Budget approximately the sum of ALL revision archive
sizes plus current-table archives, SQLite indexes/pages, WAL and backups; do not
estimate capacity from active current records alone. Measure volume/database and
WAL growth during the disposable rehearsal, reserve space for migration and a
separate verified restore copy, and monitor free space before rollout. There is
no automatic retention or purge; no revision deletion is authorized. Deduplication
requires a separately reviewed migration after measuring actual growth. An old
backup is not a substitute for retaining the migrated volume during rollback.

## Isolated container smoke

Prerequisites: a local Linux Docker daemon on the default Unix-socket context,
Bash, Python 3, curl, GNU `timeout`, `mktemp`, `tar`, `cmp`, and a .NET SDK able to build `net8.0` projects
(.NET 8 or newer). Restore needs NuGet access or cached packages. Run from the
repository root after building the image:

```sh
scripts/Test-ServerContainer.sh --image opennest-server:local
# Optional durable logs outside the default ignored .hermes/progress directory:
scripts/Test-ServerContainer.sh --image opennest-server:local --results /path/to/results
```

Manifest refusal tests run without Docker. The optional image variables also
exercise the Bash backup/restore blocks extracted verbatim from this page in a
unique disposable Compose project, including existing-file/volume refusal and
tampered-history rejection. The old image test restores ONLY a synthetic
pre-migration backup to another fresh volume and compares every old record's
metadata/bytes; it never mounts a migrated database into the old image.

```sh
python3 -m unittest discover -s scripts/Server.Tests -p test_revision_backup.py -v
OPENNEST_TEST_IMAGE=opennest-server:local \
OPENNEST_TEST_OLD_IMAGE=<local-pre-version-image> \
  python3 -m unittest discover -s scripts/Server.Tests -p test_revision_backup.py -v
```

Local smoke evidence does not imply registry publication or production deployment.

The wrapper accepts an image, not a URL or an existing volume/container. It uses
only the local default Docker context, creates uniquely labeled disposable
resources, and publishes to `127.0.0.1` on a Docker-allocated port. Every container
runs with the Compose example's `--cap-drop ALL` and `no-new-privileges`. It builds
the smoke tool once into a private temporary directory, honoring `TMPDIR`;
readiness, HTTP requests, and child commands have watchdogs.

Before starting a container it checks the image's numeric non-root `USER`, the
documented `HEALTHCHECK` command and timings (including the start interval), and
the OCI source/version/revision labels. A service is ready only when Docker reports the image's own healthcheck
`healthy` and the host receives `{"status":"ok"}`. Each service then checks that
the server process (PID 1) runs as the image user with no effective capabilities,
owns `nests.db`, and can write `/app/data` but not `/app` or the server DLL.

The test-only `scripts/Server.Tests/Server.Tests.csproj` console exercises the
existing .NET storage client; it is not included in the server image.

Before seeding, the wrapper restores two synthetic legacy-schema rows into its
fresh volume, verifies byte-identical revision-1 migration against a precomputed
complete metadata/hash oracle, then checks repeated startup preserves history.
The synthetic fixture is generated in code: one plate and one rectangular part,
with no customer files. `seed` uses the actual `RemoteNestRepository` and
`NestSaveSession` to create, update the same ID, and save a copy with a new ID.
It also creates metadata-only, historical-restore, delete/recover/delete revisions,
leaving a six-revision hidden record with a restore-source relationship.
It checks read-back metadata, session binding, exact list membership, raw
camelCase/string-enum JSON, identical downloaded bytes, and `NestReader` geometry
and counts. `reject` snapshots every record's metadata and archive hash, then
checks 400 responses for missing/invalid/null metadata and missing/empty files on
both upload and file-update routes; unknown metadata/file IDs must return 404.
Every rejected operation must leave the snapshot unchanged.
`limits` stores, downloads byte-for-byte, and soft-deletes a 29,000,000-byte synthetic
archive (buffered to disk by the non-root runtime), then requires 413 for a
request over 30,000,000 bytes sent with `Expect: 100-continue`, and a reported
failure from the real client's oversized upload and file update. The snapshot
must be unchanged after each.

The persistence stage stops and removes the first container while retaining its
fresh named volume, starts a replacement on another allocated loopback port, and runs
`verify` against saved IDs, **all** metadata (including server-assigned `savedAt`),
archive hashes, and exact list membership. It then follows the documented
stopped-service backup: `tar` of the whole data directory through the image,
restored into a second owned volume whose service must pass the same `verify`.
The Python manifest is captured after all mutations and compared exactly after
restart and volume restore, with page size 1 to exercise every pagination boundary.
It covers active and deleted current records and ALL revisions, not just the
ordinary `/api/nests` enumeration; the large soft-deleted archive remains included.
The original volume mounted read-only, and a root-owned `tmpfs`, must each make
the server exit nonzero at startup with a SQLite error and never report healthy.

A missing/invalid state file or failed assertion exits nonzero. Direct tool use is
test-only: it accepts plain loopback URLs, and `seed` refuses a nonempty server
unless `--test-allow-nonempty` is explicitly supplied for an owned disposable
target. Prefer the wrapper; loopback alone is not proof that an existing server is
disposable.

Each invocation writes a fresh `run.*` results directory containing build, smoke,
container, image, runtime, ownership, and cleanup logs. On failure those logs remain
for diagnosis. The exit trap removes only invocation-owned containers and named
volumes; transient state, the backup archive, synthetic databases, and build
artifacts are removed on success or failure. It never prunes Docker or alters
preexisting services/volumes.
Cleanup requires successful empty Docker inventory readback, including for an
already-removed container. Unresolved ownership, lookup, removal, or readback
errors make the wrapper fail without deleting unproven resources; inspect
`cleanup.log` and `outcome.log` for diagnostics. An original smoke failure is
preserved even if cleanup also fails.

The focused cleanup regression needs only Python 3 and Bash:

```sh
python3 -m unittest discover -s scripts -p test_server_container_cleanup.py -v
```

It executes the wrapper's actual cleanup functions with explicitly mocked Docker
transport, including timeouts and missing cidfiles; it does not replace the real
container smoke or contact a daemon.
