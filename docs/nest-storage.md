# Nest storage: File mode vs. Database mode

The desktop app can save nests two ways:

- **File mode** (default, unchanged behavior): Save/Save As write a `.nest` ZIP
  archive to disk via the normal file dialog. See [nest-file-format.md](nest-file-format.md).
- **Database mode**: Open lists server records with filterable metadata and
  sortable columns; Save creates a record on first save and updates that same
  record afterward. Save As creates a new record (a copy). A failed save keeps
  the previous record association; switching server URLs creates a record on the
  new server rather than updating an id from the previous one. Delete in the
  saved-nest list permanently removes the selected server record after confirmation.
  A separate **File > Export .nest...** command is always available (in both
  modes) for producing a local file to share or back up. Export does not change
  the document's file save path or database record association.

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

## Endpoints

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/healthz` | — | `{ "status": "ok" }` |
| GET | `/api/nests` | — | `NestRecord[]`, newest `savedAt` first |
| GET | `/api/nests/{id}` | — | `NestRecord` or 404 |
| GET | `/api/nests/{id}/file` | — | `.nest` archive bytes (`application/zip`) or 404 |
| POST | `/api/nests` | multipart: `metadata` (JSON `NestRecord`) + `file` (`.nest` bytes) | `NestRecord` with server-assigned `id` when the client sends an empty guid |
| PUT | `/api/nests/{id}/file` | multipart: `metadata` + `file` | Updated `NestRecord`, or 404 if `id` is unknown |
| PUT | `/api/nests/{id}/metadata` | JSON `NestRecord` | Updated `NestRecord` (archive untouched), or 404 |
| DELETE | `/api/nests/{id}` | — | 204, or 404 |

There is no authentication; this is a LAN-only service. Do not expose it
outside the shop network without adding one.

## Storage

`OpenNest.Server.NestDatabase` uses SQLite (`Microsoft.Data.Sqlite`) with
`journal_mode=WAL`. Each row holds the metadata columns plus the `.nest`
archive as a `BLOB`. The database file path comes from `--database=<path>`,
then `OPENNEST_DB`, defaulting to `./data/nests.db`.

## Running

Locally:

```sh
dotnet run --project OpenNest.Server/OpenNest.Server.csproj
```

Listens on `ASPNETCORE_URLS` (default Kestrel ports) unless overridden.

Docker must be built from the repository root: the server references
`OpenNest.Data`, which references `OpenNest.Core`. The Dockerfile copies all three
project files before restore and their source/resources before publish, including
Data's embedded `Defaults/CL-980.json`. The allowlist `.dockerignore` excludes
other projects, host `bin`/`obj`, local databases, environment files, and launch
settings. The server image uses the .NET 8 SDK to build and the .NET 8 ASP.NET
runtime to run; IO/Engine are used only by the host-side smoke tool, not the image.

```sh
docker build --pull -f OpenNest.Server/Dockerfile -t opennest-server:local .
```

The image listens on `:8090` and keeps `OPENNEST_DB=/app/data/nests.db`. Mount
`/app/data` to preserve the SQLite database and archive blobs across recreation.
For an intentional local run (not needed for the isolated tests below):

```sh
docker run -d -p 127.0.0.1:8090:8090 -v opennest-data:/app/data opennest-server:local
```

This example binds only to loopback. Remote shop clients require an explicitly
chosen trusted-interface binding and network access controls; do not casually
replace it with an all-interface publish. The service has no authentication.
These build/smoke instructions do not establish release readiness, non-root
hardening, or a production deployment.

Point the desktop app's **File > Storage Mode...** server URL at
`http://<trusted-host>:8090` (or `http://127.0.0.1:8090` for local use).

## Isolated container smoke

Prerequisites: a local Linux Docker daemon on the default Unix-socket context,
Bash, curl, GNU `timeout`, `mktemp`, and a .NET SDK able to build `net8.0` projects
(.NET 8 or newer). Restore needs NuGet access or cached packages. Run from the
repository root after building the image:

```sh
scripts/Test-ServerContainer.sh --image opennest-server:local
# Optional durable logs outside the default ignored .hermes/progress directory:
scripts/Test-ServerContainer.sh --image opennest-server:local --results /path/to/results
```

The wrapper accepts an image, not a URL or an existing volume/container. It uses
only the local default Docker context, creates uniquely labeled disposable
resources, and publishes to `127.0.0.1` on a Docker-allocated port. It builds the
smoke tool once into a private temporary directory, honoring `TMPDIR`; readiness,
HTTP requests, and child commands have watchdogs.

The test-only `scripts/Server.Tests/Server.Tests.csproj` console exercises the
existing .NET storage client; it is not included in the server image.

The synthetic fixture is generated in code: one plate and one rectangular part,
with no customer files. `seed` uses the actual `RemoteNestRepository` and
`NestSaveSession` to create, update the same ID, and save a copy with a new ID.
It checks read-back metadata, session binding, exact list membership, raw
camelCase/string-enum JSON, identical downloaded bytes, and `NestReader` geometry
and counts. `reject` snapshots every record's metadata and archive hash, then
checks 400 responses for missing/invalid/null metadata and missing/empty files on
both upload and file-update routes; unknown metadata/file IDs must return 404.
Every rejected operation must leave the snapshot unchanged.

The persistence stage stops and removes the first container while retaining its
named volume, starts a replacement on another allocated loopback port, and runs
`verify` against saved IDs, **all** metadata (including server-assigned `savedAt`),
archive hashes, and exact list membership. A missing/invalid state file or failed
assertion exits nonzero. Direct tool use is test-only: it accepts plain loopback
URLs, and `seed` refuses a nonempty server unless `--test-allow-nonempty` is
explicitly supplied for an owned disposable target. Prefer the wrapper; loopback
alone is not proof that an existing server is disposable.

Each invocation writes a fresh `run.*` results directory containing build, smoke,
container, ownership, and cleanup logs. On failure those logs remain for diagnosis.
The exit trap removes only invocation-owned containers and the named volume;
transient state, synthetic databases, and build artifacts are removed on success
or failure. It never prunes Docker or alters preexisting services/volumes.
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
