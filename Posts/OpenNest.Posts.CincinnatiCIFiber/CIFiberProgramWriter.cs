using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenNest;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Posts.CincinnatiCIFiber
{
    /// <summary>
    /// Emits the CI Fiber (TF5200) machine program: header, restart jump,
    /// per-sheet / per-part / per-contour blocks and the tail, matching the
    /// structure of the Cincinnati-supplied sample NC:
    ///
    /// <code>
    /// ( &lt;nest name&gt; )
    /// ( CONFIGURATION - CI FIBER 8K )
    /// ( August 20, 2026   01:10 PM )
    /// ( Material = Mild Steel .060 )
    /// V.E.MATERIAL = "MSN" ... V.E.UNIT = 1
    /// G90
    /// L PROGRAMSTART.NC
    /// P3=V.E.R3
    /// $GOTO NP3:
    /// N0:
    ///   ( Sheet number - 1 )
    ///   ... (single-program mode only: /L "L0", pallet change, next sheet)
    ///   ( Part #k ) ( PART:... ) V.E.R4=k
    ///     N&lt;n&gt;: /L "L0" V.E.R3=&lt;n&gt; G0X..Y..
    ///          /L "L2" + G41 | /L "L4" + G42
    ///          G1X..Y..          (linear lead-in)
    ///          /L "L6"           (cut layer on)
    ///          G1/G2/G3 ...
    ///          /L "ZHSOFF"
    ///   ( PART END )
    /// /L "L0"
    /// L PROGRAMEND.NC
    /// M50               (configurable pallet change)
    /// M30
    /// %
    /// </code>
    ///
    /// Coordinates are spaceless (G1X3.706Y47.488); arcs post I/J incremental
    /// from the arc start (TF5200 G162 default). The first motion block after
    /// the G41/G42 selection is always LINEAR (TF5200 §13.2.4.1): contours
    /// without a lead-in move are rejected with a clear error.
    /// </summary>
    public sealed class CIFiberProgramWriter
    {
        private readonly CIFiberPostConfig _config;
        private readonly CIFiberFormatter _fmt;

        public CIFiberProgramWriter(CIFiberPostConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _fmt = new CIFiberFormatter(config.PostedAccuracy);
        }

        /// <summary>The plates that are posted: every plate with parts, in nest order.</summary>
        public static IReadOnlyList<Plate> PostedSheets(Nest nest) =>
            nest?.Plates.Where(p => p.Parts.Count > 0).ToList()
            ?? throw new ArgumentNullException(nameof(nest));

        /// <summary>
        /// Writes every posted sheet into one program, with a pallet change
        /// between sheets and after the last.
        /// </summary>
        public void Write(Nest nest, TextWriter w)
        {
            if (nest == null)
                throw new ArgumentNullException(nameof(nest));

            Write(nest, PostedSheets(nest), 1, w);
        }

        /// <summary>
        /// Writes one sheet as a complete program. Contour labels restart at 1;
        /// <paramref name="sheetNumber"/> only labels the sheet comment.
        /// </summary>
        public void WriteSheet(Nest nest, Plate sheet, int sheetNumber, TextWriter w)
        {
            if (nest == null)
                throw new ArgumentNullException(nameof(nest));
            if (sheet == null)
                throw new ArgumentNullException(nameof(sheet));

            Write(nest, new[] { sheet }, sheetNumber, w);
        }

        /// <summary>
        /// Throws if any sheet exceeds the table, or if a single program would hold
        /// sheets of different sizes (its header carries only one size).
        /// </summary>
        public void Validate(IReadOnlyList<Plate> sheets, bool singleProgram)
        {
            foreach (var sheet in sheets)
                _config.ValidateTableSize(sheet.Size.Length, sheet.Size.Width);

            if (!singleProgram || sheets.Count < 2)
                return;

            var first = sheets[0].Size;
            if (sheets.Any(p => !SameSize(p.Size, first)))
                throw new InvalidOperationException(
                    "The sheets are different sizes, but one program has a single sheet "
                        + "size in its header. Turn on \"One program per sheet\" to post them."
                );
        }

        private static bool SameSize(Size a, Size b) =>
            System.Math.Abs(a.Length - b.Length) < 1e-6 && System.Math.Abs(a.Width - b.Width) < 1e-6;

        private void Write(Nest nest, IReadOnlyList<Plate> sheets, int firstSheetNumber, TextWriter w)
        {
            // Check every sheet before writing anything.
            Validate(sheets, singleProgram: true);

            WriteHeader(nest, sheets.Count > 0 ? sheets[0] : nest.Plates.FirstOrDefault(), w);

            CIFiberFormatter.Line(w, "G90");
            CIFiberFormatter.Line(w, $"L {_config.ProgramStartMacro}");
            CIFiberFormatter.Line(w, "P3=V.E.R3");
            CIFiberFormatter.Line(w, "$GOTO NP3:");
            CIFiberFormatter.Line(w, "N0:");

            var contourNumber = 0;
            for (var s = 0; s < sheets.Count; s++)
            {
                if (s > 0)
                {
                    // Park, then swap pallets before the next sheet.
                    CIFiberFormatter.Line(w, SkippableLine(_config.LayerCancel));
                    WritePalletChange(w);
                }

                CIFiberFormatter.Line(w, $"( Sheet number - {firstSheetNumber + s} )");
                contourNumber = WriteSheet(sheets[s], w, contourNumber);
            }

            WriteTail(w);
        }

        private void WriteHeader(Nest nest, Plate firstSheet, TextWriter w)
        {
            CIFiberFormatter.Line(w, $"( {nest.Name ?? ""} )");
            CIFiberFormatter.Line(w, $"( CONFIGURATION - {_config.ConfigurationName} )");
            CIFiberFormatter.Line(
                w,
                "( "
                    + DateTime.Now.ToString("MMMM d, yyyy   hh:mm tt", CultureInfo.InvariantCulture)
                    + " )"
            );

            var materialName = nest.Material?.Name ?? "";
            var thickness = _fmt.Fixed(nest.Thickness);
            if (thickness.StartsWith("0"))
                thickness = thickness.Substring(1); // sample posts ".060"
            CIFiberFormatter.Line(w, $"( Material = {materialName} {thickness} )".TrimEnd());

            var code = _config.ResolveMaterialCode(materialName);
            CIFiberFormatter.Line(w, $"V.E.MATERIAL = \"{code}\"");
            CIFiberFormatter.Line(w, $"V.E.THICKNESS = {_fmt.Fixed(nest.Thickness)}");

            // A single program holding several sheets still carries only the
            // first sheet's size (the machine sample has one sheet).
            var xSize = firstSheet?.Size.Length ?? 0.0;
            var ySize = firstSheet?.Size.Width ?? 0.0;
            CIFiberFormatter.Line(w, $"V.E.X_SIZE = {_fmt.Fixed(xSize)}");
            CIFiberFormatter.Line(w, $"V.E.Y_SIZE = {_fmt.Fixed(ySize)}");

            if (_config.EmitSheetWeight)
                CIFiberFormatter.Line(w, "V.E.SHEET_WEIGHT = 0");

            var unit = nest.Units == Units.Millimeters
                ? _config.MetricUnitCode
                : _config.InchUnitCode;
            CIFiberFormatter.Line(w, $"V.E.UNIT = {unit}");
        }

        private void WriteTail(TextWriter w)
        {
            CIFiberFormatter.Line(w, SkippableLine(_config.LayerCancel));
            CIFiberFormatter.Line(w, $"L {_config.ProgramEndMacro}");
            WritePalletChange(w);
            CIFiberFormatter.Line(w, "M30");
            CIFiberFormatter.Line(w, "%");
        }

        private void WritePalletChange(TextWriter w)
        {
            if (!string.IsNullOrWhiteSpace(_config.PalletChangeCode))
                CIFiberFormatter.Line(w, _config.PalletChangeCode.Trim());
        }

        private int WriteSheet(Plate plate, TextWriter w, int contourNumber)
        {
            // Cut-offs run last: severing the sheet first would free the
            // skeleton before the parts are cut (matches the CL post).
            var ordered = plate
                .Parts.Where(p => !p.BaseDrawing.IsCutOff)
                .Concat(plate.Parts.Where(p => p.BaseDrawing.IsCutOff));

            var partNumber = 0;
            foreach (var part in ordered)
            {
                partNumber++;
                contourNumber = WritePart(part, partNumber, w, contourNumber);
            }

            return contourNumber;
        }

        private int WritePart(Part part, int partNumber, TextWriter w, int contourNumber)
        {
            CIFiberFormatter.Line(w, $"( Part #{partNumber} )");

            var partComment = PartName(part);
            CIFiberFormatter.Line(w, $"( PART:{partComment} )");

            CIFiberFormatter.Line(w, $"V.E.R4={partNumber}");

            var contours = CIFiberContourExtractor.Extract(part);
            if (_config.SkipScribe)
                contours = contours.Where(c => !IsScribeContour(c)).ToList();

            var isCutOff = part.BaseDrawing.IsCutOff;
            foreach (var contour in contours)
            {
                contourNumber++;
                if (isCutOff)
                    WriteCutOffContour(contour, contourNumber, w);
                else
                    WriteContour(contour, contourNumber, w);
            }

            CIFiberFormatter.Line(w, "( PART END )");
            return contourNumber;
        }

        private void WriteContour(CIFiberContour contour, int contourNumber, TextWriter w)
        {
            var isExterior = contour.IsExterior || CIFiberWinding.IsExterior(contour);

            CIFiberFormatter.Line(w, $"N{contourNumber}:");
            CIFiberFormatter.Line(w, SkippableLine(_config.LayerCancel));
            CIFiberFormatter.Line(w, $"V.E.R3={contourNumber}");
            CIFiberFormatter.Line(w, $"G0X{Fmt(contour.Pierce.X)}Y{Fmt(contour.Pierce.Y)}");

            if (contour.LeadIn == null)
                throw new InvalidOperationException(
                    $"Contour {contourNumber} has no lead-in move. TF5200 13.2.4.1 "
                        + "requires a LINEAR motion block immediately after G41/G42 "
                        + "selection; assign lead-ins before posting."
                );

            if (contour.LeadIn is not LinearMove)
                throw new InvalidOperationException(
                    $"Contour {contourNumber} has an arc lead-in. TF5200 13.2.4.1 "
                        + "requires the first motion block after G41/G42 selection "
                        + "to be LINEAR."
                );

            CIFiberFormatter.Line(
                w,
                SkippableLine(
                    isExterior ? _config.LayerExteriorLeadin : _config.LayerInteriorLeadin
                )
            );
            CIFiberFormatter.Line(
                w,
                isExterior ? "G42" : "G41"
            );

            var prev = contour.LeadIn.EndPoint;
            CIFiberFormatter.Line(
                w,
                $"G1X{Fmt(contour.LeadIn.EndPoint.X)}Y{Fmt(contour.LeadIn.EndPoint.Y)}"
            );

            // Additional lead-layer moves (e.g. CleanHole arc) still run
            // before the cut layer turns on.
            foreach (var extra in contour.LeadInExtra)
            {
                CIFiberFormatter.Line(w, FormatMotion(extra, prev));
                prev = extra.EndPoint;
            }

            CIFiberFormatter.Line(w, SkippableLine(_config.LayerCut));

            foreach (var cut in contour.Cuts)
            {
                CIFiberFormatter.Line(w, FormatMotion(cut, prev));
                prev = cut.EndPoint;
            }

            CIFiberFormatter.Line(w, SkippableLine(_config.LayerCutEnd));
        }

        /// <summary>
        /// Cut-offs are open straight lines with no lead-in: the line is the
        /// beam centreline (CutOffSettings clearance already allows for kerf)
        /// and an open line has no inside/outside, so no G41/G42 is selected
        /// and the §13.2.4.1 linear-lead-in rule does not apply. The exterior
        /// lead layer still runs so the pierce sequence matches a perimeter.
        /// </summary>
        private void WriteCutOffContour(CIFiberContour contour, int contourNumber, TextWriter w)
        {
            CIFiberFormatter.Line(w, $"N{contourNumber}:");
            CIFiberFormatter.Line(w, SkippableLine(_config.LayerCancel));
            CIFiberFormatter.Line(w, $"V.E.R3={contourNumber}");
            CIFiberFormatter.Line(w, $"G0X{Fmt(contour.Pierce.X)}Y{Fmt(contour.Pierce.Y)}");
            CIFiberFormatter.Line(w, SkippableLine(_config.LayerExteriorLeadin));
            CIFiberFormatter.Line(w, SkippableLine(_config.LayerCut));

            var prev = contour.Pierce;
            if (contour.LeadIn != null)
            {
                CIFiberFormatter.Line(w, FormatMotion(contour.LeadIn, prev));
                prev = contour.LeadIn.EndPoint;
            }

            foreach (var motion in contour.LeadInExtra.Concat(contour.Cuts))
            {
                CIFiberFormatter.Line(w, FormatMotion(motion, prev));
                prev = motion.EndPoint;
            }

            CIFiberFormatter.Line(w, SkippableLine(_config.LayerCutEnd));
        }

        /// <summary>Source file name without extension, else the drawing name.</summary>
        private static string PartName(Part part)
        {
            var name = part.BaseDrawing?.Name ?? "";
            var source = part.BaseDrawing?.Source?.Path;
            if (!string.IsNullOrEmpty(source))
                return Path.GetFileNameWithoutExtension(source);

            return name;
        }

        private static bool IsScribeContour(CIFiberContour contour)
        {
            var hasAny = false;
            foreach (var cut in contour.Cuts)
            {
                var layer = LayerOf(cut);
                if (layer != LayerType.Scribe)
                    return false;
                hasAny = true;
            }
            return hasAny;
        }

        private static LayerType LayerOf(Motion motion) =>
            motion switch
            {
                LinearMove l => l.Layer,
                ArcMove a => a.Layer,
                _ => LayerType.Cut,
            };

        private string Fmt(double value) => _fmt.Coord(value);

        /// <summary>
        /// One motion line: G0/G1/G2/G3 with spaceless X/Y, plus I/J for arcs.
        /// I/J are incremental from the arc START (TF5200 G162/basic default,
        /// verified against the machine sample): center minus arc start point.
        /// </summary>
        private string FormatMotion(Motion motion, Vector arcStart)
        {
            var prefix = MovePrefix(motion);
            var line = $"{prefix}X{Fmt(motion.EndPoint.X)}Y{Fmt(motion.EndPoint.Y)}";

            if (motion is ArcMove arc)
            {
                var i = arc.CenterPoint.X - arcStart.X;
                var j = arc.CenterPoint.Y - arcStart.Y;
                line += $"I{Fmt(i)}J{Fmt(j)}";
            }

            return line;
        }

        private static string SkippableLine(string macroName) => $"/L \"{macroName}\"";

        private static string MovePrefix(Motion motion) =>
            motion switch
            {
                RapidMove => "G0",
                ArcMove a => a.Rotation == RotationType.CW ? "G2" : "G3",
                _ => "G1",
            };
    }
}
