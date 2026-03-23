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
    /// <param name="partSubprograms">
    /// Optional mapping of (drawingId, rotationKey) to sub-program number.
    /// When provided, non-cutoff parts are emitted as M98 calls instead of inline features.
    /// </param>
    public void Write(TextWriter w, Plate plate, string nestName, int sheetIndex, int subNumber,
        Dictionary<(int, long), int> partSubprograms = null)
    {
        if (plate.Parts.Count == 0)
            return;

        var width = plate.Size.Width;
        var length = plate.Size.Length;
        var sheetDiagonal = System.Math.Sqrt(width * width + length * length);
        var libraryFile = _config.DefaultLibraryFile ?? "";
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
        w.WriteLine($"#{_config.SheetWidthVariable}={_fmt.FormatCoord(width)}(SHEET WIDTH FOR CUTOFFS)");
        w.WriteLine($"#{_config.SheetLengthVariable}={_fmt.FormatCoord(length)}(SHEET LENGTH FOR CUTOFFS)");

        // 2. Coordinate setup
        w.WriteLine("M42");
        w.WriteLine("N10000");
        w.WriteLine("G92X#5021Y#5022");
        if (!string.IsNullOrEmpty(libraryFile))
            w.WriteLine($"G89 P {libraryFile}");
        w.WriteLine($"M98 P{varDeclSub} (Variable Declaration)");
        w.WriteLine("G90");
        w.WriteLine("M47(CPT)");
        if (!string.IsNullOrEmpty(libraryFile))
            w.WriteLine($"G89 P {libraryFile}");
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
            WritePartsWithSubprograms(w, allParts, libraryFile, sheetDiagonal, partSubprograms);
        else
            WritePartsInline(w, allParts, libraryFile, sheetDiagonal);

        // 5. Footer
        w.WriteLine("M42");
        w.WriteLine("G0X0Y0");
        if (_config.PalletExchange != PalletMode.None)
            w.WriteLine($"N{sheetIndex + 1}M50");
        w.WriteLine($"M99(END OF {nestName}.{sheetIndex:D3})");
    }

    private void WritePartsWithSubprograms(TextWriter w, List<Part> allParts,
        string libraryFile, double sheetDiagonal,
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
                var features = SplitPartFeatures(part);
                for (var f = 0; f < features.Count; f++)
                {
                    var featureNumber = featureIndex == 0
                        ? _config.FeatureLineNumberStart
                        : 1000 + featureIndex + 1;

                    var isLastFeature = isLastPart && f == features.Count - 1;
                    var cutDistance = ComputeCutDistance(features[f]);

                    var ctx = new FeatureContext
                    {
                        Codes = features[f],
                        FeatureNumber = featureNumber,
                        PartName = partName,
                        IsFirstFeatureOfPart = isNewPart && f == 0,
                        IsLastFeatureOnSheet = isLastFeature,
                        IsSafetyHeadraise = isSafetyHeadraise && f == 0,
                        IsExteriorFeature = false,
                        LibraryFile = libraryFile,
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
            w.WriteLine($"M47 P{_config.SafetyHeadraiseDistance.Value}(Safety Headraise)");

        // Rapid to part position (bounding box lower-left)
        var featureNumber = featureIndex == 0
            ? _config.FeatureLineNumberStart
            : 1000 + featureIndex + 1;

        var sb = new StringBuilder();
        if (_config.UseLineNumbers)
            sb.Append($"N{featureNumber}");
        sb.Append($"G0X{_fmt.FormatCoord(part.Left)}Y{_fmt.FormatCoord(part.Bottom)}");
        w.WriteLine(sb.ToString());

        // Part name comment
        w.WriteLine(CoordinateFormatter.Comment($"PART: {partName}"));

        // Set local coordinate system at part position
        w.WriteLine("G92X0Y0");

        // Call part sub-program
        w.WriteLine($"M98P{subNum}({partName})");

        // Restore sheet coordinate system
        w.WriteLine($"G92X{_fmt.FormatCoord(part.Left)}Y{_fmt.FormatCoord(part.Bottom)}");

        // Head raise (unless last part on sheet)
        if (!isLastPart)
            w.WriteLine("M47");
    }

    private void WritePartsInline(TextWriter w, List<Part> allParts,
        string libraryFile, double sheetDiagonal)
    {
        // Multi-contour splitting
        var features = new List<(Part part, List<ICode> codes)>();
        foreach (var part in allParts)
        {
            List<ICode> current = null;
            foreach (var code in part.Program.Codes)
            {
                if (code is RapidMove)
                {
                    if (current != null)
                        features.Add((part, current));
                    current = new List<ICode> { code };
                }
                else
                {
                    current ??= new List<ICode>();
                    current.Add(code);
                }
            }
            if (current != null && current.Count > 0)
                features.Add((part, current));
        }

        // Emit features
        var lastPartName = "";
        for (var i = 0; i < features.Count; i++)
        {
            var (part, codes) = features[i];
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
                LibraryFile = libraryFile,
                CutDistance = cutDistance,
                SheetDiagonal = sheetDiagonal
            };

            _featureWriter.Write(w, ctx);
            lastPartName = partName;
        }
    }

    private static List<List<ICode>> SplitPartFeatures(Part part)
    {
        var features = new List<List<ICode>>();
        List<ICode> current = null;

        foreach (var code in part.Program.Codes)
        {
            if (code is RapidMove)
            {
                if (current != null)
                    features.Add(current);
                current = new List<ICode> { code };
            }
            else
            {
                current ??= new List<ICode>();
                current.Add(code);
            }
        }

        if (current != null && current.Count > 0)
            features.Add(current);

        return features;
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
