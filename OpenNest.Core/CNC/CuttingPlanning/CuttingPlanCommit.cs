using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>An owned planned program for one unlocked part; ownership passes to the part on Apply.</summary>
internal sealed class PlannedPartProgram
{
    public PlannedPartProgram(Part part, Program program, CuttingParameters parameters)
    {
        Part = part;
        Program = program;
        Parameters = parameters;
    }

    public Part Part { get; }
    public Program Program { get; }
    public CuttingParameters Parameters { get; }
}

/// <summary>
/// An owned cutoff segment program cut the other way at the far sheet edge
/// (<see cref="CutOff.ReverseAtFarEdge"/>); Apply installs it and records the direction on the
/// segment's cutoff definition, so regeneration keeps it.
/// </summary>
internal sealed class PlannedCutOffProgram
{
    public PlannedCutOffProgram(Part segment, Program program)
    {
        Segment = segment;
        Program = program;
    }

    public Part Segment { get; }
    public Program Program { get; }
}

/// <summary>The verified order and planned programs for one plate, bound to its captured state.</summary>
internal sealed class PlateCuttingPlan
{
    public PlateCuttingPlan(PlateCuttingState expected, IEnumerable<Part> order,
        IEnumerable<PlannedPartProgram> programs = null, IEnumerable<PlannedCutOffProgram> cutOffs = null)
    {
        Expected = expected;
        Order = order == null ? null : Array.AsReadOnly(order.ToArray());
        Programs = Array.AsReadOnly((programs ?? []).ToArray());
        CutOffs = Array.AsReadOnly((cutOffs ?? []).ToArray());
    }

    public PlateCuttingState Expected { get; }
    public IReadOnlyList<Part> Order { get; }
    public IReadOnlyList<PlannedPartProgram> Programs { get; }
    public IReadOnlyList<PlannedCutOffProgram> CutOffs { get; }
}

public enum CuttingCommitStatus
{
    Applied,
    Stale,
    InvalidInput,
    Canceled,
    Failed
}

/// <summary>
/// Applied means every plate in scope holds its new state. RefreshErrors are observer failures
/// raised after that consistent state was published; they are not a rollback. Every other
/// status leaves every plate exactly as it was.
/// </summary>
public sealed class CuttingCommitResult
{
    internal CuttingCommitResult(CuttingCommitStatus status, string message = null, Plate plate = null,
        Exception error = null, IEnumerable<Exception> refreshErrors = null)
    {
        Status = status;
        Message = message;
        Plate = plate;
        Error = error;
        RefreshErrors = Array.AsReadOnly((refreshErrors ?? []).ToArray());
    }

    public CuttingCommitStatus Status { get; }
    public string Message { get; }
    public Plate Plate { get; }
    public Exception Error { get; }
    public IReadOnlyList<Exception> RefreshErrors { get; }
}

/// <summary>
/// Installs verified cutting plans for a whole scope at once. Nothing is searched, emitted,
/// rotated or regenerated here: inputs are validated and checked for freshness, bounds are
/// staged, then order and programs are installed synchronously and published once per plate.
/// Internal: it checks root program references only, so payloads must be owned copies of
/// independently replayed proposals. CuttingPlanService.Apply is the public entry point.
/// </summary>
internal static class CuttingPlanCommit
{
    internal static CuttingCommitResult Apply(IEnumerable<PlateCuttingPlan> plans, CancellationToken token = default) =>
        Apply(plans, token, null);

