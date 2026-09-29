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

Docker (built from the repository root so it can see `OpenNest.Data`):

```sh
docker build -f OpenNest.Server/Dockerfile -t opennest-server .
docker run -d -p 8090:8090 -v opennest-data:/app/data opennest-server
```

The image listens on `:8090` and stores `nests.db` under `/app/data`, which
should be a named volume or bind mount so nests survive container recreation.

Point the desktop app's **File > Storage Mode...** server URL at
`http://<host>:8090` (no trailing slash required; `RemoteNestRepository`
trims it).
