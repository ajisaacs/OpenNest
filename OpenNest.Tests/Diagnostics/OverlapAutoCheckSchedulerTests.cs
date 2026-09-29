using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.Diagnostics;

public class OverlapAutoCheckSchedulerTests
{
    [Fact]
    public void ChecksOnlyAfterTheLayoutStaysQuietForAWholePeriod()
    {
        var plate = PlateWithParts();
        var state = new OverlapReportState();
        var scheduler = new OverlapAutoCheckScheduler();

        Assert.True(scheduler.Observe(plate, state));       // NotChecked: start the timer
        Assert.False(scheduler.Observe(plate, state));      // repaint without change: leave it running
        plate.Parts[0].Offset(1, 0);                          // drag step
        Assert.True(scheduler.Observe(plate, state));       // restart the quiet period
        plate.Parts[0].Offset(1, 0);                          // moved again, not yet observed
        Assert.Equal(OverlapAutoCheckStep.Wait, scheduler.Elapsed(plate, state, interactionActive: false));
        Assert.Equal(OverlapAutoCheckStep.Check, scheduler.Elapsed(plate, state, interactionActive: false));
        Assert.False(scheduler.IsWaiting);
    }

    [Fact]
    public void WaitsWhileAnInteractionIsActive()
    {
        var plate = PlateWithParts();
        var state = new OverlapReportState();
        var scheduler = new OverlapAutoCheckScheduler();
        scheduler.Observe(plate, state);
        Assert.Equal(OverlapAutoCheckStep.Wait, scheduler.Elapsed(plate, state, interactionActive: true));
        Assert.Equal(OverlapAutoCheckStep.Check, scheduler.Elapsed(plate, state, interactionActive: false));
    }

    [Fact]
    public void CurrentOrRunningChecksNeedNoAutomaticRequest()
    {
        var plate = PlateWithParts();
        var state = new OverlapReportState();
        var scheduler = new OverlapAutoCheckScheduler();
        scheduler.Observe(plate, state);
        var request = state.Begin(plate);
        scheduler.Started(plate);
        Assert.False(scheduler.Observe(plate, state));
        Assert.Equal(OverlapAutoCheckStep.None, scheduler.Elapsed(plate, state, false));
        Assert.True(state.TryPublish(request, plate, PlateOverlapAnalyzer.Analyze(plate.Parts.ToArray())));
        Assert.False(scheduler.Observe(plate, state));

        plate.Parts[1].Offset(0, 3);
        state.EnsureFresh(plate);                            // Stale after the edit
        Assert.True(scheduler.Observe(plate, state));
        Assert.Equal(OverlapAutoCheckStep.Check, scheduler.Elapsed(plate, state, false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanceledOrFailedLayoutIsNotRetriedUntilItChanges(bool cancel)
    {
        var plate = PlateWithParts();
        var state = new OverlapReportState();
        var scheduler = new OverlapAutoCheckScheduler();
        var request = state.Begin(plate);
        scheduler.Started(plate);
        if (cancel)
            state.Cancel();
        else
            Assert.True(state.TryFail(request, plate));

        Assert.False(scheduler.Observe(plate, state));      // respect the cancel / avoid a failure loop
        Assert.Equal(OverlapAutoCheckStep.None, scheduler.Elapsed(plate, state, false));
        plate.Parts[0].Offset(0.5, 0);
        Assert.True(scheduler.Observe(plate, state));
        Assert.Equal(OverlapAutoCheckStep.Check, scheduler.Elapsed(plate, state, false));
    }

    [Fact]
    public void ResetForgetsPendingWorkAndPreviousRequests()
    {
        var plate = PlateWithParts();
        var state = new OverlapReportState();
        var scheduler = new OverlapAutoCheckScheduler();
        state.Begin(plate);
        scheduler.Started(plate);
        state.Cancel();
        scheduler.Reset();
        Assert.True(scheduler.Observe(plate, state));       // a new document/plate may check again
        scheduler.Reset();
        Assert.False(scheduler.IsWaiting);
        Assert.Equal(OverlapAutoCheckStep.None, scheduler.Elapsed(plate, state, false));
        Assert.False(scheduler.Observe(null, state));
    }

    private static Plate PlateWithParts()
    {
        var plate = new Plate();
        plate.Parts.Add(Rectangle());
        plate.Parts.Add(Rectangle());
        return plate;
    }

    private static Part Rectangle()
    {
        var program = new Program(Mode.Absolute);
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(4, 0));
        program.Codes.Add(new LinearMove(4, 4));
        program.Codes.Add(new LinearMove(0, 4));
        program.Codes.Add(new LinearMove(0, 0));
        return new Part(new Drawing("same name", program));
    }
}
