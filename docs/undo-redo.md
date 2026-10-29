# Undo and Redo

The desktop's Undo and Redo (Edit menu; Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z) work on whole-nest
states. Each open nest has a history of in-memory `.nest` copies; Undo reads the previous
copy back and swaps it into the window. There are no hand-written inverse operations: anything
the [nest file format](nest-file-format.md) saves is undoable, and a new edit becomes undoable
once its entry point records it.

| Piece | Where |
|---|---|
| History engine (states, cursor, limits, coalescing, save point) | `OpenNest.Data/UndoHistory.cs` |
| State codec | `OpenNest.IO/NestSnapshot.cs` |
| Recording scope, fingerprint, history state | `OpenNest/Undo/` |
| Recording, restore, safety net | `OpenNest/Forms/EditNestForm.Undo.cs` |
| Menu, keys, guards | `OpenNest/Forms/MainForm.Undo.cs`, `MainForm.Guards.cs` (`IsUndoBusy`) |
| Tests | `OpenNest.WinForms.Tests/Forms/UndoRedoTests.cs`, `UndoRecordingTests.cs` |

## What is undoable

Everything stored in the nest:

- parts: add, delete, cut, move, drag, nudge, rotate, align, push, center, expand spacing, clone;
- placement: fill, fill area, pattern tile, best-fit placement, Auto Nest, Clear Nest;
- plates: add, remove, remove empty plates, Edit Plate (its sheet outline included), rotate,
  resize, cutting parameters, clamps and clamp rail, the support grid, the nest's default plate;
- cutting: manual and automatic cut-offs and their drags, Auto Sequence, Set Sequence,
  Sequence by Path, placing and removing lead-ins, Plan Cutting's Apply, clearing lead-ins and
  cut-offs;
- drawings: import, Shape Library, edit in the converter, drawing properties and quantities, delete, clean
  unused, Drawing Library adds, a drawing's library link after Save to Drawing Library;
- the nest: its machine and the Nest > Edit fields other than identity (below).

The Edit menu names the step: "Undo Rotate", "Redo Fill". `UndoRecordingTests` holds one test
per recorded edit with its label.

## What is not undoable

Deliberately excluded:

- anything outside the nest: Save, Save As and database saves, posting and exports, releases
  and Undo Release, Drawing Library server writes (the library entry Save to Drawing Library
  creates stays), drawing-set edits, Save as Machine Defaults (clamps, support grid), machine
  and post configuration, nest defaults;
- view state: zoom, pan, view toggles, selection, the active side panel;
- the nest's identity: `Name`, `Status`, `MadeBy` and `DateCreated`. A restore carries them
  over from the live nest, so a Nest > Edit change of only these records nothing. All `Document`
  state (save path, server id and revision) stays too: the `Document` is never replaced.

A save does not clear the history; it marks a save point (`UndoHistory.MarkSaved`,
`IsAtSavePoint`). A database save marks it only when no edit was recorded while it ran.

## Recording rule

Every desktop edit that changes a nest goes through `EditNestForm.BeginChange(label)` (or
`PlateView.BeginChange`, which forwards to its window) or the `RecordChange(label, edit)`
helper, with a short user-facing label:

```csharp
using var change = BeginChange("Rotate");
change.MarkChanged();
...mutate...
change.Complete();
```

- Dispose after `Complete()` commits one history entry. A scope that never called
  `MarkChanged()` records nothing, so a cancelled dialog or an edit that changed nothing does not
  add an entry.
- A scope disposed without `Complete()` (an exception, a cancellation) rolls a changed nest back
  to the last recorded state.
- A scope opened inside another merges into the outermost one: one entry under the outer
  label. A nested scope left incomplete rolls back the whole entry.
- Record in the desktop only, at the command's entry point. Never in engines, Core, IO or
  dialogs that do not own the window's nest.
- Long or asynchronous runs (Fill with progress, Auto Nest, automatic cut-offs, Plan Cutting)
  open the scope at their final commit and record only the result they keep. A stopped run
  that keeps nothing records nothing; an interactive drag records one entry on mouse-up.
- Repeated edits with an equal `coalesceKey` within one second undo as one (arrow-key nudges of
  one selection). An Undo, Redo or save in between ends the run.

### The "Change" safety net

