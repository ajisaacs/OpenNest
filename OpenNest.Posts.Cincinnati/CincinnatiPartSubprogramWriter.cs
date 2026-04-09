using System.Collections.Generic;
using System.IO;
using System.Text;
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

        w.WriteLine($"M99 (END OF {drawingName})");
    }

    /// <summary>
    /// If the program has no leading rapid, inserts a synthetic rapid at the
    /// last motion endpoint (the contour return point). This ensures the feature
    /// writer knows the true pierce location and preserves the first contour segment.
    /// </summary>
    internal static void EnsureLeadingRapid(Program pgm)
    {
        if (pgm.Codes.Count == 0 || pgm.Codes[0] is RapidMove)
            return;

        for (var i = pgm.Codes.Count - 1; i >= 0; i--)
        {
            if (pgm.Codes[i] is Motion lastMotion)
            {
                pgm.Codes.Insert(0, new RapidMove(lastMotion.EndPoint));
                return;
            }
        }
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
                    pgm.Mode = Mode.Absolute;
                    var bbox = pgm.BoundingBox();
                    pgm.Offset(-bbox.Location.X, -bbox.Location.Y);

                    // If the program has no leading rapid, the feature writer
                    // will use the first motion endpoint as the pierce point,
                    // losing the first contour segment. Insert a synthetic rapid
                    // at the contour's return point (last motion endpoint) so
                    // the full contour is preserved.
                    EnsureLeadingRapid(pgm);

                    entries.Add((subNum, part.BaseDrawing.Name, pgm));
                }
            }
        }

        return (mapping, entries);
    }

    /// <summary>
    /// Scans all parts across all plates and builds a nest-level registry of unique
    /// hole sub-programs. Deduplicates by comparing sub-program code content.
    /// </summary>
    internal static (Dictionary<int, int> modelToPostMapping, List<(int subNum, Program program)> entries)
        BuildHoleRegistry(IEnumerable<Plate> plates, int startNumber)
    {
        var mapping = new Dictionary<int, int>();
        var entries = new List<(int, Program)>();
        var contentIndex = new Dictionary<string, int>();
        var nextSubNum = startNumber;

        foreach (var plate in plates)
        {
            foreach (var part in plate.Parts)
            {
                if (part.BaseDrawing.IsCutOff) continue;
                foreach (var code in part.Program.Codes)
                {
                    if (code is not SubProgramCall call) continue;
                    if (mapping.ContainsKey(call.Id)) continue;

                    var canonical = ProgramToCanonical(call.Program);
                    if (contentIndex.TryGetValue(canonical, out var existingNum))
                    {
                        mapping[call.Id] = existingNum;
                    }
                    else
                    {
                        var subNum = nextSubNum++;
                        mapping[call.Id] = subNum;
                        contentIndex[canonical] = subNum;
                        entries.Add((subNum, call.Program));
                    }
                }
            }
        }

        return (mapping, entries);
    }

    private static string ProgramToCanonical(Program pgm)
    {
        var sb = new StringBuilder();
        sb.Append(pgm.Mode == Mode.Absolute ? "A" : "I");
        foreach (var code in pgm.Codes)
        {
            if (code is LinearMove lm)
                sb.Append($"L{lm.EndPoint.X:F6},{lm.EndPoint.Y:F6},{(int)lm.Layer}");
            else if (code is ArcMove am)
                sb.Append($"A{am.EndPoint.X:F6},{am.EndPoint.Y:F6},{am.CenterPoint.X:F6},{am.CenterPoint.Y:F6},{(int)am.Rotation},{(int)am.Layer}");
            else if (code is RapidMove rm)
                sb.Append($"R{rm.EndPoint.X:F6},{rm.EndPoint.Y:F6}");
        }
        return sb.ToString();
    }
}
