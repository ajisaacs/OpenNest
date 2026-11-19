# MCP nest server tools

`OpenNest.Mcp` has Database-mode tools beside its local `.nest` tools: an agent can go from
"these sets × these quantities" to saved, nested Draft nests on the nest server in one or two
calls, the same nests the desktop's **Make Nests** makes ([release panel](nest-storage.md#make-nests-the-release-panel-desktop)).
They live in `OpenNest.Mcp/Tools/NestServerTools.cs`; the release itself is
`OpenNest.Data.SetReleaseSession`, unchanged.

## Setup

The tools read this PC's desktop settings when first used (`NestServerContext.Load`):

- `%APPDATA%\OpenNest\storage.json` must be in Database mode with a server URL; otherwise
  every tool returns `Error: OpenNest is not in Database mode...`.
- New nests are made as File > New makes them (`OpenNest.Desktop.NewNestSource`): nest
  defaults and machines from the server's shared copy or this PC's files
  (`SharedSettingsSource`, `MachineSettingsSource`), the group's units, material, thickness
  and machine spacing, and a server-allocated name. The local counter in
  `user-settings.json` is the fallback, as on the desktop.
- Auto Nest uses the engine saved in `engine-selection.json` (the desktop's last choice)
  unless a call names one, with `Nest.DefaultMinimumRemnantDimension`.
- MaterialLedger stock is offered first when `material-ledger.json` is set up for this
  Windows user (the key is DPAPI-protected); otherwise the nests use sheet sizes only and the
  result says so.
- `SavedBy`, `ReleasedBy` and `UndoneBy` are the Windows user name.

Every tool returns text. Failures start with `Error:` and say what was left unchanged.
Sets, drawings and nests are named by id or exact name (case-insensitive); an ambiguous
name is refused with the ids to choose from, and an unknown one lists similar names.

## Tools

| Tool | Writes | What it does |
|---|---|---|
| `search_drawings` | no | Library query: text, customer, material, grade, thickness, `includeArchived`, paging. |
| `get_drawing` | no | One drawing's record, notes and the sets that list it. |
| `search_sets` | no | Set query: text, customer, `includeArchived`, paging. |
| `get_set` | no | One set with each member's drawing and quantity per kit. |
| `create_set` | yes | New set from `members: [{drawing, quantity}]`; refuses a name another set has, an unknown drawing or one listed twice. |
| `update_set` | yes | Next revision: omitted fields keep their value, `members` replaces the list. The `If-Match` revision is the one just read. |
| `archive_set`, `restore_set` | yes | Archive or restore at the current revision. |
| `plan_release` | no | `sets: [{set, quantity}]` (kits) and `drawings: [{drawing, quantity}]` (on their own) into material groups (`SetReleasePlan.Build`): each group's key, parts, warnings, refusal, and the Draft nests it could merge into (`SetReleasePlan.FindDraftsAsync`). |
| `make_nests` | yes | The release; see below. |
| `renest_nest` | yes | Re-nest a saved Draft or Quote nest; see below. |
| `nest_summary` | no | A saved nest: status, material, plate defaults and options, sheets with sizes and utilization, each drawing's required and nested quantity. |
| `get_release` | no | One release: sets, drawings, notes and its nests with undo state. |
| `undo_release` | yes | `UndoReleaseAsync`, all or nothing. A nest changed since the release refuses it and is listed; pass its id in `skipNests` to leave it as it is. |

### make_nests

Takes the same `sets` and `drawings` as `plan_release`, plus `notes`, `sheetSizes`, `merge`,
`includeArchived`, `nestAfterRelease` (default true), `useLedger` (default true),
`skipRefusedGroups` (default false) and `engine`.

- Every group must be releasable, or nothing is created and the refusals are listed;
  `skipRefusedGroups` releases the others (the desktop's unticked rows).
- `merge: [{group, draft}]` merges a group (its key from `plan_release`) into a Draft nest
  instead of a new one. The session re-checks the draft (status, units, material, cut type)
  and saves over the revision just read.
- With `nestAfterRelease`, each nest is nested before anything is uploaded, as
  `ReleaseRenest.RunHeadlessAsync` does for the desktop (ledger first, then the plate
  defaults). A nest is saved even with parts unplaced, as on the desktop; the result lists them.
- The result gives the release id (or why it was not recorded), and per group its outcome,
  nest name, id and revision, new or merged, each plate's size, count, parts and
  utilization, parts required and nested, unplaced parts, sheet trials and notes.

### Sheet sizes

`make_nests` and `renest_nest` take `sheetSizes`, such as `["60x120", "72x144", "12x72"]`:
width × length in the nest's units. Without it a nest uses its plate defaults and plate
options, as the desktop does. With one size, that size is the plate default and the plate
options are cleared. With several, `SheetSizeTrials` nests a copy of the nest (read back
from its archive) on each size alone and on all of them together as `Nest.PlateOptions`,
which the engine may mix, without ledger stock. It keeps the layout that was kept at all,
then leaves the fewest copies unplaced, then uses the fewest physical sheets, then the least
sheet area, and the result lists every trial. The choice is then nested again on the release's
own nest (with ledger stock first when set up), so the kept layout may differ slightly from
its trial when the engine is not deterministic. The saved nest keeps the chosen size or options as its
defaults. The engine's own stock choice with plate options minimizes net area, not sheet
count, which is why sizes are also tried alone.

### renest_nest

Re-nests a Draft or Quote nest from scratch: every plate removed and every drawing's
required quantity nested again on `sheetSizes` (or its own defaults), ledger stock first
when `useLedger` and set up (codes on this nest are not counted as in use). It saves a new
revision only when a layout was kept and every part is placed, with
`UpdateFileAsync(id, archive, record, expectedRevision)`, so a nest saved since it was read
is not overwritten. `save: false` only reports.

## Example

```text
search_sets {"search": "element rack"}
plan_release {"sets": [{"set": "4348 A21 Lower element rack", "quantity": 12}]}
make_nests {"sets": [{"set": "4348 A21 Lower element rack", "quantity": 12}],
            "notes": "4348-102R: 12 lower element racks",
            "sheetSizes": ["12x72", "48x96", "60x120"]}
nest_summary {"nest": "N26-2CP"}
renest_nest {"nest": "N26-2CP", "sheetSizes": ["72x144"]}
undo_release {"release": "<id from make_nests>"}
```

## Tests

`OpenNest.Server.Tests/McpNestServerToolsTests.cs` runs the tools against the in-process
server (sets by name, a two-material release on two sheet sizes with server-named drafts,
a merge, guarded re-nests, a refused and a partial undo). Never point development or tests
at the production nest server.
