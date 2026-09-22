using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Placement;

/// <summary>Constrained-order linear fills in conservative rectangular free regions.
/// Regions are only search hints; every accepted pose passes the job geometry validator.</summary>
internal sealed class OrderedPlateNester : IPlateNester
{
    private readonly Dictionary<string, Drawing> drawings = new(StringComparer.Ordinal);

    public PlateCandidate Place(
        PlatePlacementRequest request,
        IProgress<NestJobProgress> progress = null,
        CancellationToken token = default
    )
    {
        var work = DrawingJobMapper.CreatePlate(request.Stock).WorkArea();
        var poses = new List<NestJobPlacement>();
        var obstacles = new List<Box>();
        var requirements = request.Parts.ToDictionary(p => p.Id);
        var demand = request.Parts.ToDictionary(p => p.Id, p => p.Quantity);
        foreach (var requirement in request.Parts)
        {
            token.ThrowIfCancellationRequested();
            if (!drawings.TryGetValue(requirement.Id, out var drawing))
                drawings.Add(requirement.Id, drawing = DrawingJobMapper.CreateDrawing(requirement));
            var left = requirement.Quantity;
            while (left > 0)
            {
                var regions = new RemnantFinder(work, obstacles).FindRemnants();
                List<Part> best = null;
                foreach (var region in regions)
                {
                    foreach (var angle in Angles(requirement.Rotation))
                    {
                        token.ThrowIfCancellationRequested();
                        // FillLinear uses actual line/arc geometry for copy distances.
                        var parts = new FillLinear(region, request.Stock.PartSpacing)
                            .Fill(drawing, angle, NestDirection.Horizontal)
                            .Take(left)
                            .ToList();
                        if (parts.Count == 0 || (best != null && parts.Count <= best.Count))
                            continue;
                        var trial = poses
                            .Concat(
                                parts.Select(p => new NestJobPlacement(
                                    requirement.Id,
                                    0,
                                    p.Location.X,
                                    p.Location.Y,
                                    p.Rotation
                                ))
                            )
                            .ToList();
                        try
                        {
                            NestJobValidator.ValidateCandidate(
                                new PlateCandidate(trial),
                                request.Stock,
                                demand,
                                requirements
                            );
                            best = parts;
                        }
                        catch (InvalidOperationException)
                        {
                            // Geometry kernels are proposal generators, never the acceptance gate.
                        }
                        if (best?.Count == left)
                            break;
                    }
                    if (best?.Count == left)
                        break;
                }
                if (best == null)
                    break;
                foreach (var part in best)
                {
                    poses.Add(
                        new NestJobPlacement(
                            requirement.Id,
                            0,
                            part.Location.X,
                            part.Location.Y,
                            part.Rotation
                        )
                    );
                    obstacles.Add(part.BoundingBox.Offset(request.Stock.PartSpacing));
                }
                left -= best.Count;
            }
        }
        token.ThrowIfCancellationRequested();
        return new PlateCandidate(poses);
    }

    private static IEnumerable<double> Angles(RotationPolicy policy)
    {
        if (policy.Kind == RotationPolicyKind.Fixed)
        {
            yield return policy.Start;
            if (policy.Allow180Equivalent)
                yield return policy.Start + System.Math.PI;
            yield break;
        }
        // A bounded deterministic search, not a proof that an unplaced part cannot fit.
        if (policy.Kind == RotationPolicyKind.Automatic)
        {
            yield return 0;
            yield return System.Math.PI / 2;
            yield return System.Math.PI;
            yield return 3 * System.Math.PI / 2;
            for (var degrees = 5; degrees < 180; degrees += 5)
                if (degrees != 90)
                    yield return degrees * System.Math.PI / 180;
            yield break;
        }
        for (var index = 0L; ; index++)
        {
            var angle = policy.Start + index * policy.Step;
            if (angle > policy.End + 1e-9)
                yield break;
            yield return angle;
            if (policy.Allow180Equivalent)
                yield return angle + System.Math.PI;
        }
    }
}
