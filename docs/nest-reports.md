# Nest report PDF export

Desktop: **File -> Export Nest Report...** writes `<nest-name>.report.pdf` for the
active nest's whole job through an overwrite-confirming save dialog. The command
is disabled without an open document and during a background database save, and
it refuses to run while whole-job nesting, an open progress window, interactive
fill or a busy plate action holds the nest or any second window sharing it. After
the dialog closes, the target and those conditions are revalidated; the snapshot
is then captured synchronously on the UI thread and only that detached snapshot
reaches the renderer. Export never changes the selected plate, dirty state,
timestamps or quantities, and it never touches post selection, verification or
CNC output. There are no persisted report settings or templates.

**File -> View Nest Report...** runs the same guards and capture without a dialog,
renders to a private folder under `%TEMP%\OpenNest\Reports\` and opens a modeless
viewer window (WebView2's PDF viewer, sharing Help's WebView2 data folder) with
**Save As...**, which copies the rendered file to a chosen path, and **Print...**,
the WebView2 print dialog. Closing the window deletes its temporary folder; a
render failure opens no viewer and leaves no folder. Without the WebView2 Runtime
the window still offers Save As.

The same two calls back the command and any future integration:

```csharp
var snapshot = NestReportBuilder.Capture(nest, DateTimeOffset.Now);
NestPdfWriter.Write(snapshot, destinationPath);
```

Reporting does not select a post, generate CNC, refresh drawing quantities, change
the selected plate, or certify that a layout passed geometry/pre-post checks.

## Part printouts

**File -> View Part Printouts...** prints every non-cutoff drawing of the active
nest; **View Part Printout...** on the drawing list's right-click menu prints the
selected one. Both render to `%TEMP%\OpenNest\Reports\` and open the report viewer,
suggesting `<nest-name>.printout.pdf` or `<drawing-name>.printout.pdf`. Each part
gets one Letter portrait page, bookmarked by name, with the name and customer (the drawing's, else the
nest's) as the header, the part's material contours stroked to fit (leads, rapids
and marks omitted), a details table and the generated time. The table shows the
bounding box (Y extent x X extent), nest material and thickness (led by the
machine config's thickness name, as in the nest report), cut length and
pierce count, net area, weight
(the nest report's density rules) and required quantity. Cut length sums the moves the head cuts (cut, break, common-line and lead layers); each run of
them is one pierce, so scribe marks, tab spans, display moves and a display
perimeter's tail add neither length nor pierces, and a tabbed cutout counts one
pierce per piece. Printouts read drawings only, so running fills do not block them, and they share the nest report's
Latin-1 text limit and render lock.

```csharp
var snapshot = PartPrintoutBuilder.Capture(nest, drawingsOrNull, DateTimeOffset.Now, catalogDensity);
PartPrintoutWriter.Write(snapshot, destinationPath);
```

## Snapshot contract

- IDs (`1`, `2`, ...) are local to the report. Visit non-cutoff placements in plate
  and part order, then append unseen demanded drawings ordered by ordinal name and
  source path. Identity is by reference: distinct same-named drawings stay distinct,
  including placed drawings absent from the name-keyed drawing collection.
- Recount nested quantities from placements times sheet copies exactly once using
  checked wide integers. Include unplaced demand; report shortage and extra
  separately. Cached `Quantity.Nested` is not authoritative.
- Omit trailing empty stock plates from report pages, layout counts and physical
  sheet totals. Keep the live nest and remaining plate numbers unchanged.
- Distinguish distinct layouts from physical sheets. Per-sheet quantities do not
  include copies; total quantities do. Utilization uses net part area divided by
  full sheet area, excluding cutoffs.
- Actual placed programs already contain rotations. Capture their material paths
  with the location applied once. Preserve holes and intentional tab gaps; exclude
  rapid, scribe, lead-in and lead-out paths. Keep Cut and Display contours.
- If any material contour of a part is open, show all its contours without fill.
  Do not close the gap or substitute the drawing's nominal outline. Cutoffs are
  separate strokes with no product ID or quantity.
- Snapshots retain values, not live drawings, parts, CNC programs or mutable
  geometry. Invalid/missing/nonfinite geometry fails with drawing/plate context.
- A drawing's weight is its net area (holes excluded) times the nest thickness
  times the material density, read as **pounds per cubic inch** as in the machine
  material defaults (steel 0.2836). The nest material's density wins. Nest
  materials picked by name and grade carry none, so the desktop passes
  `Capture(..., catalogDensity)` from `MaterialDensityLookup`: the nest machine's
  exact name and grade, any machine's exact name and grade, then the same material
  name on the nest machine or any machine. The drawing's own material is the last
  fallback. Millimeter jobs convert the volume to
  cubic inches and report kilograms. With no thickness or no density the weight
  is unknown, never zero.
- A plate's sheet weight is one full sheet (its outline area for an irregular
  sheet) times the nest thickness and density, from the nest material or the
  catalog only: a drawing's own material does not say what the stock is.
- A plate's clamps are its clamp zones moved into the parts' frame with
  `ClampZoneFrame`, then any rail clamps taken off that sheet (`RemovedClamps`,
  resolved by the rail for the sheet), flagged as removed. Footprints stay whole,
  even where they hang past the sheet.

## Layout, pagination and dense-label fallback

An empty/demand-only job, a single plate, or many plates and drawings all
produce one document: a Letter portrait summary followed by one Letter
landscape section per plate, every page carrying a repeated header and a
footer with the generation time (left, in the computer's time zone, no offset)
and "Page X of Y" (right). Long notes, long drawing names, many drawings and many parts
per sheet paginate naturally: MigraDoc continues the Plates table and each
plate's part table across pages with the heading row repeated, the summary's
part cards continue row by row, and notes flow as an ordinary paragraph. No
row, card, table or note text is ever dropped or truncated to fit a page.

The summary shows each drawing as a card, four to a row: its ID, a thumbnail,
the drawing name, its bounding-box size (width x length, to three decimals),
the weight of one part and of all nested copies, required and nested counts,
and its plates. A shortage (**SHORT n**, in red) or extra (**EXTRA n**) sits
beside the ID; complete drawings show no status. Cards without a weight show
"-" with a note under the Parts heading saying what is missing. A card row is
a table row, so a card never splits across pages.

The summary's header lists the job and material on the left and the totals on
the right, to leave room for part cards on the first page. When the nest
machine's config names the thickness (its **Name / Gauge**, matched as the
[machine cutting defaults](machine-cutting-defaults.md) match it), the name
leads the decimal: "16 ga - 0.0598 in". The desktop supplies it, since
Reporting does not read machine configs.

Stock sizes read **width x length** in job units. Plate tables use **Quantity**
and **Parts per plate**; the plate quantity multiplies per-plate part counts for
the totals. The summary's Plates table adds a **Sheet weight** column when any
plate's weight is known; a plate with copies shows one sheet's weight, then all
copies on a second line.

Individual IDs use 7 pt text centered on the part's pole of inaccessibility
(`PolyLabel`, as in `PlateView`), clear of edges, holes, cutoffs and other labels.
The search uses a placement-independent quantized copy for consistent placement
on identical parts. When the pole is too narrow (a ring around a large hole), the
ID goes in the clear spot within the part's own extents with the most room, such
as an empty hole, nearest the part's center. Labels are best effort: there are no
closeup or grid pages, and a part too small to label never prevents export. Every
part remains in the quantity tables even when its diagram label cannot fit.

Nearby small parts with the same report ID may share a dotted rectangular outline
and one ID. Separate clusters get separate outlines; an outline stays within half
the part spacing of its parts, so clusters of different parts packed one spacing
apart each keep their own outline and ID. The group ID goes wherever it has the
most room, inside the outline (between the parts or in a hole) or beside it. With
no clear spot, it covers the group's own parts near the outline's center (the
parts list shows each part up close), but never another part or a cutoff. No ID
sits across the sheet outline. A group is omitted if its rectangle includes any
other part or cutoff, or if even that fallback finds no place for its label.
Clearance is tested against real contours, not bounding boxes, so a cluster nested
in a larger part's cutout or notch is still grouped and labeled. Dotted group outlines are distinct from dashed scrap cutoffs.

Machine clamps appear on the plate diagram as white, diagonally hatched
rectangles drawn under the parts, and IDs keep clear of them like any other
obstacle. A clamp taken off the sheet is a dashed rectangle crossed out, drawn
over the parts (they may sit where it was) so the operator sees it must come off
before cutting. A legend line explains them only on plates that have clamps.

Rulers sit beside the diagram: a horizontal one below it and a vertical one to its
left, each spanning the sheet and reading its plate coordinates (negative in
quadrants 2-4) in job units, with the unit in the corner between them. Numbered
ticks use the smallest 1-2-5 step at least 40 pt apart whose numbers do not touch,
subdivided as finely as stays legible. The diagram is fitted in two passes so the
ruler bands are sized to their numbers, and labels never enter those bands. The
rulers are drawn as their own content stream before the diagram's, so the
diagram's stream holds only the sheet and what is on it.

The writer still rejects, with `NotSupportedException` and before touching
the destination:

- a page header (nest name plus plate/material line) needing more than 3
  wrapped lines;
- a table cell or part-card field needing more than 20 wrapped lines.

Card thumbnails and the sheet diagram are vector paths, never raster images.
Each thumbnail is a small PDFsharp page embedded by MigraDoc as a form XObject.
The diagram is drawn into a fixed-height table row reserved in MigraDoc's flow,
located after layout with `DocumentRenderer.GetRenderInfoFromPage`. Closed parts
use one even-odd filled path, so holes stay unfilled. Parts with any open contour,
and cutoffs (dashed), stroke each contour as its own path: PDFsharp's
`StartFigure` does not start a new subpath after an open figure, and would
otherwise draw a false segment across a tab gap.

A report is fully rendered to a unique temporary sibling before replacement of
its destination. A failed render or write leaves an existing report untouched and
removes temporary output. Applications should obtain overwrite consent before
calling the library. PDFsharp/MigraDoc layout and font state is process-wide;
`NestPdfWriter.Write` serializes every export behind one static lock so
concurrent calls cannot lay out text differently from a sequential export.

## Estimated cut time

When the caller passes `CutParameters` (the desktop export always does, from the
settings last used in Calculate Cut Time or the simulator), each plate gets an
estimated time per copy from `CutSimulation`, plus clamp-removal minutes, the same
as Calculate Cut Time (see [cut-time estimates](cut-time-estimates.md)). The summary
shows the job total over all sheets, and the plates table gains an "Est. cut time"
column (a plate with copies gives the time for each, then the total on a second
line). Each plate page shows the time per plate and for all its copies. The report
does not state the settings behind the estimate. These are estimates, not
machine-reported times. Without `CutParameters` the report has no timing rows or column.

Cutting distances, pierce counts, costs, gas use, and machine/NC identity
are intentionally omitted until their semantics are verified.
The PDF can be opened or printed through a normal PDF viewer.

## Backend, fonts and redistribution

- Official `PDFsharp-MigraDoc` Core **6.2.4**, with `PDFsharp` **6.2.4**, targeting
  net8.0. No GDI/WPF dependency or printer driver is used. MigraDoc owns flowing
  document layout; PDFsharp draws vectors into its measured reserved areas.
- DejaVu Sans **2.37**, regular and bold, is bundled unmodified as assembly
  resources. Source: https://dejavu-fonts.github.io/ ; binary provenance:
  Ubuntu `fonts-dejavu-core` **2.37-8**. Both required faces are embedded in PDFs;
  neither generation nor viewing needs a platform font installation.
- A custom resolver is installed once before fonts are created. Repeated exports
  reuse it. An already-installed foreign resolver is an explicit integration
  error, never silently replaced. A host that already uses PDFsharp needs resolver
  composition before adding report support. MigraDoc's predefined error font is
  pointed at the bundled family; its default (`Courier New`) is a platform font
  the bundled resolver deliberately cannot supply.
- The initial text contract is printable ASCII/Latin-1 and line/tab separators.
  Other codepoints, controls and soft hyphens fail with the field and codepoint;
  there is no silent missing-glyph substitution. Broader Unicode/shaping support
  is deferred rather than implied by the font's larger character inventory.
- PDFsharp/MigraDoc are MIT-licensed. The actual v6.2.4 license and the Microsoft
  transitive dependency notices are in `OpenNest.Reporting/THIRD-PARTY-NOTICES.txt`.
  Font permission/redistribution terms are in `OpenNest.Reporting/Fonts/LICENSE.txt`
  (Bitstream Vera permission with DejaVu changes in the public domain). Preserve
  both files in `ReportingNotices/` when publishing; embedded fonts do not replace
  the obligation to ship their notices. Package notices are deduplicated, not
  rewritten or shortened.

## Verification

```sh
dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter FullyQualifiedName~Reporting
```

The writer tests read page sizes and content streams with PDFsharp. Text
assertions use Poppler's `pdftotext -bbox` coordinates to rebuild visual rows;
`-layout` can return columns in a different order on Windows. Part cards are
read column by column (`ReportPdf.PartCards`), each ending at its "Plates:" line. They are skipped,
not passed, when poppler-utils is absent. The Windows release workflow installs
an SHA-256-pinned Poppler binary because the runner's built-in `pdftotext` lacks
`-bbox`; no PDF text test is skipped for that reason in a release candidate.
For manual inspection also use `pdfinfo`,
`pdffonts` and `pdftoppm -png` (Linux verification baseline: poppler-utils
24.02.0). Confirm page sizes, quantity rows, page X of Y, embedded fonts, native
vector curves, unfilled holes and visible tab gaps with no connecting stroke.
Pixel inspection supplements geometry assertions; it is not a CNC-validation
result.

Windows compilation is not runtime acceptance. `OpenNest.WinForms.Tests/Forms/NestReportExportTests.cs`
covers enablement, busy/cancel/failure/success adapter behavior, but it only
compiles on Linux. Windows runtime acceptance still owes: exporting a real nest
(and the packaged application, so bundled fonts/notices are verified), checking
page sizes/labels/copies and embedded fonts with `pdffonts`, a tabbed part with a
hole, opening and printing the PDF, and exercising fill/nesting rejection, cancel,
overwrite and invalid-path behavior with job state and any existing destination
unchanged.
