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
CNC output. Opening and printing use any normal PDF viewer; the report includes
no printer controls and there are no persisted report settings or templates.

The same two calls back the command and any future integration:

```csharp
var snapshot = NestReportBuilder.Capture(nest, DateTimeOffset.Now);
NestPdfWriter.Write(snapshot, destinationPath);
```

Reporting does not select a post, generate CNC, refresh drawing quantities, change
the selected plate, or certify that a layout passed geometry/pre-post checks.

## Snapshot contract

- IDs (`R001`, ...) are local to the report. Visit non-cutoff placements in plate
  and part order, then append unseen demanded drawings ordered by ordinal name and
  source path. Identity is by reference: distinct same-named drawings stay distinct,
  including placed drawings absent from the name-keyed drawing collection.
- Recount nested quantities from placements times sheet copies exactly once using
  checked wide integers. Include unplaced demand; report shortage and extra
  separately. Cached `Quantity.Nested` is not authoritative.
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

## Layout, pagination and dense-label fallback

An empty/demand-only job, a single plate, or many plates and drawings all
produce one document: a Letter portrait summary followed by one Letter
landscape section per plate, every page carrying a repeated header and
"Page X of Y". Long notes, long drawing names, many drawings and many parts
per sheet paginate naturally: MigraDoc continues the Plates/Parts tables and
each plate's part table across pages with the heading row repeated, and notes
flow as an ordinary paragraph. No row, table or note text is ever dropped or
truncated to fit a page.

Stock sizes read **width x length** in job units. Plate tables use **Quantity**
and **Parts per plate**; the plate quantity multiplies per-plate part counts for
the totals.

Each part ID is centered on its part's pole of inaccessibility (`PolyLabel`,
the same method `PlateView`'s `LayoutPart` uses), computed on a
placement-independent quantized copy so identical parts always get the
identical label position and the label naturally clears a central hole. When
an ID cannot sit legibly inside its own material at overview scale (for
example, a cluster of tiny repeated parts), the plate gains a lettered
(rows)/numbered (columns) map grid drawn beneath the sheet, and only the
crowded cells get a zoomed, framed detail page listing that cell's real
coordinates. Detail-cell outlines use a long dash-dot stroke, distinct from
the shorter dashed scrap-cutoff stroke and the dotted grid lines. IDs are
never shrunk below 7 pt or silently dropped.

The writer still rejects, with `NotSupportedException` and before touching
the destination:

- a page header (nest name plus plate/material line) needing more than 3
  wrapped lines;
- a table cell needing more than 20 wrapped lines;
- a part ID that cannot be placed legibly even in the most zoomed supported
  detail view, or a plate that would need more than 24 detail views to label
  every part — named with the plate, part index and ID.

Summary thumbnails and the sheet diagram are vector paths, never raster images.
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

Advanced timing, cutting distances, pierce counts, weights, costs, gas use, and
machine/NC identity are intentionally omitted until their semantics are verified.
The PDF can be opened or printed through a normal PDF viewer; its fitted diagram
is not a dimensioned cutting drawing.

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
`-layout` can return columns in a different order on Windows. They are skipped,
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
