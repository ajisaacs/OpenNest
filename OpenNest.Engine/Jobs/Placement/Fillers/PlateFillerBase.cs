using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Jobs.Placement.Fillers;

internal abstract class PlateFillerBase
{
    private IFillComparer comparer;

    protected PlateFillerBase(Plate plate)
    {
        Plate = plate;
    }

    public Plate Plate { get; }

    public int PlateNumber { get; set; }

    public NestDirection NestDirection { get; set; }

    public NestPhase WinnerPhase { get; protected set; }

    internal void SetWinnerPhase(NestPhase winnerPhase)
    {
        WinnerPhase = winnerPhase;
    }

    public List<PhaseResult> PhaseResults { get; } = new();

    public List<AngleResult> AngleResults { get; } = new();

    protected IFillComparer Comparer => comparer ??= CreateComparer();

    protected virtual IFillComparer CreateComparer() => new DefaultFillComparer();

    public virtual NestDirection? PreferredDirection => null;

    public virtual ShrinkAxis TrimAxis => ShrinkAxis.Width;

    public virtual List<double> BuildAngles(
        NestItem item,
        ClassificationResult classification,
        Box workArea
    )
    {
        return new List<double>
        {
            classification.PrimaryAngle,
            classification.PrimaryAngle + OpenNest.Math.Angle.HalfPI,
        };
    }

    protected virtual void RecordProductiveAngles(List<AngleResult> angleResults) { }

    protected FillPolicy BuildPolicy() => new(Comparer, PreferredDirection);

    internal IFillComparer FillComparer => Comparer;

    protected string BuildProgressSummary() => BuildProgressSummary(PhaseResults);

    internal static string BuildProgressSummary(IReadOnlyList<PhaseResult> phaseResults)
    {
        if (phaseResults.Count == 0)
            return null;

        var parts = new List<string>(phaseResults.Count);
        foreach (var result in phaseResults)
            parts.Add($"{result.Phase.ShortName()}: {result.PartCount}");

        return string.Join(" | ", parts);
    }

    protected bool IsBetterFill(List<Part> candidate, List<Part> current, Box workArea) =>
        IsBetterFill(Comparer, candidate, current, workArea);

    internal static bool IsBetterFill(
        IFillComparer comparer,
        List<Part> candidate,
        List<Part> current,
        Box workArea
    ) => comparer.IsBetter(candidate, current, workArea);

    protected bool IsBetterValidFill(List<Part> candidate, List<Part> current, Box workArea)
    {
        if (
            candidate != null
            && candidate.Count > 0
            && HasOverlaps(candidate, Plate.PartSpacing)
        )
        {
            Debug.WriteLine(
                $"[IsBetterValidFill] REJECTED {candidate.Count} parts due to overlaps (current best: {current?.Count ?? 0})"
            );
            return false;
        }

        return IsBetterFill(candidate, current, workArea);
    }

    internal static bool HasOverlaps(List<Part> parts, double spacing)
    {
        if (parts == null || parts.Count <= 1)
            return false;

        for (var i = 0; i < parts.Count; i++)
        {
            var box1 = parts[i].BoundingBox;

            for (var j = i + 1; j < parts.Count; j++)
            {
                var box2 = parts[j].BoundingBox;

                var overlapX = System.Math.Min(box1.Right, box2.Right)
                    - System.Math.Max(box1.Left, box2.Left);
                var overlapY = System.Math.Min(box1.Top, box2.Top)
                    - System.Math.Max(box1.Bottom, box2.Bottom);

                if (overlapX <= Tolerance.Epsilon || overlapY <= Tolerance.Epsilon)
                    continue;

                List<Vector> points;
                if (parts[i].Intersects(parts[j], out points))
                {
                    var first = parts[i].BoundingBox;
                    var second = parts[j].BoundingBox;
                    Debug.WriteLine(
                        $"[HasOverlaps] Overlap: part[{i}] ({parts[i].BaseDrawing?.Name}) @ ({first.Left:F2},{first.Bottom:F2})-({first.Right:F2},{first.Top:F2}) rot={parts[i].Rotation:F2}"
                            + $" vs part[{j}] ({parts[j].BaseDrawing?.Name}) @ ({second.Left:F2},{second.Bottom:F2})-({second.Right:F2},{second.Top:F2}) rot={parts[j].Rotation:F2}"
                            + $" intersections={points?.Count ?? 0}"
                    );
                    return true;
                }
            }
        }

        return false;
    }

    public virtual List<Part> Fill(
        NestItem item,
        Box workArea,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        return new List<Part>();
    }

    public virtual List<Part> Fill(
        List<Part> groupParts,
        Box workArea,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        return new List<Part>();
    }

    public virtual List<Part> PackArea(
        Box box,
        List<NestItem> items,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        return new List<Part>();
    }

    public virtual List<Part> Nest(
        List<NestItem> items,
        IProgress<NestProgress> progress,
        CancellationToken token
    )
    {
        return PlateFillOrchestrator.Nest(
            Plate,
            items,
            Comparer,
            (item, workArea, sink, cancellation) => Fill(item, workArea, sink, cancellation),
            (workArea, packItems, sink, cancellation) =>
                PackArea(workArea, packItems, sink, cancellation),
            progress,
            token
        );
    }
}
