using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Engine.Jobs;

/// <summary>Why a requested copy was not placed. Engines report one stop reason per job; the
/// pipeline adds what it can establish itself per requirement. None is a proof of
/// geometric impossibility except <see cref="NoStockFits"/> at the sampled rotations.</summary>
public enum NestUnplacedReason
{
    /// <summary>The proposal cannot be kept, so none of its placements count.</summary>
    ProposalNotKeepable,
    /// <summary>The part's material bounds exceed every offered sheet's work area at each
    /// sampled legal rotation.</summary>
    NoStockFits,
    /// <summary>Reserved inside a cutout of a frame copy the engine did not place.</summary>
    FrameUnplaced,
    /// <summary>Every offered sheet was used.</summary>
    StockExhausted,
    /// <summary>The job's plate limit was reached.</summary>
    PlateLimitReached,
    /// <summary>The engine found no position for it; not proof that none exists.</summary>
    NoPlacementFound,
}

/// <summary>Copies of one requirement that the proposal leaves unplaced, and why.
/// <see cref="Drawing"/> is the caller's drawing for that requirement.</summary>
public sealed record NestUnplacedPart(string PartId, Drawing Drawing, int Count, NestUnplacedReason Reason)
{
    /// <summary>One line for people: drawing name, copy count and reason.</summary>
    public string Describe() => $"{Drawing?.Name ?? PartId}: {Count} not placed ({Describe(Reason)})";

    /// <summary>The reason in words shared by every front end.</summary>
    public static string Describe(NestUnplacedReason reason) => reason switch
    {
        NestUnplacedReason.ProposalNotKeepable => "the proposal cannot be kept",
        NestUnplacedReason.NoStockFits => "does not fit the usable area of any offered sheet at the rotations checked",
        NestUnplacedReason.FrameUnplaced => "reserved inside a cutout of a frame that was not placed",
        NestUnplacedReason.StockExhausted => "no sheets left",
        NestUnplacedReason.PlateLimitReached => "sheet limit reached",
        NestUnplacedReason.NoPlacementFound => "the engine found no position; this does not prove none exists",
        _ => reason.ToString(),
    };
}

internal static class NestUnplacedParts
{
    /// <summary>Counts come from the returned physical placements (validated when keepable),
    /// never from engine fulfillment metadata.</summary>
    internal static IReadOnlyList<NestUnplacedPart> Classify(
        NestJob job,
        NestJobResult raw,
        bool canKeep,
        IReadOnlyDictionary<string, Drawing> drawings,
        IReadOnlyDictionary<string, int> releasedInserts)
    {
        var placed = new Dictionary<string, int>(StringComparer.Ordinal);
        if (canKeep)
            foreach (var pose in raw.Plates.SelectMany(p => p.Placements))
                placed[pose.PartId] = placed.GetValueOrDefault(pose.PartId) + 1;
        var general = raw.StopReason switch
        {
            NestJobStopReason.StockExhausted => NestUnplacedReason.StockExhausted,
            NestJobStopReason.PlateLimitReached => NestUnplacedReason.PlateLimitReached,
            _ => NestUnplacedReason.NoPlacementFound,
        };
        var unplaced = new List<NestUnplacedPart>();
        foreach (var part in job.Parts)
        {
            var missing = part.Quantity - placed.GetValueOrDefault(part.Id);
            if (missing <= 0)
                continue;
            var drawing = drawings[part.Id];
            if (!canKeep)
            {
                unplaced.Add(new(part.Id, drawing, missing, NestUnplacedReason.ProposalNotKeepable));
                continue;
            }
            if (!FitsOfferedStock(part, job.Plates))
            {
                unplaced.Add(new(part.Id, drawing, missing, NestUnplacedReason.NoStockFits));
                continue;
            }
            var released = System.Math.Min(missing, releasedInserts.GetValueOrDefault(part.Id));
            if (released > 0)
                unplaced.Add(new(part.Id, drawing, released, NestUnplacedReason.FrameUnplaced));
            if (missing > released)
                unplaced.Add(new(part.Id, drawing, missing - released, general));
        }
        return unplaced.AsReadOnly();
    }

    /// <summary>Whether the part's material bounds fit the work area of any offered stock at
    /// any of up to 16 sampled legal rotations (automatic rotation keeps its quarter turns).</summary>
    internal static bool FitsOfferedStock(NestJobPart part, IReadOnlyList<NestPlateStock> stock)
    {
        foreach (var angle in part.Rotation.EnumerateAngles(maxSamples: 16))
            if (FitsOfferedStock(part, angle, stock))
                return true;
        return false;
    }

    internal static bool FitsOfferedStock(NestJobPart part, double angle,
        IReadOnlyList<NestPlateStock> stock)
    {
        var placed = new Part(DrawingJobMapper.CreateDrawing(part));
        placed.Rotate(angle);
        var bounds = NestLayoutCheck.MaterialBounds(placed);
        return stock.Any(s => s.Quantity != 0 && s.Fits(bounds.Length, bounds.Width,
            NestTolerances.WorkAreaSlack));
    }
}