The window keeps a fingerprint (`NestFingerprint`: plate sizes and parts, poses, programs,
cut-offs, drawings and required quantities, objects by reference) of the last recorded state.
Before a scope opens and before Undo or Redo, a changed fingerprint means an edit bypassed
recording; it is committed as an entry labelled "Change", so it is never discarded. The
fingerprint is a cheap hash, so this is best effort and no excuse to skip recording. In DEBUG
builds an `Application.Idle` check writes a `Debug.WriteLine` warning when the nest changed
outside a scope; watch the output window when adding an edit.

## How a state is made

`NestSnapshot.Capture(nest)` writes the archive a save would, with three differences:

- `PreserveDateLastModified`: a normal write stamps the live nest's modified date;
- `KeepEmptyPlates`: a file write drops empty plates, which would lose an added plate and shift
  plate indices. Snapshots keep every plate, the trailing sentinel too;
- no `BestFitStore`: best fits are a cache, never part of history.

`snapshot.Restore(live)` reads the copy and reuses each live `Drawing` whose saved content is
unchanged (matched by `NestWriter.GetDrawingFingerprints`), so `BestFitCache`,
`FillResultCache`, colours and `Drawing.Id` survive. A changed drawing, a quantity edit
included, comes from the copy. Restore then recomputes nested quantities.

Never compare snapshots byte for byte to detect a change: two writes of one nest can differ in
derived values (`expectedRemnant` around 1e-10). The scope's `MarkChanged` flag decides.

## What a restore rebuilds

`EditNestForm.RestoreNest(restored)`:

1. Closes the Cutting Inspection panel, and refuses if it stays open (below).
2. Copies the identity fields from the live nest.
3. Captures the plate index, viewport and selected part indices.
4. Ends the current action, clears preview parts, cancels the overlap check and any pending
   drawing-list drag, and hides the side panel: all of them hold parts, plates or the nest.
5. Unbinds the old nest, replaces `Document.Nest` and binds the new one through the same path
   the constructor uses (`PlateManager`, sentinel, units, cut-off defaults, overlap check).
6. Regenerates each plate's cut-offs with `PlateView.CutOffSettings` (reading uses defaults)
   and recolours drawings with the current scheme, as Open does.
7. Loads the same plate index (clamped), restores the viewport instead of zooming to fit, and
   reselects by part index when the plate's part count is unchanged.
8. Refreshes the plate list and header, then raises `NestRestored`; the main window closes a
   remnant viewer on that view and refreshes the status bar, overlap and Edit menus.

`CuttingInspectionPanel` holds the old nest's plates, its staged cut-offs and a proposal. Apply
after a restore would commit to plates no longer in the nest, so it must close first. Undo is
unavailable while it is open, and the main window swallows Ctrl shortcuts during inspection.

Every `Nest`, `Plate`, `Part` and `Program` is a new object after a restore. Never key state on
them, or on `Drawing.Id`, across an Undo; a new holder of nest objects must be closed or rebuilt
by `RestoreNest`.

## Guards and keys

Undo and Redo do nothing, and the menu items are disabled, while:

- `MainForm.IsUndoBusy`: a whole-job run or progress window, a background database save
  (it serializes the live nest), a fill on any view of the nest, or a current action whose
  `Action.BlocksUndo` is true. `BlocksUndo` defaults to `IsBusy()`; `ActionSelect` blocks only
  during a cut-off drag (a selection is not busy), and `ActionLeadIn` never blocks;
- a change scope is open or a restore is running (`IsChangeInProgress`);
- Cutting Inspection is open.

Keys are routed in `MainForm.ProcessCmdKey`; the menu items show shortcut text but have no
`ShortcutKeys`. A focused text box, numeric box, editable combo box, grid cell in edit mode or
the program editor keeps its own Ctrl+Z and Ctrl+Y (`KeepsOwnUndo`). Otherwise Ctrl+Z first
asks the current action's `TryUndoStep()`: Sequence by Path removes its last line, and only
when it has none does the nest undo.

## Limits and cost

- 50 undo steps and 128 MB per open nest (`UndoHistory.DefaultMaxDepth`, `DefaultMaxBytes`);
  the oldest states are evicted first, never the current one.
- Typical nests take a few kilobytes and a few milliseconds per state. Measured on the
  heaviest production nests (one of about 7,000 parts, one with 101 drawings) with every part
  planned: capture about 350 ms and 2.2 MB, Undo about 1 s, most of it the plate reload
  (`LayoutPart.Create` per part, the same as switching to that plate). Opening a nest pays one
  capture for the history's first state.
- The worst nest's 50 states stay inside the byte budget (about 110 MB). Plate-scoped states
  are not needed unless a heavier nest shows lag.
