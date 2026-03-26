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
        int subNumber, string cutLibrary, string etchLibrary, double sheetDiagonal)
    {
        var allFeatures = FeatureUtils.SplitByRapids(normalizedProgram.Codes);
        if (allFeatures.Count == 0)
            return;

        // Classify and order: etch features first, then cut features
        var ordered = FeatureUtils.ClassifyAndOrder(allFeatures);

        w.WriteLine("(*****************************************************)");
        w.WriteLine($":{subNumber}");
        w.WriteLine(CoordinateFormatter.Comment($"PART: {drawingName}"));

        for (var i = 0; i < ordered.Count; i++)
        {
            var (codes, isEtch) = ordered[i];
            var featureNumber = i == 0
                ? _config.FeatureLineNumberStart
                : 1000 + i + 1;
            var cutDistance = FeatureUtils.ComputeCutDistance(codes);

            var ctx = new FeatureContext
            {
                Codes = codes,
                FeatureNumber = featureNumber,
                PartName = drawingName,
                IsFirstFeatureOfPart = false,
                IsLastFeatureOnSheet = i == ordered.Count - 1,
                IsSafetyHeadraise = false,
                IsExteriorFeature = false,
                IsEtch = isEtch,
                LibraryFile = isEtch ? etchLibrary : cutLibrary,
                CutDistance = cutDistance,
                SheetDiagonal = sheetDiagonal
            };

            _featureWriter.Write(w, ctx);
        }

        w.WriteLine("G0 X0 Y0");
        w.WriteLine($"M99 (END OF {drawingName})");
    }

    /// <summary>
    /// Creates a sub-program key for matching parts to their sub-programs.
    /// </summary>
    internal static (int drawingId, long rotationKey) SubprogramKey(Part part) =>
        (part.BaseDrawing.Id, (long)System.Math.Round(part.Rotation * 1e6));

    /// <summary>
    /// Scans all plates and builds a mapping of unique part geometries to sub-program numbers,
    /// along with their normalized programs for writing.
    /// </summary>
    internal static (Dictionary<(int, long), int> mapping, List<(int subNum, string name, Program program)> entries)
        BuildRegistry(IEnumerable<Plate> plates, int startNumber)
    {
        var mapping = new Dictionary<(int, long), int>();
        var entries = new List<(int, string, Program)>();
        var nextSubNum = startNumber;

        foreach (var plate in plates)
        {
            foreach (var part in plate.Parts)
            {
                if (part.BaseDrawing.IsCutOff) continue;
                var key = SubprogramKey(part);
                if (!mapping.ContainsKey(key))
                {
                    var subNum = nextSubNum++;
                    mapping[key] = subNum;

                    var pgm = part.Program.Clone() as Program;
                    var bbox = pgm.BoundingBox();
                    pgm.Offset(-bbox.Location.X, -bbox.Location.Y);

                    entries.Add((subNum, part.BaseDrawing.Name, pgm));
                }
            }
        }

        return (mapping, entries);
    }
}
