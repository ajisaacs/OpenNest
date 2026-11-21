# SVG import and export

`OpenNest.IO.Svg` reads and writes SVG alongside DXF and DWG. Everything goes through `CadImporter`, so an SVG drawing behaves like any other import after loading. `.svg` is not in `DrawingFileIndex`: BOM imports still look for DXF and DWG only.

## Scale

A DXF's numbers are taken as drawing units. An SVG's are not, so import converts to `CadImportOptions.DrawingUnits` (the nest's units in the desktop app) and never guesses.

The root element states a real-world size only when `width` or `height` uses an absolute unit: `in`, `cm`, `mm`, `Q`, `pt` or `pc`. Then:

- With a `viewBox`, inches per user unit is the page size over the view box size. Mismatched aspect ratios fit uniformly, as browsers do, unless `preserveAspectRatio` starts with `none`.
- Without one, a user unit is a CSS pixel, 1/96 inch.

Everything else does not count: a missing size, percentages and `px` or bare numbers. `px` is excluded on purpose, because a CSS pixel is 1/96 inch but Illustrator writes px at 72 per inch.

With no stated size, `CadImportOptions.SvgUserUnitsPerInch` supplies the scale, and without it the import throws `InvalidDataException`. A stated size always wins over that option. The desktop app asks with `SvgScaleForm`, which shows the size each choice produces; the console takes `--svg-units-per-inch` (`--cad-units mm` sets the drawing units) and MCP's `import_dxf` takes `svgUnitsPerInch`.

## What import reads

Import takes path centerlines: `path`, `rect` (with rounded corners), `circle`, `ellipse`, `line`, `polyline`, `polygon`, `g` and `use`. Stroke widths and fills are ignored. Transforms are applied before an entity type is chosen, so a circle under a non-uniform scale becomes an ellipse.

- SVG's Y axis points down, so import negates Y. The view box origin is not subtracted, which keeps OpenNest's own exports at their original coordinates.
- Circular arcs stay arcs. Ellipses go through `EllipseConverter`, and Bézier curves are sampled and fitted by `SplineConverter`, both within 0.001 in or 0.025 mm.
- Malformed path data fails the import instead of importing part of a shape.
- Elements with `display` none or `visibility` hidden or collapse are skipped, as are definition elements such as `defs` and `clipPath`. `<style>` sheets are not read, so CSS-class hiding is not honored.
- Text, images, nested `svg`, `use` of a `symbol`, and shapes sized in percentages are skipped and listed in `CadImportResult.Warnings`.
- Layers come from `data-layer`, an Inkscape layer label, or the `id` of a top-level group (Illustrator). `ETCH`, `SCRIBE`, `RAPID` and `PLATE` are dropped, so an exported plate reads back as its parts.
- The file's DTD may parse, because older Illustrator files declare namespaces in one, but external fetches are off and entity expansion is capped.
- An element's `<title>` (not the root's) is kept in `CadImportResult.Labels` as a name for the region its geometry covers. Labels never change geometry; they name the parts when the CAD Converter [separates a file](separate-parts.md).
- Each `line` on a `BEND` layer is a bend, as a center line on a DXF's `BEND` layer is; nothing else there is read. Its `data-bend-direction` (`up` or `down`), `data-bend-angle` (degrees), `data-bend-radius` (scaled like the line) and `data-bend-note` fill in the bend when present. `CadImportOptions.DetectBends` false ignores them; the detector choice does not apply.

## What export writes

`ExportProgram`, `ExportPlate` and `ExportEntities` write one `<g data-layer>` per layer; the `ExportEntities` overload that takes bends adds a `BEND` group with each bend's `line` and `data-bend-*` attributes, plus its note as text for people. `width` and `height` are in the given units and match the `viewBox`, so one user unit is one drawing unit and the file needs no scale prompt. Arcs stay `A` commands. The plate outline and edge spacing go on `PLATE`. Plate export leaves out the rapids DXF draws between parts.

## CAD Converter export

**Export...** writes the current drawing with `CadExporter`: DXF R14 to 2018, DWG 2004 to 2018 or SVG. Entities keep their layers. Bends go on `BEND` as `CENTER` lines with an MText note such as `UP 90° R 0.06`, the form `SolidWorksBendDetector` reads, so importing the file again gives back the same bends; a line picked as a bend is written only as that bend. Etch marks go on `ETCH`, which import drops and makes again from the bends. Files before 2007 write non-ASCII note characters as `\U+XXXX` escapes. ACadSharp cannot write DWG 2007, and its DWG R14 and 2000 output loses entities and layers, so those are not offered.
