# Shared settings

In Database mode ([nest storage](nest-storage.md)) the nest server holds one copy of the
settings that decide what a nest gets and what a post writes, so every workstation in the
building nests and posts the same way:

| Kind | Key | What it holds | This PC's own copy |
|---|---|---|---|
| `machine` | machine id | A Tools > Machine Configuration entry: materials, grades, thicknesses, leads, kerf, cut-offs, motion limits, plate sizes, support grid | `%APPDATA%\OpenNest\Machines\<id>.json` |
| `post` | post type name, e.g. `OpenNest.Posts.CincinnatiCIFiber.CIFiberPostProcessor` | A post's settings, as its JSON file holds them | `<post>.json` beside the post DLL in `Posts\` |
| `clamps` | `default` | Machine clamp defaults (Save as Machine Defaults) | `%APPDATA%\OpenNest\clamps.json` |
| `nest-defaults` | `default` | Tools > Nest Defaults: units, sheet size, quadrant, spacing, default machine | `%APPDATA%\OpenNest\defaults.json` |

File mode, the console, MCP and API front ends use only this PC's copies.

## Which copy is used

Each kind is read from the server once the server holds it, and from this PC's copy until
then: machines as a whole (the server's machines replace this PC's as soon as it holds
one), posts per post, clamps and nest defaults as single documents. Saves go where the
settings were read, so editing a kind the server does not hold yet changes only this PC.

When the desktop opens in Database mode and the server lacks any of this PC's machines (all
of them, when it holds none), post settings, clamp settings or nest defaults, it lists them
and offers to upload them. The first workstation to answer Yes seeds the server; a document
the server already holds is never replaced, so later workstations only add what is missing
(a post installed on just one PC, for example). Declining keeps that PC on its own copies and
asks again at the next start.

## Freshness, offline and conflicts

The desktop keeps the last copy it read in `%APPDATA%\OpenNest\SharedSettings\<server>.json`
and re-reads the server when that copy is more than two minutes old, every time it posts
and when Tools > Machine Configuration opens. A request gives up after five seconds.

While the server cannot be reached, the last copy read is used (this PC's own settings when
none has been read yet), the user is told once per session, and nothing shared can be
changed: saves fail with the reason. The desktop retries the server at most once a minute.

Every document carries a revision. A save sends the revision it read; when someone else
saved first the server refuses it and the desktop takes their copy:

- Post settings: the post stops, the other workstation's settings are loaded, and posting
  again shows them. Posting without changing the settings dialog never saves a revision,
  even though the Cincinnati post records the material library the nest selected.
- Machine Configuration: the unsaved machines stay pending; reopen the window to see the
  other changes.
- Clamps, nest defaults, support grid: the save reports the conflict.

The window title of Tools > Machine Configuration says whether it edits the shared machines,
this PC's (not shared yet), or a read-only copy.

## Server

`/api/settings` (schema v13) stores each document's current revision in `shared_settings`
and every revision, deletions included, in `shared_setting_revisions`, with who saved it
(the Windows user name) and when. The history has no endpoint; read it from the database to
trace or recover a change.

| Method | Path | Body | Result |
|---|---|---|---|
| GET | `/api/settings` | — | Every current document: `[{ "kind", "key", "revision", "json", "savedAt", "savedBy" }]` |
| GET | `/api/settings/{kind}/{key}` | — | The document, or 404 |
| POST | `/api/settings/{kind}/{key}` | `{ "json", "savedBy" }` | 201 created; 200 when the same JSON is stored; 409 with the stored document otherwise |
| PUT | `/api/settings/{kind}/{key}` | `{ "json", "savedBy" }`, `If-Match: "<revision>"` | The document (same JSON is a no-op); 404 when not stored; 409 with the current document when stale |
| DELETE | `/api/settings/{kind}/{key}?savedBy=` | `If-Match: "<revision>"` | 204; 404 when not stored; 409 when stale |

The server checks the key against the kind (a machine id, a post type name of letters,
digits and `._-+`, or `default`), that `json` is a JSON object of at most 512 K characters,
and that a machine's JSON is a machine configuration with the key's id. It cannot check a
post's settings beyond that; the desktop only sends what the post serialized. Recreating a
deleted document continues its revision count. Kinds a client does not know are ignored, so
a newer server can share more.

A post takes part by implementing `IJsonConfigPostProcessor` (`SerializeConfig`,
`LoadConfig`); a configurable post without it keeps using only its own file.
