using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

/// <summary>
/// Read-only, hole-aware material overlap diagnostics, separate from the engine's boolean checks.
/// Uses clean drawing outlines, not placed lead-in/tab toolpaths or spacing offsets.
/// </summary>
public static class PlateOverlapAnalyzer
{
    public const double ChordTolerance = 0.001;

    /// <summary>
    /// Captures poses and converts each distinct clean source program to owned entities once.
    /// Inputs must not change during capture. Later analysis never reads live domain objects.
    /// </summary>
    public static PlateOverlapSnapshot Capture(IReadOnlyList<Part> parts,
        CancellationToken cancellationToken = default) =>
        Capture(parts, new OverlapMaterialCache(), cancellationToken);

    /// <summary>
    /// As <see cref="Capture(IReadOnlyList{Part}, CancellationToken)"/>, but reuses converted
    /// and prepared drawing material from <paramref name="cache"/> across requests. Clear the
    /// cache before any in-place clean-program edit (see <see cref="OverlapMaterialCache"/>).
    /// </summary>
    public static PlateOverlapSnapshot Capture(IReadOnlyList<Part> parts, OverlapMaterialCache cache,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(cache);
        cancellationToken.ThrowIfCancellationRequested();
        var captured = new List<CapturedOverlapPart>();
        var issues = new List<PlateOverlapIssue>();
        for (var id = 0; id < parts.Count; id++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var part = parts[id];
            if (part?.BaseDrawing?.IsCutOff == true)
                continue;
            try
            {
                if (part?.BaseDrawing?.Program == null)
                    throw new ArgumentException("Part has no clean drawing program.");
                var program = part.BaseDrawing.Program;
                var rotation = part.Rotation - program.Rotation;
                var location = part.Location;
                if (!double.IsFinite(rotation) || !OverlapMaterial.IsFinite(location))
                    throw new ArgumentException("Part pose must be finite.");
                var source = cache.Get(program, CaptureSource);
                if (source.Error != null)
                    throw new ArgumentException(source.Error);
                captured.Add(new CapturedOverlapPart(id, part.BaseDrawing.Name,
                    source, rotation, location));
            }
            catch (Exception exception) when (IsGeometryFailure(exception))
            {
                issues.Add(new PlateOverlapIssue(id, null, exception.Message));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PlateOverlapSnapshot(captured, issues);
    }

    private static OverlapSource CaptureSource(Program program)
    {
        try
        {
            ValidateProgram(program, new HashSet<Program>(ReferenceEqualityComparer.Instance), false);
            // Conversion creates fresh geometry, including expanded shared hole calls;
            // no cloning/rotation of a live program or subprogram is necessary.
            return new OverlapSource(program, ConvertProgram.ToGeometry(program)
                .Where(entity => SpecialLayers.IsMaterial(entity.Layer)
                    && entity.Layer != SpecialLayers.Leadin
                    && entity.Layer != SpecialLayers.Leadout).ToList(), null);
        }
        catch (Exception exception) when (IsGeometryFailure(exception))
        {
            return new OverlapSource(program, null, exception.Message);
        }
    }

    /// <summary>Convenience synchronous capture and analysis of a group of parts.</summary>
    public static PlateOverlapReport Analyze(IReadOnlyList<Part> parts,
        CancellationToken cancellationToken = default) =>
        Analyze(Capture(parts, cancellationToken), cancellationToken);

    /// <summary>
    /// Returns deterministic pair reports containing closed world-coordinate overlap fragments.
    /// Cancellation throws and publishes no partial report. Check IsComplete before claiming clear.
    /// </summary>
    public static PlateOverlapReport Analyze(PlateOverlapSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        Analyze(snapshot, null, cancellationToken);

    /// <summary>
    /// Incremental recheck: identical to a full analysis of <paramref name="snapshot"/>, but a
    /// pair whose two parts are unchanged since <paramref name="previous"/> (same captured source
    /// and bit-identical pose, in the same relative order) reuses that report's result instead
    /// of being clipped again. After moving one part only its own neighbors are recomputed.
    /// Reuse needs sources shared through one <see cref="OverlapMaterialCache"/>; otherwise every
    /// pair is recomputed. Null <paramref name="previous"/> performs a full analysis.
    /// </summary>
    public static PlateOverlapReport Analyze(PlateOverlapSnapshot snapshot, PlateOverlapReport previous,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var reuse = PairReuse.Create(previous, snapshot);
        var issues = snapshot.Issues.ToList();
        var pairs = new List<PlateOverlapPair>();
        var prepared = new List<PreparedPart>();
        foreach (var part in snapshot.Parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Prepared material is shared by every part and request using this source.
                var source = part.Source.Prepare(cancellationToken);
                if (source.Error != null)
                    throw new ArgumentException(source.Error);
                var material = source.Material.Transform(part.Rotation, part.Location);
                prepared.Add(new PreparedPart(part, material));
            }
            catch (Exception exception) when (IsGeometryFailure(exception))
            {
                issues.Add(new PlateOverlapIssue(part.Id, null, exception.Message));
            }
        }

        var sorted = prepared.OrderBy(part => part.Material.Outer.BoundingBox.Left)
            .ThenBy(part => part.Input.Id).ToArray();
        for (var i = 0; i < sorted.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = sorted[i];
            var bounds = first.Material.Outer.BoundingBox;
            for (var j = i + 1; j < sorted.Length; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var second = sorted[j];
                var otherBounds = second.Material.Outer.BoundingBox;
                if (otherBounds.Left >= bounds.Right)
                    break;
                if (otherBounds.Bottom >= bounds.Top || bounds.Bottom >= otherBounds.Top)
                    continue;
                var a = first.Input.Id < second.Input.Id ? first : second;
                var b = first.Input.Id < second.Input.Id ? second : first;
                if (reuse != null && reuse.TryReuse(a.Input, b.Input, pairs, issues))
                    continue;
                try
                {
                    // Keep pair clipping arithmetic near the parts where possible, then
                    // restore output to world space. Triangulation itself also uses stable
                    // local-origin winding so tiny holes in a huge part remain correct.
                    var origin = a.Material.Outer.Vertices[0];
                    var localA = a.Material.Transform(0, origin * -1);
                    var localB = b.Material.Transform(0, origin * -1);
                    var result = Collision.Check(localA.Outer, localB.Outer, localA.Holes, localB.Holes);
                    if (!result.Overlaps)
                        continue;
                    // Evaluate every hole-subtracted fragment before restoring world space.
                    // A failed moment must make this pair incomplete, never an origin marker.
                    var moments = new List<PolygonAreaMoments>();
                    var regions = new List<PlateOverlapRegion>();
                    foreach (var region in result.OverlapRegions)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!PolygonAreaMoments.TryCompute(region.Vertices, out var fragment))
                            throw new ArithmeticException("Overlap fragment area moments are invalid.");
                        moments.Add(fragment);
                        regions.Add(new PlateOverlapRegion(region.Vertices.Select(point => point + origin),
                            fragment.Area));
                    }
                    if (!PolygonAreaMoments.TryCombine(moments, out var combined))
                        throw new ArithmeticException("Combined overlap area moments are invalid.");
                    var centroid = combined.Centroid + origin;
                    if (!OverlapMaterial.IsFinite(centroid))
                        throw new ArithmeticException("Overlap centroid is not finite in world coordinates.");
                    pairs.Add(new PlateOverlapPair(a.Input.Id, b.Input.Id,
                        a.Input.Name, b.Input.Name, regions, centroid));
                }
                catch (Exception exception) when (IsGeometryFailure(exception))
                {
                    issues.Add(new PlateOverlapIssue(a.Input.Id, b.Input.Id, exception.Message));
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PlateOverlapReport(pairs.OrderBy(pair => pair.PartAId)
            .ThenBy(pair => pair.PartBId).ToList(), issues.OrderBy(issue => issue.PartAId)
            .ThenBy(issue => issue.PartBId).ToList(), snapshot);
    }

    /// <summary>
    /// Maps unchanged parts to their previous input positions and looks up previous pair results.
    /// A part is unchanged when its captured source object and exact pose bits match; identical
    /// duplicates are matched in input order, which is safe because their inputs are bit-identical.
    /// </summary>
    private sealed class PairReuse
    {
        private readonly Dictionary<int, int> previousIds;
        private readonly Dictionary<(int, int), PlateOverlapPair> pairs = new();
        private readonly Dictionary<(int, int), PlateOverlapIssue> issues = new();

        private PairReuse(Dictionary<int, int> previousIds, PlateOverlapReport previous)
        {
            this.previousIds = previousIds;
            foreach (var pair in previous.Pairs)
                pairs[(pair.PartAId, pair.PartBId)] = pair;
            foreach (var issue in previous.Issues)
                if (issue.PartBId.HasValue)
                    issues[(issue.PartAId, issue.PartBId.Value)] = issue;
        }

        public static PairReuse Create(PlateOverlapReport previous, PlateOverlapSnapshot snapshot)
        {
            if (previous?.Snapshot == null)
                return null;
            var available = new Dictionary<PoseKey, Queue<int>>();
            foreach (var part in previous.Snapshot.Parts)
            {
                var key = PoseKey.Of(part);
                if (!available.TryGetValue(key, out var ids))
                    available.Add(key, ids = new Queue<int>());
                ids.Enqueue(part.Id);
            }
            var previousIds = new Dictionary<int, int>();
            foreach (var part in snapshot.Parts)
                if (available.TryGetValue(PoseKey.Of(part), out var ids) && ids.Count > 0)
                    previousIds.Add(part.Id, ids.Dequeue());
            return previousIds.Count < 2 ? null : new PairReuse(previousIds, previous);
        }

        /// <summary>
        /// Every bounding-box candidate pair among prepared parts was evaluated by the previous
        /// analysis, and unchanged parts have identical bounds, so absence there means clear.
        /// The previous pair must have had the same operand order: clipping is order-sensitive.
        /// </summary>
        public bool TryReuse(CapturedOverlapPart a, CapturedOverlapPart b,
            List<PlateOverlapPair> pairOutput, List<PlateOverlapIssue> issueOutput)
        {
            if (!previousIds.TryGetValue(a.Id, out var oldA) || !previousIds.TryGetValue(b.Id, out var oldB)
                || oldA >= oldB)
                return false;
            if (pairs.TryGetValue((oldA, oldB), out var pair))
                pairOutput.Add(pair.Renumber(a.Id, b.Id, a.Name, b.Name));
            else if (issues.TryGetValue((oldA, oldB), out var issue))
                issueOutput.Add(issue with { PartAId = a.Id, PartBId = b.Id });
            return true;
        }
    }

    private readonly record struct PoseKey(OverlapSource Source, long Rotation, long X, long Y)
    {
        public static PoseKey Of(CapturedOverlapPart part) => new(part.Source,
            BitConverter.DoubleToInt64Bits(part.Rotation),
            BitConverter.DoubleToInt64Bits(part.Location.X),
            BitConverter.DoubleToInt64Bits(part.Location.Y));
    }

    internal static bool IsGeometryFailure(Exception exception) => exception is
        ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;

    // Exact built-in instruction types. Anything else, subclasses included, is refused before the
    // graph is copied or converted, so no unknown Clone or cast runs.
    private static readonly HashSet<Type> SupportedCodeTypes =
    [
        typeof(RapidMove), typeof(LinearMove), typeof(ArcMove), typeof(SubProgramCall),
        typeof(Comment), typeof(Feedrate), typeof(Kerf),
    ];

    private static void ValidateProgram(Program program, HashSet<Program> visiting, bool subprogram)
    {
        if (program == null || !visiting.Add(program) || visiting.Count > 64)
            throw new ArgumentException("Missing, recursive, or excessively nested subprogram.");
        if (program.Codes == null)
            throw new ArgumentException("Program has no instruction list.");
        // The converter adds a call's frame offset to incremental moves only, so an absolute
        // subprogram would be read at its frame origin. Converting it exactly needs a lossless
        // frame transform; until then it is refused rather than misread (OpenNest writes hole
        // subprograms in incremental mode).
        if (subprogram && program.Mode == Mode.Absolute)
            throw new NotSupportedException("Absolute-mode subprograms are not supported by the overlap check.");
        foreach (var code in program.Codes)
        {
            if (code == null)
                throw new ArgumentException("Program contains a missing instruction.");
            if (!SupportedCodeTypes.Contains(code.GetType()))
                throw new NotSupportedException("Program contains an unsupported instruction.");
            if (code is Motion motion && !OverlapMaterial.IsFinite(motion.EndPoint)
                || code is ArcMove arc && !OverlapMaterial.IsFinite(arc.CenterPoint))
                throw new ArgumentException("Program coordinates must be finite.");
            if (code is SubProgramCall call)
            {
                if (!OverlapMaterial.IsFinite(call.Offset) || !double.IsFinite(call.Rotation))
                    throw new ArgumentException("Subprogram pose must be finite.");
                ValidateProgram(call.Program, visiting, true);
            }
        }
        visiting.Remove(program);
    }

    private sealed record PreparedPart(CapturedOverlapPart Input, OverlapMaterial Material);
}
