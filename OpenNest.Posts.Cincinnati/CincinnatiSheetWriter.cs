using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Posts.Cincinnati;

/// <summary>
/// Emits one Cincinnati-format sheet subprogram per plate.
/// Splits each part's codes at RapidMove boundaries to handle multi-contour parts.
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
    public void Write(TextWriter w, Plate plate, string nestName, int sheetIndex, int subNumber)
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

        // 4. Multi-contour splitting
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

        // 5. Emit features
        var lastPartName = "";
        for (var i = 0; i < features.Count; i++)
        {
            var (part, codes) = features[i];
            var partName = part.BaseDrawing.Name;
            var isFirstFeatureOfPart = partName != lastPartName;
            var isSafetyHeadraise = partName != lastPartName && lastPartName != "";
            var isLastFeature = i == features.Count - 1;

            // Feature numbering: first = FeatureLineNumberStart, then 1002, 1003, etc.
            var featureNumber = i == 0
                ? _config.FeatureLineNumberStart
                : 1000 + i + 1;

            // Compute cut distance for this feature
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

        // 6. Footer
        w.WriteLine("M42");
        w.WriteLine("G0X0Y0");
        if (_config.PalletExchange != PalletMode.None)
            w.WriteLine($"N{sheetIndex + 1}M50");
        w.WriteLine($"M99(END OF {nestName}.{sheetIndex:D3})");
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
