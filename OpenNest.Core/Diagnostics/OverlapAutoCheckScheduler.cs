namespace OpenNest.Diagnostics;

public enum OverlapAutoCheckStep { None, Wait, Check }

/// <summary>
/// UI-thread debounce policy for automatic overlap rechecks; the host owns the timer.
/// Restart the quiet-period timer whenever <see cref="Observe"/> returns true, and call
/// <see cref="Elapsed"/> when it fires. A check starts only after the ordered layout stamp
/// has stayed unchanged for a whole quiet period with no interaction in progress.
/// A canceled or failed request is not retried until the layout differs from the one it
/// started from, so cancellation is respected and a failing layout cannot loop.
/// </summary>
public sealed class OverlapAutoCheckScheduler
{
    private OverlapGeometryStamp pending;
    private OverlapGeometryStamp lastRequest;

    public bool IsWaiting => pending != null;

    /// <summary>
    /// Call after the report state's freshness check. True means (re)start the quiet-period
    /// timer; false means leave it as it is.
    /// </summary>
    public bool Observe(Plate plate, OverlapReportState state)
    {
        if (plate == null || !NeedsCheck(plate, state))
        {
            pending = null;
            return false;
        }
        if (pending != null && pending.Matches(plate))
            return false;
        pending = OverlapGeometryStamp.Capture(plate);
        return true;
    }

    /// <summary>
    /// Call when the quiet period ends. <see cref="OverlapAutoCheckStep.Wait"/> means restart the
    /// timer (the layout moved or an interaction is still running); only
    /// <see cref="OverlapAutoCheckStep.Check"/> starts a request.
    /// </summary>
    public OverlapAutoCheckStep Elapsed(Plate plate, OverlapReportState state, bool interactionActive)
    {
        if (pending == null)
            return OverlapAutoCheckStep.None;
        if (plate == null || !NeedsCheck(plate, state))
        {
            pending = null;
            return OverlapAutoCheckStep.None;
        }
        if (interactionActive || !pending.Matches(plate))
        {
            pending = OverlapGeometryStamp.Capture(plate);
            return OverlapAutoCheckStep.Wait;
        }
        pending = null;
        return OverlapAutoCheckStep.Check;
    }

    /// <summary>Record every request start, manual or automatic.</summary>
    public void Started(Plate plate)
    {
        pending = null;
        lastRequest = OverlapGeometryStamp.Capture(plate);
    }

    /// <summary>Forget pending and previous requests (plate switch, handle loss, disable).</summary>
    public void Reset()
    {
        pending = null;
        lastRequest = null;
    }

    private bool NeedsCheck(Plate plate, OverlapReportState state) => state.Status switch
    {
        OverlapCheckStatus.NotChecked or OverlapCheckStatus.Stale => true,
        OverlapCheckStatus.Canceled or OverlapCheckStatus.Failed => lastRequest?.Matches(plate) != true,
        _ => false
    };
}
