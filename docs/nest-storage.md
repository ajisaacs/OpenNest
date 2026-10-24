# Nest storage: File mode vs. Database mode

The desktop app can save nests two ways:

- **File mode** (default, unchanged behavior): Save/Save As write a `.nest` ZIP
  archive to disk via the normal file dialog. See [nest-file-format.md](nest-file-format.md).
- **Database mode**: Open browses server records 100 at a time. The Find box
  searches name, customer, material, grade, made by, comments and status on the server
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
  row's right-click menu) to edit the same information as **Nest > Nest Info**,
  including the name, without opening the nest in the main editor. **OK** saves immediately to the
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
`OpenNest.Data`'s local JSON stores. Uploaded metadata (POST and both PUTs) must
carry string text fields (`null` is 400) and a defined `status`: a name, or for
existing callers its integer value (0 quote, 1 toBeCut, 2 hasBeenCut, 3 draft); an
undefined value is 400 and stores nothing. A server older than `draft` refuses it,
so upgrade the server before desktops that can set it.
An uploaded `file` (POST and PUT `/file`) must be a `.nest` archive with a readable
`nest.json`, or the request is 400 and stores nothing. The server reads only that
entry, never programs or geometry, and takes the archive-derived fields below from it.

| Field | Type | Notes |
|---|---|---|
| `id` | guid | Client-generated on first save; kept on updates. |
| `name` | string | |
| `customer` | string | |
| `dateCreated` / `dateModified` | datetime | Set by the client from the nest's own metadata. |
| `material` | string | Material name (type), e.g. `Stainless`. Read from the uploaded archive (nest material, else plate defaults' on an explicit null); a metadata-only PUT may change it, and the desktop applies the record's value when opening the nest. |
| `materialGrade` | string | Optional; default blank. Older servers drop it. A blank grade on a record saved before schema v8 is treated as unrecorded: opening keeps the archive's grade while the material name matches. |
| `thickness` | number | Read from the uploaded archive (nest thickness, else plate defaults' when not positive); editable like `material`. |
| `status` | `"draft"` \| `"quote"` \| `"toBeCut"` \| `"hasBeenCut"` | Default `quote`. |
| `plateCount` / `partCount` | integer | Server-computed from the uploaded archive (non-cutoff parts only; a trailing plate with no parts or cut-offs is not counted); ignored on upload and kept by a metadata-only PUT. |
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
| GET | `/api/nests/query?search=&status=&material=&materialGrade=&thickness=&sort=&order=&offset=&limit=` | — | `{ "items": NestRecord[], "total", "offset", "limit" }`: one bounded page filtered in SQL (see below), or 400 for an invalid parameter |
| GET | `/api/nests/{id}` | — | `NestRecord` or 404 |
| GET | `/api/nests/{id}/file` | — | `.nest` archive bytes (`application/zip`) or 404 |
| GET | `/api/nests/{id}/sheets` | — | `{ "nestId", "revision", "sheets": [{ "width", "length", "quantity" }], "parts": [{ "name", "required", "nested" }] }` read from the current archive (see below), 404, or 500 problem when the stored archive is unreadable |
| POST | `/api/nests` | multipart: `metadata` (JSON `NestRecord`) + `file` (`.nest` bytes) | `NestRecord` with server-assigned `id` when the client sends an empty guid; 409 with the current record (active or deleted) when the `id` already exists, storing nothing |
| PUT | `/api/nests/{id}/file` | multipart: `metadata` + `file` | Updated `NestRecord`, or 404 if `id` is unknown |
| PUT | `/api/nests/{id}/metadata` | JSON `NestRecord` | Updated `NestRecord` (archive untouched), or 404 |
| DELETE | `/api/nests/{id}` | — | Updated hidden `NestRecord`, or 404 |
| GET | `/api/nests/{id}/revisions?offset=0&limit=100` | — | Metadata page, newest first; limit 1–200 |
| GET | `/api/nests/{id}/revisions/{revision}/file` | — | Exact immutable archive bytes |
| POST | `/api/nests/{id}/revisions/{revision}/restore` | — | New current revision of the same ID, with source metadata/bytes |
| GET | `/api/nests/deleted/query` | — | Bounded deleted-record page; same query parameters as ordinary browsing |
| GET | `/api/nests/deleted/{id}` / `/{id}/file` / `/{id}/revisions` / `/{id}/revisions/{revision}/file` | — | Recovery equivalents of current/history reads |
| POST | `/api/nests/deleted/{id}/recover` | — | New nondeleted revision of the same ID |
| POST | `/api/nest-names/next?year=` | — | `{ "year", "number", "name" }`: the year's next generated nest number (see below); 400 outside 2000–2099 |

All existing-record mutations require `If-Match: "<current revision>"`.
Missing precondition returns 428 (upgrade/reload), malformed precondition 400,
stale precondition 409 with current metadata; none mutate data. Ordinary reads
of hidden records return 404. Changed file/metadata saves and visibility changes
append snapshots atomically; an identical save at the current revision is a no-op.
Restore appends a new snapshot, never rewrites its source or intervening history.

### Sheets and part counts

`/sheets` lists what a nest needs cut, for inventory holds (MaterialLedger). It
skips the trailing empty new-plate slot, as `plateCount` does. Sheets group by
size with `width` the shorter side, sizes as stored (unrounded), `quantity` the
sum of those plates' quantities, ordered by width then length. `parts` has one
entry per drawing, ordered by name: `required` is the drawing's required
quantity, `nested` its parts on each plate times that plate's quantity; parts
whose drawing is missing are skipped.

### Generated nest names

**File > New** names a nest `N<yy>-<seq>` (`OpenNest.Data.NestNames`): the
two-digit year and the year's sequence number in a base-30 alphabet without
look-alike characters, padded to three digits (27,000 a year before a fourth
appears). The sequence runs for the whole year. In Database mode the desktop
asks `POST /api/nest-names/next` for the number, so every PC shares one
counter per year (`nest_sequences`, schema version 6). The server also keeps
the number above every stored generated name for that year, active or
deleted, so names created offline or before the server counted are not
reissued; renamed nests and other years' names do not move it.

When the server is unreachable within 3 seconds, has no such endpoint, or the
app is in File mode, the PC's own counter (`NestNumber` user setting) names
the nest. It resets when the year changes and is raised past each number the
server hands out. Two PCs naming nests offline at the same time can still pick
the same name; a nest's identity on the server is its `id`, never its name.

A nest's name may already be on posted CNC programs or labels, so it is never
renamed silently. When **Save** (not Save As, and not the reconciliation of a
pending upload) creates a nest's first record on the current server, it first
searches active nests for one with exactly the same name (case-sensitive;
deleted nests do not count, `NestNameConflicts`). Only on a match does it
allocate the year's next name and show it in an editable dialog: OK uploads
under the chosen name, Cancel saves nothing. If the check or the allocation
fails or takes longer than 3 seconds, the nest uploads under its current name.

In Database mode the name can be edited in **Nest > Nest Info** (it uploads with
the next Save) and in the saved-nest browser's **Nest Information...** (saved
immediately). In File mode it stays read-only, since the file name is the nest
name. If another active nest already has the exact new name, a warning asks
before using it; **No** reopens the dialog with the edits kept. A name accepted
this way is not asked about again by the nest's first Save. A rename that the
server cannot check within 3 seconds goes ahead without a warning.

There is no authentication; this is a LAN-only service. Do not expose it
outside the shop network without adding one.

### Browsing query

`GET /api/nests/query` filters, orders and pages in SQLite, so a client receives
only the requested page of metadata (never archive bytes):

- `search` (optional): trimmed, at most 200 characters, no NUL characters;
  blank means no filter.
  Case-insensitive substring of the whole text in `name`, `customer`, `material`,
  `materialGrade`, `madeBy`, `comments` or the status (`ToBeCut` or the display name `To Be Cut`).
  `%`, `_` and `\` are literal. Dates and numbers are not matched as text.
  Case folding is ASCII-only (SQLite `LIKE`).
- Exact filters (optional, all must match; used to find draft nests a release
  can merge into): `status` (`draft`, `quote`, `toBeCut` or `hasBeenCut`),
  `material` and `materialGrade` (compared by `SharedListNames.Key`, so case and
  spacing are ignored; a present but blank `materialGrade` asks for nests with no
  grade) and `thickness` (a number, zero or greater, matched at four decimals).
  The client sends them only when set, so plain browsing still works against
  servers without them.
- `offset` (default 0, at least 0) and `limit` (default 100, 1 to 500).
- `sort` (default `savedAt`): one of `savedAt`, `name`, `customer`, `status`,
  `material`, `dateCreated`, `dateModified`, `thickness`, `plateCount`,
  `partCount`, `madeBy`, `comments`, `fileSize`, `materialGrade` (names, case-insensitive; no
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

The server also keeps a shared library of drawings (schema v5). The desktop
adds library drawings to a nest and saves nest drawings to the library (below).
A drawing's geometry travels as an opaque
**drawing package**: a `.nest` archive holding exactly one drawing and no plates,
written and read on the client by `OpenNest.IO.DrawingPackage`. The server never
parses it.

`OpenNest.Data.DrawingLibraryRecord` fields: `id` (client-allocated before the
first upload), `revision`, `name`, `customer`, `materialName`, `materialGrade`,
`thickness` (stored rounded to 4 decimals), `units` (`inches` | `millimeters`),
`cutType` (`laser` | `waterjet` | `plasma`), `notes`, `settings`, `archived`,
`savedBy`, and the server-computed `packageSha256` (lower-case hex), `packageSize`
and `savedAt`.

`settings` holds the drawing's nesting settings, applied when it is added to a
nest: `cutFirst` (sequence its parts first, for example test squares), `priority`,
`stepAngle`, `startAngle`, `endAngle` (radians), `allow180Equivalent` and
`colorArgb` (32-bit ARGB; alpha 0 means no stored color, so the nest assigns one).
They are record fields, not package content (see
[drawing packages](nest-file-format.md#drawing-packages)), so changing them adds a
revision without new geometry. Default quantity is not stored: quantity belongs to
each nest.

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/drawings` | multipart: `metadata` (JSON record with a nonempty `id`) + `package` | Revision 1; 409 with the current record when the id exists |
| GET | `/api/drawings/query?search=&customer=&materialName=&materialGrade=&thickness=&cutType=&cutFirst=&includeArchived=&sort=&order=&offset=&limit=` | — | `{ items, total, offset, limit }` |
| GET | `/api/drawings/{id}` / `/{id}/file` | — | Current record / package bytes (archived drawings included), or 404 |
| GET | `/api/drawings/{id}/revisions?offset=0&limit=100` | — | Revision page, newest first; limit 1–200 |
| GET | `/api/drawings/{id}/revisions/{revision}/file` | — | Exact immutable package bytes |
| PUT | `/api/drawings/{id}/file` | multipart: `metadata` + `package` | New revision |
| PUT | `/api/drawings/{id}/metadata` | JSON record (all details and `settings`; an omitted `settings` means defaults) | New revision with the same package |
| POST | `/api/drawings/{id}/archive` / `/restore` | — | New revision with `archived` changed |

- Existing-drawing writes require `If-Match` exactly as for nests: 428 missing,
  400 malformed, 409 stale with the current record. Writing to an archived
  drawing, archiving an archived one or restoring an active one is also 409.
  None of these change data. There is no delete; archive hides a drawing from
  the query unless `includeArchived=true`.
- Every change appends an immutable revision in the same transaction that
  updates the current row, including metadata-only edits and archive/restore.
  An identical save (details, settings and package hash) at the current
  revision is a no-op.
- Packages are content-addressed: each distinct package is stored once, keyed by
  its SHA-256, and revisions point at it. Only a new package adds a blob;
  details, settings, archive and restore revisions add none.
- Validation (400, no state change): `cutType` and `units` must be single enum
  names (no numbers or combinations); `thickness` finite and at least 0; `name`
  nonblank; name, customer, material, grade and savedBy at most 200 characters,
  notes at most 4000; no NUL characters; `settings` not null, angles finite
  and `stepAngle` at least 0; package nonempty, at most 16 MiB and
  starting with a ZIP signature. Metadata `id` on a PUT must be empty or match
  the route.
- Query: `search` is a literal, case-insensitive (ASCII) substring of name,
  customer or notes (`%`, `_`, `\` literal). `customer`, `materialName` and
  `materialGrade` match exactly after `SharedListNames.Key` normalization
  (trimmed, collapsed spaces, case-insensitive); blank means any. `thickness`
  matches exactly after rounding to 4 decimals. `sort` is one of `savedAt`
  (default), `name`, `customer`, `materialName`, `materialGrade`, `thickness`,
  `cutType`, `revision`; ties break by `id`. `cutFirst=true|false` keeps only
  drawings with that setting. Paging, `order` and the refusal of
  unknown, repeated or out-of-range parameters follow the nest query.
- An older server answers 404 on these routes; the client reports that the
  server must be updated and never falls back to another store.

Schema v5 keeps three drawing tables in the same file: `drawing_packages`
(`sha256`, `size`, `package`), `drawing_revisions` (every immutable revision's
details, settings, archived flag and package hash) and `drawings` (each
drawing's current revision, indexed for browsing). Upgrading from v4 runs in
one transaction: each distinct v4 blob is stored once by its hash, every
revision and current row is copied as it was with default nesting settings
(v4 had none), and the v4 drawing tables are dropped. A pre-v4 database gets
the v5 tables directly. The nest tables are untouched. A server older than v5 refuses
to open a v5 database, so rolling back after the upgrade needs the pre-upgrade backup (see
[Backup, restore, and upgrade](#backup-restore-and-upgrade)). The stopped-volume
`tar` backup includes the drawing tables, but `nest_manifest.py` and
`restore_check` do not verify drawings yet.

### Add from Drawing Library (desktop)

**Nest > Add from Drawing Library...** and the **Library** button above the
drawing list open a paged browser of active library drawings (Database mode
only; in File mode both are disabled with "Database mode only"). It starts
filtered to the nest's material, grade and thickness; **Show all materials**
lifts that filter. Selecting a drawing fills the details pane beside the list:
its details, notes and a preview of its geometry. The preview downloads the
listed revision's package shortly after the selection settles (arrowing through
the list does not download each row), caches it per revision while the dialog is
open, and is best-effort: a failed download shows "Preview unavailable" and
never blocks adding. **Add to Nest** (or double-click /
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
in the nest never changes the library. The copy takes its name, customer,
material and nesting settings from the library record, not from the package.

The **In This Nest** tab (its title counts them) lists the nest's drawings,
library or not, cutoffs excluded: name, library revision ("Not from the library"
or "(other server)" when it is linked elsewhere), and the Required, Nested and
Remaining quantities, with totals below. It refreshes after every add. Required
is edited in place (0 to 99,999; one click starts editing, a bad number keeps
the editor open with an error) and applies to the nest at once, like an add;
the main window's drawing list refreshes when the dialog closes.

The browser's **Cut first** column shows the setting.

**File > Drawing Library...** (Database mode only, like Drawing Sets) opens the
same browser without a nest: it lists every active drawing, has no Add to Nest
or In This Nest tab, and a double-click or Enter edits the selected drawing. Its
right-click menu has Edit Drawing and the set and release items below.

### Edit Drawing (desktop)

**Edit Drawing...** (`OpenNest.Data.DrawingLibraryEditSession`) edits one active
library drawing. Its **Details** are the name, customer, material, grade,
thickness and cut type; **Units** are shown read-only, since changing them would
change what the geometry means. Its **Nesting settings** are Cut first, priority,
rotation step/start/end (degrees), 180° equivalent and color, then the notes.
Text is trimmed and thickness rounded as the server stores it; Save stays
disabled until something changed and every value passes the library rules.

When the new details would give the drawing another identity (D4: normalized
customer, name, material and grade, exact thickness, units and cut type), Save
first looks up active library drawings that already have them and, if any do,
names them and asks before saving; a failed lookup also asks. Editing only notes
or settings never asks.

Save goes through `PUT /api/drawings/{id}/metadata`, guarded by the revision
the dialog opened with: a new revision with the same package. A material or
thickness change is an edit of the same drawing, not a new one. A 409 (someone
else saved first) is shown, nothing is saved and the dialog stays open; close it
and edit again. Nests that already hold the drawing keep their copy and
revision. Sets list drawings by id, so they release the new revision next time.

Edit Drawing opens from:

- the Drawing Library dialog (the **Edit Drawing...** button or the drawing's
  right-click menu), which reloads the page after a save;
- the set editor's member grid: double-click a member (outside its quantity),
  right-click it, or **Edit Drawing...** beside the grid. It first reads the
  member's current record, so a set read earlier does not cause a conflict; the
  row then shows what was saved. The set itself is unchanged and needs no save;
- the release dialog: a group refused for its drawings (no material or
  thickness, or two drawings with one name) has **Fix Drawings...**, which opens
  Edit Drawing on each drawing it is refused for in turn (Cancel stops), as does
  a double-click on the group; with a releasable group selected, it takes the
  first refused one. Its right-click menu lists Edit Drawing for each of
  the group's drawings. Each edit reads the current record first, and the plan
  is rebuilt with what was saved, so a fixed drawing joins its material group.

### Save to Drawing Library (desktop)

Right-click a drawing in the drawing list > **Save to Drawing Library...**
(Database mode only; disabled with "Database mode only" in File mode). The
dialog (`OpenNest.Data.DrawingLibrarySaveSession`) prefills name and customer
from the drawing (the nest's customer when the drawing has none), material,
grade, thickness and units from the nest (the drawing's material when the nest
has none), and the cut type and notes of the linked revision when the drawing
came from this server's library, otherwise the cut type last saved on this PC.
Units follow the nest and cannot be changed. The drawing's own nesting
settings (Cut first, priority, rotation, color) are saved in the record and
summarized read-only in the dialog; change them later with Edit Drawing.

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

### Drawing sets (desktop)

**File > Drawing Sets...** (Database mode only) opens the drawing sets window;
it needs no open nest, since releasing a set makes new nests or merges into
draft ones and opens each in its own tab. It lists sets a page at a time
(`OpenNest.Data.DrawingSetBrowseSession`): search by name, customer or notes,
sort by name, customer, revision or saved time, and **Show archived**. Selecting
a set shows its notes.

- **New Set...** and **Edit...** open the set editor
  (`OpenNest.Data.DrawingSetEditSession`): name, customer, notes and the member
  grid (drawing, material, grade, thickness, units, cut type, quantity, note).
  The editor's left pane browses the library in place, with no second dialog:
  every active drawing (never filtered to a nest), searched, sorted and paged
  like the Drawing Library dialog. Select one or several and choose **Add to Set**,
  double-click or press Enter to add them at the entered **Qty per set**.
  Drawings already in the set are greyed; one picked again is skipped and
  named, so there is one member per drawing. Quantities are edited
  in the grid (1 to 100,000); **Remove**, **Move Up** and **Move Down** change
  the list. The note column flags drawings archived in the library (skipped on
  release unless included) and drawings with no material or thickness (cannot be
  released until set; double-click to open
  [Edit Drawing](#edit-drawing-desktop)).
- **Save** adds a revision guarded by the revision the set was read at; a 409 is
  shown and the operator cancels and reloads. A new set's id is allocated
  before its first save, and a save that fails without a definite answer stays
  pending: the next Save reads that id and adopts the stored set (saving any
  later edit as its next revision) instead of creating it twice. Closing with
  unsaved edits asks first.
- **Archive** (after confirming) and **Restore** add a revision; nothing is
  deleted. **History...** lists revisions newest first with the members of the
  selected one, shown with their drawings' current library details.

In the Drawing Library dialog, right-click a drawing for **Used in Sets...** (the sets
whose current revision contains it) or **Add to Set...**: choose an active set
or **New Set**, and the set editor opens with the drawing added at the entered
quantity for review and Save. **Releases...** lists the releases that had the
drawing on its own (see below), with Undo Release. When the dialog was opened
from a nest, **Make Nests...** reads the drawing's current record and opens the
release dialog with it on its own at the entered quantity; more drawings and
sets are added there.

The nest's drawing list has **Add Library Drawings to Set...** (right-click any
drawing; it acts on the whole nest, `OpenNest.Data.NestSetMerge`). It takes each
drawing copied from this server's library once, in list order, with its required
quantity (copies of one library drawing are summed). Drawings never saved to the
library, linked to another server or with no required quantity are skipped and
named; cutoffs are left out. If none remain it says so before anything else.
Otherwise choose an active set or **New Set** (named after the nest, with its
customer): the set's current library records are read, drawings the set already
lists **keep the set's quantity** (a released nest carries its multiplier, so
copying its quantities back would multiply them again), and members are added at the
library's latest revision. Anything kept, skipped, archived in the library or
newer there than in the nest is listed first, then the set editor opens for
review and Save; nothing is stored before then.

**Make Nests...** (in the drawing sets window) reads the
selected active set with its members and opens the release dialog with it listed.
More sets are added from **Add a set**: the list opens with the first active sets
by name and narrows as you type; Enter or **Add** takes the picked set, or the one
set whose name is exactly what was typed. **Add Drawings...** adds library drawings on
their own, outside any set: it browses every active drawing as the set editor's
library pane does (search, server-side sort, pages, several selected at once),
and **Add to Release**, a double-click or Enter adds the selection at the
entered quantity. It stays open for more; a drawing already listed is greyed
and skipped, and an archived one is refused. **Remove** takes a set or drawing
out while another remains. The list shows each line's type; **Quantity** is how
many of a set (1 to 1000, multiplying its quantities) or of a drawing (1 to
100,000), and a set or drawing is listed once. A drawing that a set also lists
becomes one item with both quantities. The heading reads as a sentence of what
is listed, such as "Release set 'Frame kit' ×2", or the first few lines by name
and quantity when there are several.

Below, a sentence previews what **Make Nests...** will do, such as "Creates 3
nests: A36 Grade A 0.25 in, 304 0.1875 in, ...", and the grid has one row per
material (name, grade, thickness, units and cut type) across all the sets and
drawings (`OpenNest.Data.SetReleasePlan`): material, grade, thickness, units,
cut type, drawings, parts and status. Refused groups (two drawings with one name,
or drawings without material or thickness) show why in the status column and
the preview counts them. **Fix Drawings...** opens
[Edit Drawing](#edit-drawing-desktop) on the selected refused group's drawings,
or the first refused group's when another row is selected, and rebuilds the plan
with what was saved. Archived members are left out and named below the grid.

The common case needs nothing more: every releasable material makes a new Draft
nest and **Nest after release** is ticked. **Options ▸** expands the choices that
change that: a **Release** tick per group (refused groups cannot be ticked), the
**Nest** column, and **Include archived drawings**, which puts archived members
back in. A tick survives changing the sets, quantities or the archived toggle.
Collapsing Options hides only what is at its default: an unticked group keeps the
Release column shown, and a ticked Include archived drawings stays shown. The
**Nest** column also shows, with a hint in the preview, whenever a Draft nest
exists that a material could merge into.

**Nest** is **New Draft nest** or **Merge into** one of the Draft nests of the
group's material, grade and thickness, listed with their sheet count and when
they were saved. A merge adds the demand to that draft's drawings and discards
every sheet (placements, cutoffs, cut plans), keeping drawing-level setup such as
cut pins (the status column says so); **Undo Release** restores it. A draft open
in a tab of this desktop is refused, since that tab could hold edits the merge
would leave behind: close it first.

**Nest after release** (ticked by default) runs Auto Nest on every released
group, new or merged, before anything is saved (`OpenNest.Engine.Jobs.NestRenest`):
every drawing's required quantity, the selected engine, sheets from the nest's
plate defaults and plate-size options, as the Auto Nest dialog would offer them.
A layout is kept only when it is valid; otherwise the nest is saved without
sheets and its status says "Auto Nest could not keep a layout: open it and run
Auto Nest". The status column lists each group's sheets and utilization, and any
copies left unplaced, which stay as remaining quantity. Unticked, new nests are
saved without sheets and merged drafts are left cleared for Auto Nest.

**Make Nests...** confirms, then runs `OpenNest.Data.SetReleaseSession`: every ticked
group's new nest is made as File > New would (the server's next nest name, the
saved plate defaults and clamps), in the group's units, with the saved plate size
and spacings converted (25.4 mm per inch) when those differ; the nest takes the
group's material, grade and thickness, the sets' customer when they share one,
and the **Draft** status (with drawings on their own, their library customers
count too). A merged draft is saved only while nobody else has
saved it since it was listed. Each created or merged nest opens in its own tab,
bound to its server record, and the release (with the release notes) is
recorded. A release with drawings on their own first checks that the server can
record them (see below) and, on an older server, saves nothing and says so. The
grid then shows every group's outcome: Created or Merged into with
the nest name, Refused or Failed with the reason, or Not started. Cancel during
preparing or nesting saves nothing; once saving starts it stops between saves,
never during one. When every nest was saved but recording failed,
the button becomes **Try Again** and posts the same release again without making
the nests again. A dialog
runs one release.

**Releases...** lists the releases that include the selected set, newest first:
when, by whom, the set's **Revision**, the **Quantity** of this set (and how many other sets
and drawings the release had), the number of nests, progress such as "2 of 3 cut" (deleted
nests are not counted) or "Undone", and the notes. The selected release's nests
show their **Material**, the **Action** ("Created", "Merged (revision
3 → 4)", and what undo did) and current status, or Deleted. **Undo Release...**
deletes the nests the release created (they can be recovered) and restores the
drafts it merged into. When some nests were changed after the release, it lists
them and offers to undo the others, leaving those as they are.

**File > Releases...** lists every release on the server the same way, newest
first, with a **Released** column naming what each one had ("Frame A ×2, Frame B;
Bracket ×4": sets with their quantity past one, then drawings on their own with
their quantities) in place of the revision and quantity, and Undo Release.
**New Release...** opens the release dialog empty; sets are added by name and
drawings with Add Drawings, and once the dialog records a release the list
re-reads from the newest. Like Drawing Sets, it is enabled in database mode
only.

**Nest > Edit** (Nest Info) shows, beside its buttons, the newest release that
made or merged into a server nest ("Released from set 'Frame A' revision 3 ×2 on
10/20/2026 by ...", "Released from drawing 'Bracket' ×4", or "Merged from sets
..."), with a count of earlier ones. It
is read in the background and left blank when the nest came from none or the
server cannot be reached.

## Drawing set endpoints

A **drawing set** (schema v7) is a named, versioned list of library drawings
with a quantity each: an assembly, a job or a recurring order. Members name
drawings by id and always mean each drawing's **current** revision; there is no
per-member revision pin. Sets travel as JSON (`application/json`, at most 1 MiB).
The desktop browses, edits and releases them in File > Drawing Sets
([Drawing sets (desktop)](#drawing-sets-desktop)).

`OpenNest.Data.DrawingSetRecord` fields: `id` (client-allocated before the
first save), `revision`, `name`, `customer`, `notes`, `archived`, `savedBy`,
the server-computed `savedAt` and `memberCount`, and `members`, in order, each
`{ drawingId, quantity, drawing }`. `drawing` is the member drawing's current
library record (archived or not), joined by the server on every read so a client
can group members by material without one request per drawing; it is ignored on
save, so a record read from the server may be sent back as it is.

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/sets` | JSON set with a nonempty `id` and `members` | Revision 1; 409 with the current record when the id exists |
| GET | `/api/sets/query?search=&customer=&includeArchived=&sort=&order=&offset=&limit=` | — | `{ items, total, offset, limit }`; items have `memberCount` and no `members` |
| GET | `/api/sets/{id}` | — | Current record with joined members (archived sets included), or 404 |
| GET | `/api/sets/{id}/revisions?offset=0&limit=100` | — | Revision page without members, newest first; limit 1–200 |
| GET | `/api/sets/{id}/revisions/{revision}` | — | That revision's details and members, each joined to the drawing's current record |
| PUT | `/api/sets/{id}` | JSON set (`id` empty or matching the route) | New revision |
| POST | `/api/sets/{id}/archive` / `/restore` | — | New revision with `archived` changed and the same members |
| GET | `/api/drawings/{id}/sets?offset=0&limit=100` | — | Sets, archived included, whose current revision contains the drawing, by name; 404 for an unknown drawing |

- Existing-set writes follow the drawing contract: `If-Match` 428 missing, 400
  malformed, 409 stale with the current record; writing to an archived set,
  archiving an archived one or restoring an active one is also 409. There is no
  delete.
- Every change appends an immutable revision, with its own copy of the members,
  in the transaction that updates the current row. An identical save (name,
  customer, notes and members in order) at the current revision is a no-op;
  reordering members is a change.
- Validation (400, no state change): `name` nonblank; name, customer and savedBy
  at most 200 characters, notes at most 4000, no NUL characters; `members`
  present and not null, at most 500, each with a nonempty `drawingId` that is in
  the library (archived drawings allowed), an integer `quantity` from 1 to
  100000, and no drawing listed twice (one member carries the whole quantity). An
  empty set is allowed.
- Query: `search` matches name, customer or notes and `customer` matches by
  `SharedListNames.Key`, exactly as for drawings. `sort` is one of `savedAt`
  (default), `name`, `customer`, `revision`; ties break by `id`. Paging, `order`
  and the refusal of unknown, repeated or out-of-range parameters follow the
  drawing query.

### Set releases

A set is never added to a nest, because a nest is one material. It is
**released**: the client makes one nest per material group with every quantity
multiplied, uploads each nest, and only then posts a **release record** naming
the set revisions and the nests. A failure part-way leaves ordinary nests and no
release, and nothing is rolled back silently.

Since schema v9 a release covers one or more sets (`release_sets`), and a nest
may be one the release **created** or an existing draft it **merged** into
(`release_nests`, with the nest revision before and after). The drawing
quantities each set added to each nest are kept in `release_nest_items`, so a
set can still be traced once sets share a nest. The v8 → v9 migration turns each
v7 release into one set line (its multiplier is the quantity) and created nests
at their first revision, with no items. Since schema v11 a release may also
name library drawings **on their own** (`release_drawings`), with or without
sets; their items in `release_nest_items` have no set.

`OpenNest.Data.SetReleaseRecord` fields: `id` (client-allocated, so an
ambiguous post can be retried), `sets` (each `{ setId, setRevision, quantity,
setName }`; `setName` is the set's name at that revision, joined on read),
`drawings` (each `{ drawingId, quantity, drawingName }`, the drawings released on
their own; `drawingName` is the drawing's current library name, joined on read),
`notes`, `releasedBy`, the server-computed `releasedAt`, `undoneAt` and
`undoneBy`, and `nests`, in order, each `{ nestId, kind, revisionBefore,
revisionAfter, materialName, materialGrade, thickness, units, cutType, items,
undo, undoRevision, nest }`. `kind` is `created` (no `revisionBefore`) or
`merged`; `revisionAfter` is the nest revision the release wrote. `items` are
`{ setId, drawingId, drawingRevision, quantity }`. `undo` and `undoRevision`
are server-computed. An item's `setId` is null for a drawing released on its own;
every other item names one of the release's sets. `nest` is the nest's current metadata (deleted or not),
joined on every read so status and deletion are always current. Joined and
server-computed values are ignored on post.

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/api/releases` | JSON release with a nonempty `id` | The stored release; 400 for an unknown set or revision; 409 with the release that already holds the id or one of the created nests |
| GET | `/api/releases/query?setId=&nestId=&drawingId=&offset=0&limit=100` | — | Release page, newest first, optionally only releases including the set, the nest or the drawing on its own; limit 1–200 |
| GET | `/api/releases/{id}` | — | The release, or 404 |
| POST | `/api/releases/{id}/undo` | `{ "undoneBy": "...", "skipNests": [...] }` | The undone release; 404 for an unknown release; 409 with `{ reason, release, changedNests }` |

- Releases are append-only: there is no edit or delete. Undo marks the release
  undone and records what it did to each nest.
- Set revisions and drawings on their own must exist but need not be current, and
  sets and drawings may be archived: the
  nests already exist by the time the release is posted, so it is never refused
  for a state the operator cannot fix. A merged nest need not still be at
  `revisionAfter` when the release is posted.
- A created nest belongs to that release only; a merged nest may be in several.
- **Undo** runs in one transaction. Each created nest is soft-deleted and each
  merged nest restored to `revisionBefore`, except the nests in `skipNests`
  (recorded as `skipped`). Every other nest must still hold `revisionAfter`: be
  at it, or have been restored to it as its latest save. Otherwise nothing
  changes and the 409 lists the changed nests (`reason` `nestsChanged`), so the
  caller can skip them and try again. A second undo is refused with
  `alreadyUndone`. Because each undo writes a new nest revision, releases that
  share a nest are undone newest first.
- Validation (400, no state change): at most 50 `sets`, no set twice, each
  quantity 1–1000; `drawings` (may be left out, as older clients do) at most 500,
  no drawing twice, each quantity 1–100,000; at least one set or drawing; notes at most 4000 and releasedBy at most 200 characters; `nests`
  present, 1–500 entries, no nest twice, each an existing, not deleted nest whose
  named revisions exist, with material name and grade at most 200 characters,
  `thickness` finite and at least 0 (stored rounded to 4 decimals), and `kind`,
  `units` and `cutType` single enum names; each item names one of the release's
  sets, or has no set and names one of its drawings on their own, at most once per
  set (or no set) and drawing, with a positive revision and quantity.
- **Compatibility.** A v11 server accepts releases from older clients (no
  `drawings`). A server older than v11 knows no `drawingId` parameter and answers
  `400` for it, so clients probe `GET /api/releases/query?drawingId=<any id>&limit=1`
  before building a release with drawings on their own; a 400 or 404 there means
  the server is too old, and nothing is saved.

#### Client and release session

`OpenNest.Data.RemoteDrawingSetStore` is the client for every route above. It
checks `DrawingSetRules`/`SetReleaseRules` before sending, raises
`ServerTooOldException` on a server without sets (or, for releases, without
`/api/releases`, and for drawings on their own, without the `drawingId` query:
`EnsureDrawingReleasesSupportedAsync`), `DrawingSetSaveConflictException` or
`SetReleasePostConflictException` (carrying the current record) on 409,
`SetReleaseUndoConflictException` (carrying the refusal) when an undo is
refused, and refuses replies whose id, revision, members, sets or nests differ
from what was sent.

`SetReleasePlan.Build(lines, drawings, includeArchived)` splits sets read from
the server, each with a quantity (`SetReleasePlanLine`), and library
drawings on their own, each with a quantity (`SetReleaseDrawingLine`), into
groups without any I/O; either list may be empty, not both (`Build(lines,
includeArchived)` and `Build(set, multiplier, includeArchived)` are the
sets-only forms). A drawing listed by several sets, or by a set and on its own,
becomes one item with the quantities summed and each share kept (`Sources`; a
share with no set is the drawing on its own), using the newest revision it was
read with. A drawing listed twice on its own is refused (give it a larger
quantity). A group is the items whose drawings share material name and grade (by
`SharedListNames.Key`), thickness (rounded to 4 decimals), units and cut type.
The plan's customer is the one every set and drawing on its own names, or blank. A set listed twice is
refused (give it a larger quantity instead). Archived drawings are skipped unless included. A group is refused when two of its drawings share a
name (compared ordinally, as a nest keys drawings), and every drawing without a
material or a positive thickness goes to one refused "unassigned" group, listed last.

`SetReleasePlan.FindDraftsAsync(nests, group)` lists the Draft nests a group
could merge into, using the nest query's status, material, grade and thickness
filters. Units and cut type are not in nest metadata, so they are checked on the
downloaded nest.

`SetReleaseSession.RunAsync` releases the chosen releasable groups in plan order.
A group named in its `mergeInto` map merges into that draft instead of making a
new nest. When the plan has drawings on their own it first asks the server
whether it can record them (`IDrawingSetStore.EnsureDrawingReleasesSupportedAsync`):
an older server refuses the first group with the reason, a failed check fails
it, and nothing is built or saved.

1. **Build every group first.** For a new nest the caller's factory makes an
   empty nest in the group's units; the session sets material, grade,
   thickness, the plan's customer (when there is one) and `Draft`. For a merge
   the session downloads the draft at the revision it was chosen at (reading it
   with the caller's nest reader) and refuses it unless it is still a Draft of
   the group's material, grade, thickness and units with no drawing for another
   cut type; then it removes every plate, since a merge renests the whole
   demand. Either way it downloads each item's exact revision and inserts it
   through `LibraryDrawingInsert` (a drawing the draft already holds at that
   revision gains quantity; another revision is refused, pointing at Update
   Drawings). When the caller supplies a renest step ("Nest after release"),
   every group is then nested from scratch; a step that throws leaves the nest
   without plates and a note, and the release goes on. A refusal or a download
   failure stops here, so nothing is uploaded. The factory may already have
   allocated nest numbers.
2. **Upload in order** under client-allocated ids, never cancelling an upload in
   flight; cancellation is checked between uploads. A failed upload is checked by
   its id, so one stored before its reply was lost still counts as created. A
   merged draft is saved with the revision it was chosen at as `If-Match`; a
   failed save counts only when the server holds exactly the bytes sent, and
   otherwise says whether someone saved the draft meanwhile. The first real
   failure stops the rest.
3. **Post the release** only when every chosen group was created or merged,
   under a client-allocated id. It names every set with its quantity, every drawing
   on its own with its quantity, each nest as created or merged with the
   revisions before and after, and the drawing revisions and quantities each set
   (or no set, for a drawing on its own) put in each nest. When the post fails, the nests stay and
   `RetryReleaseAsync` posts the same id again; a 409 naming that id is adopted
   as the stored release.

The result lists each group's outcome (`Created`, `Refused`, `Failed`,
`NotStarted`), the created or merged nests' records and in-memory `Nest` objects
(so the desktop can open them without downloading), the draft each merged into,
what nesting after release did, and the release or why it was not recorded.

`RemoteDrawingSetStore.UndoReleaseAsync` undoes a release (see above);
`SetReleaseText.UndoRefusal` words a refusal for the operator, naming each changed nest.

Schema v7 adds `drawing_set_revisions` (every revision's details and archived
flag), `drawing_set_members` (each revision's members by position, unique per
drawing), `drawing_sets` (each set's current revision), `set_releases` and
`set_release_nests`. Upgrading from v6 only creates these tables; every other
table is untouched. A server older than
v7 refuses to open a v7 database, so rolling back needs the pre-upgrade backup.
The stopped-volume `tar` backup includes the set and release tables; `nest_manifest.py` and
`restore_check` do not verify them yet.

Schema v8 adds a `materialGrade` text column (default blank) to `nests` and
`nest_revisions`; existing records keep a blank grade until they are next saved.
A server older than v8 refuses to open a v8 database, so rolling back needs the
pre-upgrade backup.

## Material list endpoints

The server keeps the company material list: each material with its grades. Both
are keyed by `SharedListNames.Key`, so names that differ only in case or spacing
are the same entry, and an entry keeps the spelling it was first added with
(trimmed, internal spaces collapsed). Nests, drawings and releases keep their own
free-text material and grade. The list only suggests names and records the
company spelling; it does not check or rewrite stored records. A grade belongs to
one material, and the same grade name may be listed under several materials.
Wire types are `OpenNest.Data.MaterialListEntry` and `MaterialListAddition`.

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/api/materials` | — | `[{ "name", "grades": [...] }]`, materials and grades in case-insensitive name order |
| POST | `/api/materials` | `{ "name": "...", "grade": "..." }` (`grade` optional) | The material as listed: 201 when the material or grade was added, 200 when both were already listed |

Validation (400, no state change, `MaterialListRules`): a JSON object with no
other properties; `name` not blank; name and grade at most 200 characters with
no NUL characters.

Schema v10 adds empty `materials` and `material_grades` tables. Existing
records are not read into the list; fill it through `POST /api/materials`. No
other table changes. A server older than v10 refuses to open a v10 database, so
rolling back needs the pre-upgrade backup.

Schema v11 adds `release_drawings` (each release's drawings on their own, by
position, unique per drawing) and rebuilds `release_nest_items` with a nullable
`setId`, copying every row (SQLite cannot drop `NOT NULL` from a column). No
other table changes. A server older than v11 refuses to open a v11 database, so
rolling back needs the pre-upgrade backup.

## Storage

`OpenNest.Server.NestDatabase` uses SQLite (`Microsoft.Data.Sqlite`) with
`journal_mode=WAL`. Each row holds the metadata columns plus the `.nest`
archive as a `BLOB`. The database file path comes from `--database=<path>` or
`--database <path>`, then `OPENNEST_DB`, defaulting to `./data/nests.db`; a
`--database` without a path, or given twice, fails startup. The server opens the
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

### Redeploying a development build

Until a verified image is published, `scripts/Deploy-Server.ps1` redeploys a
development build of this checkout to a host's Compose stack over SSH, using the
local Docker engine (no build on the host, no registry):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Deploy-Server.ps1 `
    -DeployHost <host> -StackDir ~/docker/opennest `
    -LocalComposeFile <config-repo>\opennest\compose.yml
```

It builds `opennest-server:<yyyyMMdd>-<short sha>` (`-dirty` and a warning when
`OpenNest.Server`, `OpenNest.Data` or `OpenNest.Core` have uncommitted changes;
`VERSION` stays `0.0.0-dev`, `SOURCE_REVISION` is the full commit), copies it with
`docker save`/`scp`/`docker load`, stops the container, tars the data volume to
`<BackupRoot>/opennest-<tag>/backup/` with the previous `compose.yml`, rewrites the
stack's `image: opennest-server:<tag>` line, starts the stack and waits for
`healthy`. The stack's `compose.yml` must name the image literally on that line,
not through a variable as `compose.server.yaml` does. `-LocalComposeFile` updates
a config repository's copy, uncommitted. Host, user and paths are parameters so
no private address is in source; `-SkipBackup` and `-NoPull` are available but
the backup is the only way back across a schema upgrade.

Build from a pushed commit (a clean worktree is simplest) so the running image
can be traced. When the build raises the schema version, first run the new image
in a throwaway loopback container on a snapshot of the live database and check
`user_version`, integrity and record counts. To roll back, stop the stack,
restore the tarball into the volume and set the printed previous tag: an older
server refuses a newer schema.

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
