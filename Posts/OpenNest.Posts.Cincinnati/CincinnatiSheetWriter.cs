using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Posts.Cincinnati;

/// <summary>
/// Emits one Cincinnati-format sheet subprogram per plate.
/// Supports two modes: inline features (default) or M98 sub-program calls per part.
/// </summary>
public sealed class CincinnatiSheetWriter
{
    private readonly CincinnatiPostConfig _config;
    private readonly ProgramVariableManager _vars;
    private readonly CoordinateFormatter _fmt;
    private readonly CincinnatiFeatureWriter _featureWriter;
    private readonly IReadOnlyDictionary<Program, int> _holeSubprograms;

    public CincinnatiSheetWriter(
        CincinnatiPostConfig config,
        ProgramVariableManager vars,
        IReadOnlyDictionary<Program, int> holeSubprograms = null
    )
    {
        _config = config;
        _vars = vars;
        _fmt = new CoordinateFormatter(config.PostedAccuracy);
        _featureWriter = new CincinnatiFeatureWriter(config);
        _holeSubprograms = holeSubprograms;
    }

    /// <summary>
    /// Writes a complete sheet subprogram for the given plate.
    /// </summary>
    /// <param name="cutLibrary">Resolved G89 library file for cut operations.</param>
    /// <param name="etchLibrary">Resolved G89 library file for etch operations.</param>
    /// <param name="partSubprograms">
    /// Optional mapping of parts (by reference) to their posted part sub-programs.
    /// When provided, mapped non-cutoff parts are emitted as M98 calls instead of inline features.
    /// </param>
    public void Write(
        TextWriter w,
        Plate plate,
        string nestName,
        int layoutIndex,
        int subNumber,
        string cutLibrary,
        string etchLibrary,
        IReadOnlyDictionary<Part, PartSubprogram> partSubprograms = null,
        Dictionary<(int drawingId, string varName), int> userVarMapping = null
    )
    {
        if (plate.Parts.Count == 0)
            return;

        var width = plate.Size.Width;
        var length = plate.Size.Length;
        var sheetDiagonal = System.Math.Sqrt(width * width + length * length);
        var varDeclSub = _config.VariableDeclarationSubprogram;
        var partCount = plate.Parts.Count(p => !p.BaseDrawing.IsCutOff);

        // 1. Sheet header
        w.WriteLine("(*****************************************************)");
        w.WriteLine($"( START OF {nestName}.{layoutIndex:D3} )");
        w.WriteLine($":{subNumber}");
        w.WriteLine($"( Layout {layoutIndex} )");
        w.WriteLine($"( SHEET NAME = {_fmt.FormatCoord(width)} X {_fmt.FormatCoord(length)} )");
        w.WriteLine($"( Total parts on sheet = {partCount} )");
        w.WriteLine(
            $"#{_config.SheetWidthVariable}={_fmt.FormatCoord(width)} (SHEET WIDTH FOR CUTOFFS)"
        );
        w.WriteLine(
            $"#{_config.SheetLengthVariable}={_fmt.FormatCoord(length)} (SHEET LENGTH FOR CUTOFFS)"
        );

        // 2. Coordinate setup
        w.WriteLine("M42");
        w.WriteLine("N10000");
        w.WriteLine("G92 X#5021 Y#5022");
        if (!string.IsNullOrEmpty(cutLibrary))
            w.WriteLine($"G89 P{cutLibrary}");
        w.WriteLine($"M98 P{varDeclSub} (Variable Declaration)");
        w.WriteLine("G90");
        w.WriteLine("M47");
        if (!string.IsNullOrEmpty(cutLibrary))
            w.WriteLine($"G89 P{cutLibrary}");

        // The operator edits this target to restart at a feature. It names the first
        // feature's line number, which exists only when line numbers are posted.
        if (_config.UseLineNumbers)
            w.WriteLine($"GOTO{_config.FeatureLineNumberStart}( Goto Feature )");

        // 3. Plate order is the cut sequence, cut-offs included: manual, cut-first
        //    and automatic sequencing all set it, so the post must not reorder.
        var items = BuildItems(plate.Parts, partSubprograms);

        // 4. Emit parts
        WriteItems(w, items, cutLibrary, etchLibrary, sheetDiagonal, width, length, userVarMapping);

        // 5. Footer
        w.WriteLine("M42");
        if (_config.PalletExchange != PalletMode.None)
            w.WriteLine("M50");
        w.WriteLine($"M99 (END OF {nestName}.{layoutIndex:D3})");
    }

    /// <summary>
    /// One posted step of the sheet: a part feature, a hole call or a part sub-program
    /// call. Points are in sheet coordinates.
    /// </summary>
    private sealed class SheetItem
    {
        public Part Part { get; init; }
        public List<ICode> Codes { get; init; }
        public bool IsEtch { get; init; }
        public bool IsExterior { get; init; }
        public PartSubprogram Subprogram { get; init; }

        /// <summary>First rapid target: the sub-program corner, else the first pierce.</summary>
        public Vector Entry { get; init; }

        public Vector Start { get; init; }
        public Vector End { get; init; }

        /// <summary>Where the tool leaves from: back at the corner after a sub-program call.</summary>
        public Vector Exit { get; init; }
    }

