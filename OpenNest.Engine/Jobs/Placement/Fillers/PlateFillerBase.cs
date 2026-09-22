using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;
using OpenNest.Geometry;

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

    protected string BuildProgressSummary() => BuildProgressSummary(PhaseResults);

    private static string BuildProgressSummary(IReadOnlyList<PhaseResult> phaseResults)
    {
        if (phaseResults.Count == 0)
            return null;

        var parts = new List<string>(phaseResults.Count);
        foreach (var result in phaseResults)
            parts.Add($"{result.Phase.ShortName()}: {result.PartCount}");

        return string.Join(" | ", parts);
    }

    protected bool IsBetterFill(List<Part> candidate, List<Part> current, Box workArea) =>
        Comparer.IsBetter(candidate, current, workArea);

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
