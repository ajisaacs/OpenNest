using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// Owner-thread lifetime of a read-only cutting proposal. Capture before starting the worker;
/// complete on the owner thread. A close request never releases ownership while work is running.
/// </summary>
public sealed class CuttingProposalInspection
{
    private CancellationTokenSource cancellation;
    private Task<CuttingPlanProposal> worker;
    public bool IsRunning => cancellation != null;
    public bool CloseRequested { get; private set; }
    public CuttingPlanProposal Proposal { get; private set; }
    public Plate Preview { get; private set; }

    public Task<CuttingPlanProposal> BeginPlanning(CuttingPlanBatch captured,
        Action<CancellationToken> beforePlan = null, IProgress<CuttingPlanProgress> progress = null)
    {
        ArgumentNullException.ThrowIfNull(captured);
        if (IsRunning || CloseRequested)
            throw new InvalidOperationException("This inspection cannot start another worker.");
        Proposal = null;
        Preview = null;
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        worker = Task.Run(() =>
        {
            beforePlan?.Invoke(token);
            token.ThrowIfCancellationRequested();
            return captured.Plan(progress, token);
        });
        return worker;
    }

    /// <summary>True only when the panel can release its input lock and be removed.</summary>
    public bool RequestClose()
    {
        CloseRequested = true;
        Preview = null;
        Proposal = null;
        cancellation?.Cancel();
        return !IsRunning;
    }

    public void CompletePlanning(Task<CuttingPlanProposal> completed)
    {
        if (!ReferenceEquals(worker, completed) || !completed.IsCompleted)
            throw new InvalidOperationException("Only the completed current worker can release ownership.");
        cancellation.Dispose();
        cancellation = null;
        worker = null;
        if (CloseRequested || !completed.IsCompletedSuccessfully)
            return;
        Proposal = completed.Result;
        RefreshPreview();
    }

    /// <summary>Recheck exact freshness before displaying, including every subsequent paint.</summary>
    public Plate RefreshPreview()
    {
        if (Proposal == null || CloseRequested)
            return Preview = null;
        // BuildPreview is the authority; never clone a rejected program for presentation.
        if (Proposal.Plates[0].Result.Snapshot?.PlateState?.IsCurrent() != true)
            return Preview = null;
        return Preview ??= Proposal.BuildPreview(0);
    }

    /// <summary>Zero-based proposed position for a one-based source diagnostic number.</summary>
    public static int? ProposedPosition(CuttingPlanProposal proposal, int plateIndex, int sourceNumber)
    {
        if (proposal == null || plateIndex < 0 || plateIndex >= proposal.Plates.Count || sourceNumber < 1)
            return null;
        var order = proposal.Plates[plateIndex].Result.ProposedOrder;
        for (var i = 0; i < order.Count; ++i)
            if (order[i].SourceOrdinal == sourceNumber - 1)
                return i;
        return null;
    }
}
