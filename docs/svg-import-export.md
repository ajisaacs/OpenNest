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
- Layers come from `data-layer`, an Inkscape layer label, or the `id` of a top-level group (Illustrator). `BEND`, `ETCH`, `SCRIBE`, `RAPID` and `PLATE` are dropped, so an exported plate reads back as its parts.
- The file's DTD may parse, because older Illustrator files declare namespaces in one, but external fetches are off and entity expansion is capped.
- There is no bend detection: it reads a CAD document, which an SVG does not have.

## What export writes

`ExportProgram`, `ExportPlate` and `ExportEntities` write one `<g data-layer>` per layer. `width` and `height` are in the given units and match the `viewBox`, so one user unit is one drawing unit and the file needs no scale prompt. Arcs stay `A` commands. The plate outline and edge spacing go on `PLATE`. Plate export leaves out the rapids DXF draws between parts.
