# Nest report PDF library

The first delivery is a bounded, cross-platform library slice, not yet a desktop
menu command or a general report layout engine. Capture a stable `Nest` on its
owning thread, then render only the detached snapshot:

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

## Initial layout and safety limits

Slice 1 supports an empty/demand-only job or one plate layout: a Letter portrait
summary page and a Letter landscape plate page, each with "Page X of Y". The
writer rejects, with `NotSupportedException` and before touching the destination:

- more than one distinct plate layout;
- a summary (job fields, plate list, part rows with thumbnails) longer than one page;
- a plate page (header, diagram, part table) longer than one page;
- a part ID label that does not fit inside its part's fitted bounds at 7 pt, or
  that overlaps another label.

It never shrinks text, truncates rows or drops labels to make a layout fit.
Multi-plate pagination, dense-label callouts and the desktop command belong to
subsequent slices. Labels are centered on the part's bounds, which can place them
inside a central hole; smarter placement is part of the dense-label work.

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
calling the library.

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
assertions extract with Poppler's `pdftotext -layout` and are skipped, not
passed, when poppler-utils is absent. For manual inspection also use `pdfinfo`,
`pdffonts` and `pdftoppm -png` (Linux verification baseline: poppler-utils
24.02.0). Confirm page sizes, quantity rows, page X of Y, embedded fonts, native
vector curves, unfilled holes and visible tab gaps with no connecting stroke.
Pixel inspection supplements geometry assertions; it is not a CNC-validation
result.

Windows compilation is not runtime acceptance. The later desktop adapter must be
tested on Windows for busy-operation guards, cancellation, overwrite handling,
unchanged job state and PDF viewing/printing, including the packaged application.
