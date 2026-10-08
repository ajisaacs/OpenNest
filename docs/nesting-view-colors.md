# Nesting-view colors

Choose **Tools > Options > Color scheme > Workshop**, then **Save** for a
high-contrast per-drawing view:

- Generated fills: hue advances by 137.508 degrees per drawing (skipping the
  green band reserved for etch) and cycles through eight saturation/lightness
  tiers on an irrational stride, so parts sharing a sheet stay far apart in
  color. All copies of a drawing keep its color.
- Thin dark gray `#303030` cut outlines.
- Bright green `#00FF00` etch strokes, independent of the part fill, so marks
  that are not cuts cannot be mistaken for part outlines.
- Pale gray `#F2F2F2` sheet and neutral gray background.

The generator continues beyond the twelve preview samples instead of wrapping
through a short palette. It maximizes separation between consecutive drawing
indices, not spatial neighbors; very large jobs can still place similar colors
side by side. This is not a colorblind-safe or perceptually uniform palette. Selected parts retain the
standard translucent blue highlight; etch and cut colors stay unchanged.
Classic, Pastel and Dark remain available; saved scheme choices are not changed.
Applying a scheme recolors drawings in currently open nests. Drawings may retain
custom or saved fill colors; open a saved nest before applying Workshop to recolor it.

In plate views and drawing thumbnails, scribe/etch motions have a separate stroke
from material cuts. A closed etched circle or lettering does not make a hole in
the displayed part fill. An etch-only part remains selectable by its closed
mark in the plate view; etch lines still do not become cut material. Selection
changes the part highlight, not the etch color.
Lead-in/out strokes keep their existing orange-red appearance. Preview placements
show etches too. A cut coincident with an etch is drawn over the etch, so it is not
hidden by the mark.

This is a display aid, not a geometry classifier or machining-safety certificate.
Only motions classified as `Scribe` receive etch styling; a line incorrectly
classified as `Cut` remains a cut line. Drawing geometry, layer classifications,
cutting plans and post-processor output are not changed by the renderer. The CAD
conversion/program-editing views retain their own source/classification colors.

## Custom scheme files

Scheme JSON files in `Schemes/` beside the application may specify `etchColor`
and `partOutlineColor` as `#RRGGBB`. Optional `selectedPartColor` specifies an opaque
selection fill; missing or null retains the legacy translucent selection highlight.
Missing `etchColor` uses the bright green etch default.
Missing or null `partOutlineColor` retains the legacy darker-than-fill outline.
Optional `useGoldenAngleColors: true` selects generated drawing colors; missing
or false keeps the explicit `partColors` palette and its legacy cycling behavior.
Workshop tests check the first 1,000 generated fills stay far in RGB distance
from both the dark cut outline and the bright green etch stroke, and that no
fill hue falls in the etch green band. Selected rendering has separate bitmap tests.
Custom or previously saved fills are not automatically contrast-corrected;
apply Workshop after loading the nest to recolor its drawings. Existing scheme
names, saved choices, custom palettes and geometry remain unchanged.

## Verification

On Windows, run:

```powershell
dotnet test OpenNest.WinForms.Tests/OpenNest.WinForms.Tests.csproj -c Release --filter FullyQualifiedName~EtchDisplayTests
```

Then inspect an actual nest with both cuts and etches: select/deselect and move a
part, zoom in/out, inspect drawing thumbnails, and switch away from Workshop and
back. Confirm the marks remain readable, closed marks stay filled and real holes
remain holes. Automated bitmap tests verify the rendering rules, not an operator's
assessment of readability on a particular display.
