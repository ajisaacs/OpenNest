using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenNest.CNC;

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
    private readonly Dictionary<int, int> _holeSubprograms;

    public CincinnatiSheetWriter(CincinnatiPostConfig config, ProgramVariableManager vars,
        Dictionary<int, int> holeSubprograms = null)
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
    /// Optional mapping of (drawingId, rotationKey) to sub-program number.
    /// When provided, non-cutoff parts are emitted as M98 calls instead of inline features.
    /// </param>
    public void Write(TextWriter w, Plate plate, string nestName, int layoutIndex, int subNumber,
        string cutLibrary, string etchLibrary,
        Dictionary<(int, long), int> partSubprograms = null,
        Dictionary<(int drawingId, string varName), int> userVarMapping = null)
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
        w.WriteLine($"#{_config.SheetWidthVariable}={_fmt.FormatCoord(width)} (SHEET WIDTH FOR CUTOFFS)");
        w.WriteLine($"#{_config.SheetLengthVariable}={_fmt.FormatCoord(length)} (SHEET LENGTH FOR CUTOFFS)");

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
            WritePartsWithSubprograms(w, allParts, cutLibrary, etchLibrary, sheetDiagonal, width, length, partSubprograms, userVarMapping);
        else
            WritePartsInline(w, allParts, cutLibrary, etchLibrary, sheetDiagonal, width, length, userVarMapping);

        // 5. Footer
        w.WriteLine("M42");
        if (_config.PalletExchange != PalletMode.None)
            w.WriteLine("M50");
        w.WriteLine($"M99 (END OF {nestName}.{layoutIndex:D3})");
    }

    private void WritePartsWithSubprograms(TextWriter w, List<Part> allParts,
        string cutLibrary, string etchLibrary, double sheetDiagonal,
        double plateWidth, double plateLength,
        Dictionary<(int, long), int> partSubprograms,
        Dictionary<(int drawingId, string varName), int> userVarMapping)
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
                var features = FeatureUtils.SplitAndClassify(part);
                for (var f = 0; f < features.Count; f++)
                {
                    var (codes, isEtch) = features[f];
                    var isLastFeature = isLastPart && f == features.Count - 1;

                    // SubProgramCall features are emitted as M98 hole calls
                    if (codes.Count == 1 && codes[0] is SubProgramCall holeCall)
                    {
                        WriteHoleSubprogramCall(w, holeCall, featureIndex, isLastFeature);
                        featureIndex++;
                        lastPartName = partName;
                        continue;
                    }

                    var featureNumber = featureIndex == 0
                        ? _config.FeatureLineNumberStart
                        : 1000 + featureIndex + 1;

                    var cutDistance = FeatureUtils.ComputeCutDistance(codes);

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
                        SheetDiagonal = sheetDiagonal,
                        PartLocation = part.Location,
                        UserVariableMapping = userVarMapping,
                        DrawingId = part.BaseDrawing.Id,
                        IsCutOff = part.BaseDrawing.IsCutOff,
                        PlateWidth = plateWidth,
                        PlateLength = plateLength
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

    private void WriteHoleSubprogramCall(TextWriter w, SubProgramCall call, int featureIndex, bool isLastFeature)
    {
        var postSubNum = _holeSubprograms != null && _holeSubprograms.TryGetValue(call.Id, out var num)
            ? num : call.Id;

        var featureNumber = featureIndex == 0
            ? _config.FeatureLineNumberStart
            : 1000 + featureIndex + 1;

        // Shift the local origin to the hole center via G52 (manual §1.52).
        // G52 does not move the nozzle, so the sub-program's first rapid
        // (the lead-in to the pierce point) takes the tool straight from the
        // previous feature's end to pierce. The hole sub-program is authored
        // in hole-local coordinates and resolves to `hole + local` under the
        // shift. See docs/cincinnati-post-output.md for the full bracket.
        var sb = new StringBuilder();
        if (_config.UseLineNumbers)
            sb.Append($"N{featureNumber} ");
        sb.Append($"G52 X{_fmt.FormatCoord(call.Offset.X)} Y{_fmt.FormatCoord(call.Offset.Y)}");
        w.WriteLine(sb.ToString());

        w.WriteLine($"M98 P{postSubNum}");

        // Cancel the local shift (manual §1.52).
        w.WriteLine("G52 X0 Y0");

        if (!isLastFeature)
            w.WriteLine("M47");
    }

    private void WritePartsInline(TextWriter w, List<Part> allParts,
        string cutLibrary, string etchLibrary, double sheetDiagonal,
        double plateWidth, double plateLength,
        Dictionary<(int drawingId, string varName), int> userVarMapping)
    {
        // Split and classify features, ordering etch before cut per part
        var features = new List<(Part part, List<ICode> codes, bool isEtch)>();
        foreach (var part in allParts)
        {
            var partFeatures = FeatureUtils.SplitAndClassify(part);
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

            // SubProgramCall features are emitted as M98 hole calls
            if (codes.Count == 1 && codes[0] is SubProgramCall holeCall)
            {
                WriteHoleSubprogramCall(w, holeCall, i, isLastFeature);
                lastPartName = partName;
                continue;
            }

            var featureNumber = i == 0
                ? _config.FeatureLineNumberStart
                : 1000 + i + 1;

            var cutDistance = FeatureUtils.ComputeCutDistance(codes);

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
                SheetDiagonal = sheetDiagonal,
                PartLocation = part.Location,
                UserVariableMapping = userVarMapping,
                DrawingId = part.BaseDrawing.Id,
                IsCutOff = part.BaseDrawing.IsCutOff,
                PlateWidth = plateWidth,
                PlateLength = plateLength
            };

            _featureWriter.Write(w, ctx);
            lastPartName = partName;
        }
    }

}