    // beforeInstall is a test seam that runs inside the install boundary, before each program.
    internal static CuttingCommitResult Apply(IEnumerable<PlateCuttingPlan> plans, CancellationToken token,
        Action<Plate, Part> beforeInstall)
    {
        if (token.IsCancellationRequested)
            return new(CuttingCommitStatus.Canceled, "Canceled before commit.");
        var scope = plans?.ToArray();
        if (scope == null || scope.Length == 0 || scope.Any(p => p?.Expected == null || p.Order == null))
            return Invalid(null, "A nonempty set of captured plate plans is required.");
        var plates = new HashSet<Plate>(ReferenceEqualityComparer.Instance);
        foreach (var plan in scope)
            if (!plates.Add(plan.Expected.Plate))
                return Invalid(plan.Expected.Plate, "A plate appears more than once in the commit scope.");

        // Freshness for the whole scope before any validation that reads live geometry.
        foreach (var plan in scope)
        {
            string difference;
            try
            {
                difference = plan.Expected.Difference(token);
            }
            catch (OperationCanceledException)
            {
                return new(CuttingCommitStatus.Canceled, "Canceled before commit.");
            }
            if (difference != null)
                return new(CuttingCommitStatus.Stale, difference, plan.Expected.Plate);
        }

        var staged = new List<Staged>(scope.Length);
        var targets = new HashSet<Part>(ReferenceEqualityComparer.Instance);
        var members = new HashSet<Part>(ReferenceEqualityComparer.Instance);
        var installed = new HashSet<Program>(ReferenceEqualityComparer.Instance);
        var live = new HashSet<Program>(scope.SelectMany(p => p.Expected.Order).Select(p => p.Program),
            ReferenceEqualityComparer.Instance);
        foreach (var plan in scope)
        {
            var plate = plan.Expected.Plate;
            Part[] order;
            try
            {
                order = plate.Parts.ValidateReorder(plan.Order);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Invalid(plate, "The planned order is not exactly the plate's current parts.");
            }
            // A part on two plates would let one plate's install change the other's verified program.
            if (order.Any(part => !members.Add(part)))
                return Invalid(plate, "A part appears on more than one plate in the commit scope.");
            var onPlate = new HashSet<Part>(order, ReferenceEqualityComparer.Instance);
            var programs = new List<(Part, Program, Box, CuttingParameters)>();
            foreach (var planned in plan.Programs)
            {
                if (planned?.Part == null || !onPlate.Contains(planned.Part) || !targets.Add(planned.Part))
                    return Invalid(plate, "Planned programs must target distinct parts of their own plate.");
                if (planned.Part.LeadInsLocked)
                    return Invalid(plate, "A locked part's program is retained exactly and cannot be replaced.");
                if (planned.Program == null || planned.Parameters == null || live.Contains(planned.Program)
                    || !installed.Add(planned.Program))
                    return Invalid(plate, "Planned programs and settings must be present, owned and unshared.");
                if (!planned.Program.Codes.Any(code => code is Motion
                    || code is SubProgramCall call && call.Program?.Codes.Any(sub => sub is Motion) == true))
                    return Invalid(plate, "A planned program has no motion.");
                Box bounds;
                try
                {
                    bounds = planned.Program.BoundingBox();
                    bounds.Offset(planned.Part.Location);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                    or NotSupportedException or ArithmeticException)
                {
                    return Invalid(plate, "A planned program's bounds cannot be computed.");
                }
                programs.Add((planned.Part, planned.Program, bounds, planned.Parameters));
            }
            var cutOffs = new List<(Part, Program, CutOff)>();
            foreach (var planned in plan.CutOffs)
            {
                if (planned?.Segment == null || !onPlate.Contains(planned.Segment) || !targets.Add(planned.Segment))
                    return Invalid(plate, "Planned programs must target distinct parts of their own plate.");
                var definition = plate.CutOffs.FirstOrDefault(c => ReferenceEquals(c?.Drawing, planned.Segment.BaseDrawing));
                // Only the placed far-edge segment, cut exactly the other way, may be installed.
                if (definition == null || planned.Program == null || live.Contains(planned.Program)
                    || !installed.Add(planned.Program)
                    || CutOff.ReverseAtFarEdge(planned.Segment.Program) is not { } expected
                    || !ProgramContent.Equal(expected, planned.Program, token))
                    return Invalid(plate, "A cutoff segment can only be installed cut the other way at the far edge.");
                cutOffs.Add((planned.Segment, planned.Program, definition));
            }
            var reordered = !order.SequenceEqual(plate.Parts, ReferenceEqualityComparer.Instance);
            staged.Add(new(plate, order, programs, cutOffs, reordered || programs.Count != 0 || cutOffs.Count != 0));
        }

        // Last cancellation point. The boundary below has no await, search or geometry work.
        if (token.IsCancellationRequested)
            return new(CuttingCommitStatus.Canceled, "Canceled before commit.");

        var undo = new List<(Plate Plate, Part[] Order, (Part Part, PartCuttingState State)[] States,
            (CutOff CutOff, bool FarEdgeFirst, Program Program)[] CutOffs)>();
        try
        {
            foreach (var item in staged)
            {
                undo.Add((item.Plate, item.Plate.Parts.ToArray(),
                    item.Programs.Select(p => p.Part).Concat(item.CutOffs.Select(c => c.Segment))
                        .Select(part => (part, part.CaptureCuttingState())).ToArray(),
                    item.CutOffs.Select(c => (c.CutOff, c.CutOff.FarEdgeFirst, c.CutOff.Drawing.Program)).ToArray()));
                item.Plate.Parts.SetOrder(item.Order);
                foreach (var (part, program, bounds, parameters) in item.Programs)
                {
                    beforeInstall?.Invoke(item.Plate, part);
                    part.InstallPlannedProgram(program, bounds, parameters);
                }
                foreach (var (segment, program, cutOff) in item.CutOffs)
                {
                    beforeInstall?.Invoke(item.Plate, segment);
                    cutOff.InstallFarEdgeSegment(segment, program);
                }
            }
        }
        catch (Exception ex)
        {
            for (var i = undo.Count - 1; i >= 0; i--)
            {
                foreach (var (part, state) in undo[i].States)
                    part.RestoreCuttingState(state);
                foreach (var (cutOff, farEdgeFirst, program) in undo[i].CutOffs)
                {
                    cutOff.FarEdgeFirst = farEdgeFirst;
                    cutOff.Drawing.Program = program;
                }
                undo[i].Plate.Parts.SetOrder(undo[i].Order);
            }
            return new(CuttingCommitStatus.Failed, "Install failed; every plate was restored.",
                undo.Count == 0 ? null : undo[^1].Plate, ex);
        }

        // Publish only after the whole scope is consistent.
        var errors = new List<Exception>();
        foreach (var item in staged.Where(s => s.Changed))
            item.Plate.Parts.RaiseItemsReordered(errors);
        return new(CuttingCommitStatus.Applied, errors.Count == 0 ? null
            : "Applied; one or more views failed to refresh.", refreshErrors: errors);
    }

    private static CuttingCommitResult Invalid(Plate plate, string message) =>
        new(CuttingCommitStatus.InvalidInput, message, plate);

    private sealed record Staged(Plate Plate, Part[] Order,
        List<(Part Part, Program Program, Box Bounds, CuttingParameters Parameters)> Programs,
        List<(Part Segment, Program Program, CutOff CutOff)> CutOffs, bool Changed);
}
