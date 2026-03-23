using System;
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

    public CincinnatiSheetWriter(CincinnatiPostConfig config, ProgramVariableManager vars)
    {
        _config = config;
        _vars = vars;
        _fmt = new CoordinateFormatter(config.PostedAccuracy);
        _featureWriter = new CincinnatiFeatureWriter(config);
    }

    /// <summary>
    /// Writes a complete sheet subprogram for the given plate.
    /// </summary>
    /// <param name="cutLibrary">Resolved G89 library file for cut operations.</param>
    /// <param name="etchLibrary">Resolved G89 library file for etch operations.</param>
    /// <param name="partSubprograms">
    /// Optional mapping of (drawingId, rotationKey) to sub-program number.
    /// When provided, non-cutoff parts are emitted as M98 calls instead of inline features.
    /// </param>
    public void Write(TextWriter w, Plate plate, string nestName, int sheetIndex, int subNumber,
        string cutLibrary, string etchLibrary,
        Dictionary<(int, long), int> partSubprograms = null)
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
        w.WriteLine($"( START OF {nestName}.{sheetIndex:D3} )");
        w.WriteLine($":{subNumber}");
        w.WriteLine($"( Sheet {sheetIndex} )");
        w.WriteLine($"( Layout {sheetIndex} )");
        w.WriteLine($"( SHEET NAME = {_fmt.FormatCoord(length)} X {_fmt.FormatCoord(width)} )");
        w.WriteLine($"( Total parts on sheet = {partCount} )");
        w.WriteLine($"#{_config.SheetWidthVariable}={_fmt.FormatCoord(width)} (SHEET WIDTH FOR CUTOFFS)");
        w.WriteLine($"#{_config.SheetLengthVariable}={_fmt.FormatCoord(length)} (SHEET LENGTH FOR CUTOFFS)");

        // 2. Coordinate setup
        w.WriteLine("M42");
        w.WriteLine("N10000");
        w.WriteLine("G92 X#5021 Y#5022");
        if (!string.IsNullOrEmpty(cutLibrary))
            w.WriteLine($"G89 P {cutLibrary}");
        w.WriteLine($"M98 P{varDeclSub} (Variable Declaration)");
        w.WriteLine("G90");
        w.WriteLine("M47");
        if (!string.IsNullOrEmpty(cutLibrary))
            w.WriteLine($"G89 P {cutLibrary}");
        w.WriteLine("GOTO1( Goto Feature )");

        // 3. Order parts: non-cutoff sorted by Bottom then Left, cutoffs last
        var nonCutoffParts = plate.Parts
            .Where(p => !p.BaseDrawing.IsCutOff)
            .OrderBy(p => p.Bottom)
            .ThenBy(p => p.Left)
            .ToList();

        var cutoffParts = plate.Parts
            .Where(p => p.BaseDrawing.IsCutOff)
            .ToList();

        var allParts = nonCutoffParts.Concat(cutoffParts).ToList();

        // 4. Emit parts
        if (partSubprograms != null)
            WritePartsWithSubprograms(w, allParts, cutLibrary, etchLibrary, sheetDiagonal, partSubprograms);
        else
            WritePartsInline(w, allParts, cutLibrary, etchLibrary, sheetDiagonal);

        // 5. Footer
        w.WriteLine("M42");
        w.WriteLine("G0 X0 Y0");
        if (_config.PalletExchange != PalletMode.None)
            w.WriteLine($"N{sheetIndex + 1} M50");
        w.WriteLine($"M99 (END OF {nestName}.{sheetIndex:D3})");
    }

    private void WritePartsWithSubprograms(TextWriter w, List<Part> allParts,
        string cutLibrary, string etchLibrary, double sheetDiagonal,
        Dictionary<(int, long), int> partSubprograms)
    {
        var lastPartName = "";
        var featureIndex = 0;

        for (var p = 0; p < allParts.Count; p++)
        {
            var part = allParts[p];
            var partName = part.BaseDrawing.Name;
            var isNewPart = partName != lastPartName;
            var isSafetyHeadraise = isNewPart && lastPartName != "";
            var isLastPart = p == allParts.Count - 1;

            var key = CincinnatiPartSubprogramWriter.SubprogramKey(part);
            partSubprograms.TryGetValue(key, out var subNum);
            var hasSubprogram = !part.BaseDrawing.IsCutOff && subNum != 0;

            if (hasSubprogram)
            {
                WriteSubprogramCall(w, part, subNum, featureIndex, partName,
                    isSafetyHeadraise, isLastPart);
                featureIndex++;
            }
            else
            {
                // Inline features for cutoffs or parts without sub-programs
                var features = SplitAndOrderFeatures(part);
                for (var f = 0; f < features.Count; f++)
                {
                    var (codes, isEtch) = features[f];
                    var featureNumber = featureIndex == 0
                        ? _config.FeatureLineNumberStart
                        : 1000 + featureIndex + 1;

                    var isLastFeature = isLastPart && f == features.Count - 1;
                    var cutDistance = ComputeCutDistance(codes);

                    var ctx = new FeatureContext
                    {
                        Codes = codes,
                        FeatureNumber = featureNumber,
                        PartName = partName,
                        IsFirstFeatureOfPart = isNewPart && f == 0,
                        IsLastFeatureOnSheet = isLastFeature,
                        IsSafetyHeadraise = isSafetyHeadraise && f == 0,
                        IsExteriorFeature = false,
                        IsEtch = isEtch,
                        LibraryFile = isEtch ? etchLibrary : cutLibrary,
                        CutDistance = cutDistance,
                        SheetDiagonal = sheetDiagonal
                    };

                    _featureWriter.Write(w, ctx);
                    featureIndex++;
                }
            }

            lastPartName = partName;
        }
    }

    private void WriteSubprogramCall(TextWriter w, Part part, int subNum,
        int featureIndex, string partName, bool isSafetyHeadraise, bool isLastPart)
    {
        // Safety headraise before rapid to new part
        if (isSafetyHeadraise && _config.SafetyHeadraiseDistance.HasValue)
            w.WriteLine($"M47 P{_config.SafetyHeadraiseDistance.Value} (Safety Headraise)");

        // Rapid to part position (bounding box lower-left)
        var featureNumber = featureIndex == 0
            ? _config.FeatureLineNumberStart
            : 1000 + featureIndex + 1;

        var sb = new StringBuilder();
        if (_config.UseLineNumbers)
            sb.Append($"N{featureNumber} ");
        sb.Append($"G0 X{_fmt.FormatCoord(part.Left)} Y{_fmt.FormatCoord(part.Bottom)}");
        w.WriteLine(sb.ToString());

        // Part name comment
        w.WriteLine(CoordinateFormatter.Comment($"PART: {partName}"));

        // Set local coordinate system at part position
        w.WriteLine("G92 X0 Y0");

        // Call part sub-program
        w.WriteLine($"M98 P{subNum} ({partName})");

        // Restore sheet coordinate system
        w.WriteLine($"G92 X{_fmt.FormatCoord(part.Left)} Y{_fmt.FormatCoord(part.Bottom)}");

        // Head raise (unless last part on sheet)
        if (!isLastPart)
            w.WriteLine("M47");
    }

    private void WritePartsInline(TextWriter w, List<Part> allParts,
        string cutLibrary, string etchLibrary, double sheetDiagonal)
    {
        // Split and classify features, ordering etch before cut per part
        var features = new List<(Part part, List<ICode> codes, bool isEtch)>();
        foreach (var part in allParts)
        {
            var partFeatures = SplitAndOrderFeatures(part);
            foreach (var (codes, isEtch) in partFeatures)
                features.Add((part, codes, isEtch));
        }

        // Emit features
        var lastPartName = "";
        for (var i = 0; i < features.Count; i++)
        {
            var (part, codes, isEtch) = features[i];
            var partName = part.BaseDrawing.Name;
            var isFirstFeatureOfPart = partName != lastPartName;
            var isSafetyHeadraise = partName != lastPartName && lastPartName != "";
            var isLastFeature = i == features.Count - 1;

            var featureNumber = i == 0
                ? _config.FeatureLineNumberStart
                : 1000 + i + 1;

            var cutDistance = ComputeCutDistance(codes);

            var ctx = new FeatureContext
            {
                Codes = codes,
                FeatureNumber = featureNumber,
                PartName = partName,
                IsFirstFeatureOfPart = isFirstFeatureOfPart,
                IsLastFeatureOnSheet = isLastFeature,
                IsSafetyHeadraise = isSafetyHeadraise,
                IsExteriorFeature = false,
                IsEtch = isEtch,
                LibraryFile = isEtch ? etchLibrary : cutLibrary,
                CutDistance = cutDistance,
                SheetDiagonal = sheetDiagonal
            };

            _featureWriter.Write(w, ctx);
            lastPartName = partName;
        }
    }

    /// <summary>
    /// Splits a part's program into features (by rapids), classifies each as etch or cut,
    /// and orders etch features before cut features.
    /// </summary>
    public static List<(List<ICode> codes, bool isEtch)> SplitAndOrderFeatures(Part part)
    {
        var etchFeatures = new List<List<ICode>>();
        var cutFeatures = new List<List<ICode>>();
        List<ICode> current = null;

        foreach (var code in part.Program.Codes)
        {
            if (code is RapidMove)
            {
                if (current != null)
                    ClassifyAndAdd(current, etchFeatures, cutFeatures);
                current = new List<ICode> { code };
            }
            else
            {
                current ??= new List<ICode>();
                current.Add(code);
            }
        }

        if (current != null && current.Count > 0)
            ClassifyAndAdd(current, etchFeatures, cutFeatures);

        // Etch features first, then cut features
        var result = new List<(List<ICode>, bool)>();
        foreach (var f in etchFeatures)
            result.Add((f, true));
        foreach (var f in cutFeatures)
            result.Add((f, false));

        return result;
    }

    private static void ClassifyAndAdd(List<ICode> codes,
        List<List<ICode>> etchFeatures, List<List<ICode>> cutFeatures)
    {
        if (IsFeatureEtch(codes))
            etchFeatures.Add(codes);
        else
            cutFeatures.Add(codes);
    }

    /// <summary>
    /// A feature is etch if any non-rapid move has LayerType.Scribe.
    /// </summary>
    public static bool IsFeatureEtch(List<ICode> codes)
    {
        foreach (var code in codes)
        {
            if (code is LinearMove linear && linear.Layer == LayerType.Scribe)
                return true;
            if (code is ArcMove arc && arc.Layer == LayerType.Scribe)
                return true;
        }
        return false;
    }

    private static double ComputeCutDistance(List<ICode> codes)
    {
        var distance = 0.0;
        var currentPos = Vector.Zero;

        foreach (var code in codes)
        {
            if (code is RapidMove rapid)
            {
                currentPos = rapid.EndPoint;
            }
            else if (code is LinearMove linear)
            {
                distance += currentPos.DistanceTo(linear.EndPoint);
                currentPos = linear.EndPoint;
            }
            else if (code is ArcMove arc)
            {
                distance += currentPos.DistanceTo(arc.EndPoint);
                currentPos = arc.EndPoint;
            }
        }

        return distance;
    }
}
