using System.Collections.Generic;
using System.Globalization;
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
    private readonly CoordinateFormatter _fmt;
    private readonly IReadOnlyDictionary<Program, int> _holeSubprograms;

    public CincinnatiPartSubprogramWriter(
        CincinnatiPostConfig config,
        IReadOnlyDictionary<Program, int> holeSubprograms = null
    )
    {
        _config = config;
        _featureWriter = new CincinnatiFeatureWriter(config);
        _fmt = new CoordinateFormatter(config.PostedAccuracy);
        _holeSubprograms = holeSubprograms;
    }

    /// <summary>
    /// Writes a complete part sub-program for the given normalized program.
    /// The program must be in absolute mode with coordinates normalized to origin (0,0).
    /// </summary>
    public void Write(
        TextWriter w,
        Program normalizedProgram,
        string drawingName,
        int subNumber,
        string cutLibrary,
        string etchLibrary,
        double sheetDiagonal
    )
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
            var isLastFeature = i == ordered.Count - 1;

            // SubProgramCall features are emitted as M98 hole calls
            if (codes.Count == 1 && codes[0] is SubProgramCall holeCall)
            {
                CincinnatiHoleCallWriter.Write(
                    w, holeCall, Vector.Zero, i, isLastFeature, _config, _fmt, _holeSubprograms
                );
                continue;
            }

            var featureNumber = i == 0 ? _config.FeatureLineNumberStart : 1000 + i + 1;
            var cutDistance = FeatureUtils.ComputeCutDistance(codes);

            var ctx = new FeatureContext
            {
                Codes = codes,
                FeatureNumber = featureNumber,
                PartName = drawingName,
                IsFirstFeatureOfPart = false,
                IsLastFeatureOnSheet = isLastFeature,
                IsSafetyHeadraise = false,
                IsExteriorFeature = false,
                IsEtch = isEtch,
                LibraryFile = isEtch ? etchLibrary : cutLibrary,
                CutDistance = cutDistance,
                SheetDiagonal = sheetDiagonal,
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
    internal static void EnsureLeadingRapid(Program pgm) => FeatureUtils.InsertLeadingRapid(pgm.Codes);

    /// <summary>
    /// Creates a sub-program key for matching parts to their sub-programs.
    /// </summary>
    internal static (int drawingId, long rotationKey) SubprogramKey(Part part) =>
        (part.BaseDrawing.Id, (long)System.Math.Round(part.Rotation * 1e6));

    /// <summary>
    /// Scans all plates and builds a mapping of unique part geometries to sub-program numbers,
    /// along with their normalized programs for writing.
    /// </summary>
    internal static (
        Dictionary<(int, long), int> mapping,
        List<(int subNum, string name, Program program)> entries
    ) BuildRegistry(IEnumerable<Plate> plates, int startNumber)
    {
        var mapping = new Dictionary<(int, long), int>();
        var entries = new List<(int, string, Program)>();
        var nextSubNum = startNumber;

        foreach (var plate in plates)
        {
            foreach (var part in plate.Parts)
            {
                if (part.BaseDrawing.IsCutOff)
                    continue;
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
    /// Builds a nest-level registry of unique hole sub-programs from every hole call in
    /// the plates' parts and in the part sub-program copies. Calls are keyed by their
    /// program reference, because hole call ids are only unique within one drawing's
    /// program; identical hole geometry shares one sub-program. Entries are absolute-mode
    /// copies, so the caller's (incremental) hole programs are never modified.
    /// </summary>
    internal static (
        Dictionary<Program, int> mapping,
        List<(int subNum, Program program)> entries
    ) BuildHoleRegistry(
        IEnumerable<Plate> plates,
        IEnumerable<Program> partSubprograms,
        int startNumber
    )
    {
        var mapping = new Dictionary<Program, int>(ReferenceEqualityComparer.Instance);
        var entries = new List<(int, Program)>();
        var contentIndex = new Dictionary<string, int>();
        var nextSubNum = startNumber;

        void Register(Program owner)
        {
            foreach (var code in owner.Codes)
            {
                if (code is not SubProgramCall { Program: not null } call)
                    continue;
                if (mapping.ContainsKey(call.Program))
                    continue;

                var absolute = FeatureUtils.ToAbsoluteCopy(call.Program);
                EnsureLeadingRapid(absolute);

                var canonical = ProgramToCanonical(absolute);
                if (!contentIndex.TryGetValue(canonical, out var subNum))
                {
                    subNum = nextSubNum++;
                    contentIndex[canonical] = subNum;
                    entries.Add((subNum, absolute));
                }

                mapping[call.Program] = subNum;
            }
        }

        foreach (var plate in plates)
        {
            foreach (var part in plate.Parts)
            {
                if (!part.BaseDrawing.IsCutOff)
                    Register(part.Program);
            }
        }

        if (partSubprograms != null)
        {
            foreach (var program in partSubprograms)
                Register(program);
        }

        return (mapping, entries);
    }

    private static string ProgramToCanonical(Program pgm)
    {
        var sb = new StringBuilder();
        foreach (var code in pgm.Codes)
        {
            if (code is LinearMove lm)
                sb.Append(
                    CultureInfo.InvariantCulture,
                    $"L{lm.EndPoint.X:F6},{lm.EndPoint.Y:F6},{(int)lm.Layer}"
                );
            else if (code is ArcMove am)
                sb.Append(
                    CultureInfo.InvariantCulture,
                    $"A{am.EndPoint.X:F6},{am.EndPoint.Y:F6},{am.CenterPoint.X:F6},{am.CenterPoint.Y:F6},{(int)am.Rotation},{(int)am.Layer}"
                );
            else if (code is RapidMove rm)
                sb.Append(CultureInfo.InvariantCulture, $"R{rm.EndPoint.X:F6},{rm.EndPoint.Y:F6}");
        }
        return sb.ToString();
    }
}
