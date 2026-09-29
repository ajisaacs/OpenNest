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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parts);
        cancellationToken.ThrowIfCancellationRequested();
        var captured = new List<CapturedOverlapPart>();
        var issues = new List<PlateOverlapIssue>();
        var sources = new Dictionary<Program, CapturedSource>(ReferenceEqualityComparer.Instance);
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
                if (!sources.TryGetValue(program, out var source))
                {
                    try
                    {
                        ValidateProgram(program, new HashSet<Program>(ReferenceEqualityComparer.Instance));
                        // Conversion creates fresh geometry, including expanded shared hole calls;
                        // no cloning/rotation of a live program or subprogram is necessary.
                        source = new CapturedSource(ConvertProgram.ToGeometry(program)
                            .Where(entity => SpecialLayers.IsMaterial(entity.Layer)
                                && entity.Layer != SpecialLayers.Leadin
                                && entity.Layer != SpecialLayers.Leadout).ToList(), null);
                    }
                    catch (Exception exception) when (IsGeometryFailure(exception))
                    {
                        source = new CapturedSource(null, exception.Message);
                    }
                    sources.Add(program, source);
                }
                if (source.Error != null)
                    throw new ArgumentException(source.Error);
                captured.Add(new CapturedOverlapPart(id, part.BaseDrawing.Name,
                    source.Entities, rotation, location));
            }
            catch (Exception exception) when (IsGeometryFailure(exception))
            {
                issues.Add(new PlateOverlapIssue(id, null, exception.Message));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PlateOverlapSnapshot(captured, issues);
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var issues = snapshot.Issues.ToList();
        var pairs = new List<PlateOverlapPair>();
        var prepared = new List<PreparedPart>();
        var sources = new Dictionary<List<Entity>, PreparedSource>(ReferenceEqualityComparer.Instance);
        foreach (var part in snapshot.Parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!sources.TryGetValue(part.Entities, out var source))
                {
                    try
                    {
                        source = new PreparedSource(OverlapMaterial.Read(part.Entities, cancellationToken), null);
                    }
                    catch (Exception exception) when (IsGeometryFailure(exception))
                    {
                        source = new PreparedSource(null, exception.Message);
                    }
                    sources.Add(part.Entities, source);
                }
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
            .ThenBy(issue => issue.PartBId).ToList());
    }

    private static bool IsGeometryFailure(Exception exception) => exception is
        ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;

    private static void ValidateProgram(Program program, HashSet<Program> visiting)
    {
        if (program == null || !visiting.Add(program) || visiting.Count > 64)
            throw new ArgumentException("Missing, recursive, or excessively nested subprogram.");
        foreach (var code in program.Codes)
        {
            if (code == null)
                throw new ArgumentException("Program contains a missing instruction.");
            if (code is Motion motion && !OverlapMaterial.IsFinite(motion.EndPoint)
                || code is ArcMove arc && !OverlapMaterial.IsFinite(arc.CenterPoint))
                throw new ArgumentException("Program coordinates must be finite.");
            if (code is SubProgramCall call)
            {
                if (!OverlapMaterial.IsFinite(call.Offset) || !double.IsFinite(call.Rotation))
                    throw new ArgumentException("Subprogram pose must be finite.");
                ValidateProgram(call.Program, visiting);
            }
        }
        visiting.Remove(program);
    }

    private sealed record CapturedSource(List<Entity> Entities, string Error);
    private sealed record PreparedSource(OverlapMaterial Material, string Error);
    private sealed record PreparedPart(CapturedOverlapPart Input, OverlapMaterial Material);
}