    private static List<SheetItem> BuildItems(
        IEnumerable<Part> parts,
        IReadOnlyDictionary<Part, PartSubprogram> partSubprograms
    )
    {
        var items = new List<SheetItem>();
        foreach (var part in parts)
        {
            if (
                partSubprograms != null
                && !part.BaseDrawing.IsCutOff
                && partSubprograms.TryGetValue(part, out var sub)
            )
            {
                var subFeatures = FeatureUtils.ClassifyAndOrder(
                    FeatureUtils.SplitByRapids(sub.Program.Codes)
                );
                if (subFeatures.Count == 0)
                    continue;

                var corner = part.Location + sub.Corner;
                items.Add(
                    new SheetItem
                    {
                        Part = part,
                        Subprogram = sub,
                        Entry = corner,
                        Start = corner + FeatureUtils.StartPoint(subFeatures[0].codes),
                        End = corner + FeatureUtils.EndPoint(subFeatures[^1].codes),
                        Exit = corner,
                    }
                );
                continue;
            }

            var features = FeatureUtils.SplitAndClassify(part);
            var exterior = FeatureUtils.FindExterior(features);
            for (var f = 0; f < features.Count; f++)
            {
                var (codes, isEtch) = features[f];
                var start = part.Location + FeatureUtils.StartPoint(codes);
                var end = part.Location + FeatureUtils.EndPoint(codes);
                items.Add(
                    new SheetItem
                    {
                        Part = part,
                        Codes = codes,
                        IsEtch = isEtch,
                        IsExterior = exterior[f],
                        Entry = start,
                        Start = start,
                        End = end,
                        Exit = end,
                    }
                );
            }
        }
        return items;
    }

    private void WriteItems(
        TextWriter w,
        List<SheetItem> items,
        string cutLibrary,
        string etchLibrary,
        double sheetDiagonal,
        double plateWidth,
        double plateLength,
        Dictionary<(int drawingId, string varName), int> userVarMapping
    )
    {
        var lastPartName = "";
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var part = item.Part;
            var partName = part.BaseDrawing.Name;
            var isFirstFeatureOfPart = partName != lastPartName;
            var next = i + 1 < items.Count ? items[i + 1] : null;
            var isLastFeature = next == null;

            // A head raise belongs before the rapid that leaves this item; its travel
            // decides whether that raise is the safety raise.
            double? rapidDistance =
                next == null
                    ? null
                    : item.End.DistanceTo(item.Exit)
                        + item.Exit.DistanceTo(next.Entry)
                        + next.Entry.DistanceTo(next.Start);
            lastPartName = partName;

            if (item.Subprogram != null)
            {
                WriteSubprogramCall(
                    w,
                    item,
                    i,
                    partName,
                    rapidDistance,
                    isLastFeature
                );
                continue;
            }

            var codes = item.Codes;

            // SubProgramCall features are emitted as M98 hole calls
            if (codes.Count == 1 && codes[0] is SubProgramCall holeCall)
            {
                CincinnatiHoleCallWriter.Write(
                    w,
                    holeCall,
                    part.Location,
                    i,
                    isLastFeature,
                    _config,
                    _fmt,
                    _holeSubprograms,
                    isFirstFeatureOfPart ? partName : null,
                    rapidDistance
                );
                continue;
            }

            var ctx = new FeatureContext
            {
                Codes = codes,
                FeatureNumber = FeatureNumber(i),
                PartName = partName,
                IsFirstFeatureOfPart = isFirstFeatureOfPart,
                IsLastFeatureOnSheet = isLastFeature,
                IsExteriorFeature = item.IsExterior,
                IsEtch = item.IsEtch,
                LibraryFile = item.IsEtch ? etchLibrary : cutLibrary,
                CutDistance = FeatureUtils.ComputeCutDistance(codes),
                SheetDiagonal = sheetDiagonal,
                RapidDistance = rapidDistance,
                PartLocation = part.Location,
                UserVariableMapping = userVarMapping,
                DrawingId = part.BaseDrawing.Id,
                IsCutOff = part.BaseDrawing.IsCutOff,
                PlateWidth = plateWidth,
                PlateLength = plateLength,
            };

            _featureWriter.Write(w, ctx);
        }
    }

    private int FeatureNumber(int index) =>
        index == 0 ? _config.FeatureLineNumberStart : 1000 + index + 1;

    /// <summary>
    /// Calls a part sub-program in a G92 frame at the part corner (§1.92). After the
    /// call the head is raised, the tool rapids back to local 0,0 and G92 gives that
    /// point its sheet coordinates again, so the frame is restored where the tool really
    /// is. The last part skips the restore: nothing moves before the next sheet
    /// re-anchors with G92 X#5021 Y#5022.
    /// </summary>
    private void WriteSubprogramCall(
        TextWriter w,
        SheetItem item,
        int index,
        string partName,
        double? rapidDistance,
        bool isLastPart
    )
    {
        var x = _fmt.FormatCoord(item.Entry.X);
        var y = _fmt.FormatCoord(item.Entry.Y);

        // Rapid to the sub-program origin (the part's toolpath corner)
        var sb = new StringBuilder();
        if (_config.UseLineNumbers)
            sb.Append($"N{FeatureNumber(index)} ");
        sb.Append($"G0 X{x} Y{y}");
        w.WriteLine(sb.ToString());

        // Part name comment
        w.WriteLine(CoordinateFormatter.Comment($"PART: {partName}"));

        // Set local coordinate system at part position
        w.WriteLine("G92 X0 Y0");

        // Call part sub-program
        w.WriteLine($"M98 P{item.Subprogram.Number} ({partName})");

        if (isLastPart)
            return;

        // One head raise covers the rapid back and the rapid on. The part is complete,
        // so the raise follows the exterior M47 mode.
        var raise = CincinnatiHeadRaise.Line(_config, true, rapidDistance);
        if (raise != null)
            w.WriteLine(raise);

        // Restore the sheet coordinate system at the corner
        w.WriteLine("G0 X0 Y0");
        w.WriteLine($"G92 X{x} Y{y}");
    }
}
