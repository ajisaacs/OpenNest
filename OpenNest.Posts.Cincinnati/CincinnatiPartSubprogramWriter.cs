using System.Collections.Generic;
using System.IO;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Posts.Cincinnati;

/// <summary>
/// Writes a Cincinnati-format part sub-program definition.
/// Each sub-program contains the complete cutting sequence for one unique part geometry
/// (drawing + rotation), with coordinates normalized to origin (0,0).
/// Called via M98 from sheet sub-programs.
/// </summary>
public sealed class CincinnatiPartSubprogramWriter
{
    private readonly CincinnatiPostConfig _config;
    private readonly CincinnatiFeatureWriter _featureWriter;

    public CincinnatiPartSubprogramWriter(CincinnatiPostConfig config)
    {
        _config = config;
        _featureWriter = new CincinnatiFeatureWriter(config);
    }

    /// <summary>
    /// Writes a complete part sub-program for the given normalized program.
    /// The program coordinates must already be normalized to origin (0,0).
    /// </summary>
    public void Write(TextWriter w, Program normalizedProgram, string drawingName,
        int subNumber, string libraryFile, double sheetDiagonal)
    {
        var features = SplitFeatures(normalizedProgram.Codes);
        if (features.Count == 0)
            return;

        w.WriteLine("(*****************************************************)");
        w.WriteLine($":{subNumber}");
        w.WriteLine(CoordinateFormatter.Comment($"PART: {drawingName}"));

        for (var i = 0; i < features.Count; i++)
        {
            var codes = features[i];
            var featureNumber = i == 0
                ? _config.FeatureLineNumberStart
                : 1000 + i + 1;
            var cutDistance = ComputeCutDistance(codes);

            var ctx = new FeatureContext
            {
                Codes = codes,
                FeatureNumber = featureNumber,
                PartName = drawingName,
                IsFirstFeatureOfPart = false,
                IsLastFeatureOnSheet = i == features.Count - 1,
                IsSafetyHeadraise = false,
                IsExteriorFeature = false,
                LibraryFile = libraryFile,
                CutDistance = cutDistance,
                SheetDiagonal = sheetDiagonal
            };

            _featureWriter.Write(w, ctx);
        }

        w.WriteLine("G0X0Y0");
        w.WriteLine($"M99(END OF {drawingName})");
    }

    /// <summary>
    /// Creates a sub-program key for matching parts to their sub-programs.
    /// </summary>
    internal static (int drawingId, long rotationKey) SubprogramKey(Part part) =>
        (part.BaseDrawing.Id, (long)System.Math.Round(part.Rotation * 1e6));

    internal static List<List<ICode>> SplitFeatures(List<ICode> codes)
    {
        var features = new List<List<ICode>>();
        List<ICode> current = null;

        foreach (var code in codes)
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

    internal static double ComputeCutDistance(List<ICode> codes)
    {
        var distance = 0.0;
        var currentPos = Vector.Zero;

        foreach (var code in codes)
        {
            if (code is RapidMove rapid)
                currentPos = rapid.EndPoint;
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
