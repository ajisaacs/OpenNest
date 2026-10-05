using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>An owned planned program for one unlocked part; ownership passes to the part on Apply.</summary>
public sealed class PlannedPartProgram
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

/// <summary>The verified order and planned programs for one plate, bound to its captured state.</summary>
public sealed class PlateCuttingPlan
{
    public PlateCuttingPlan(PlateCuttingState expected, IEnumerable<Part> order,
        IEnumerable<PlannedPartProgram> programs = null)
    {
        Expected = expected;
        Order = order == null ? null : Array.AsReadOnly(order.ToArray());
        Programs = Array.AsReadOnly((programs ?? []).ToArray());
    }

    public PlateCuttingState Expected { get; }
    public IReadOnlyList<Part> Order { get; }
    public IReadOnlyList<PlannedPartProgram> Programs { get; }
}

public enum CuttingCommitStatus
{
    Applied,
    Stale,
    InvalidInput,
    Cancelled,
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
/// </summary>
public static class CuttingPlanCommit
{
    public static CuttingCommitResult Apply(IEnumerable<PlateCuttingPlan> plans, CancellationToken token = default) =>
        Apply(plans, token, null);

    // beforeInstall is a test seam that runs inside the install boundary, before each program.
    internal static CuttingCommitResult Apply(IEnumerable<PlateCuttingPlan> plans, CancellationToken token,
        Action<Plate, Part> beforeInstall)
    {
        if (token.IsCancellationRequested)
            return new(CuttingCommitStatus.Cancelled, "Cancelled before commit.");
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
                return new(CuttingCommitStatus.Cancelled, "Cancelled before commit.");
            }
            if (difference != null)
                return new(CuttingCommitStatus.Stale, difference, plan.Expected.Plate);
        }

        var staged = new List<Staged>(scope.Length);
        var targets = new HashSet<Part>(ReferenceEqualityComparer.Instance);
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
            var members = new HashSet<Part>(order, ReferenceEqualityComparer.Instance);
            var programs = new List<(Part, Program, Box, CuttingParameters)>();
            foreach (var planned in plan.Programs)
            {
                if (planned?.Part == null || !members.Contains(planned.Part) || !targets.Add(planned.Part))
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
            var reordered = !order.SequenceEqual(plate.Parts, ReferenceEqualityComparer.Instance);
            staged.Add(new(plate, order, programs, reordered || programs.Count != 0));
        }

        // Last cancellation point. The boundary below has no await, search or geometry work.
        if (token.IsCancellationRequested)
            return new(CuttingCommitStatus.Cancelled, "Cancelled before commit.");

        var undo = new List<(Plate Plate, Part[] Order, (Part Part, PartCuttingState State)[] States)>();
        try
        {
            foreach (var item in staged)
            {
                undo.Add((item.Plate, item.Plate.Parts.ToArray(),
                    item.Programs.Select(p => (p.Part, p.Part.CaptureCuttingState())).ToArray()));
                item.Plate.Parts.SetOrder(item.Order);
                foreach (var (part, program, bounds, parameters) in item.Programs)
                {
                    beforeInstall?.Invoke(item.Plate, part);
                    part.InstallPlannedProgram(program, bounds, parameters);
                }
            }
        }
        catch (Exception ex)
        {
            for (var i = undo.Count - 1; i >= 0; i--)
            {
                foreach (var (part, state) in undo[i].States)
                    part.RestoreCuttingState(state);
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
        List<(Part Part, Program Program, Box Bounds, CuttingParameters Parameters)> Programs, bool Changed);
}
