namespace OpenNest.Tests;

public class AccumulatingProgressTests
{
    private class CapturingProgress : IProgress<NestProgress>
    {
        public NestProgress Last { get; private set; }
        public void Report(NestProgress value) => Last = value;
    }

    [Fact]
    public void Report_PrependsPreviousParts()
    {
        var inner = new CapturingProgress();
        var previous = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        var accumulating = new AccumulatingProgress(inner, previous);

        var newParts = new List<Part> { TestHelpers.MakePartAt(20, 0, 10) };
        accumulating.Report(new NestProgress { BestParts = newParts, BestPartCount = 1 });

        Assert.NotNull(inner.Last);
        Assert.Equal(2, inner.Last.BestParts.Count);
        Assert.Equal(2, inner.Last.BestPartCount);
    }

    [Fact]
    public void Report_NoPreviousParts_PassesThrough()
    {
        var inner = new CapturingProgress();
        var accumulating = new AccumulatingProgress(inner, new List<Part>());

        var newParts = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        accumulating.Report(new NestProgress { BestParts = newParts, BestPartCount = 1 });

        Assert.NotNull(inner.Last);
        Assert.Single(inner.Last.BestParts);
    }

    [Fact]
    public void Report_NullBestParts_PassesThrough()
    {
        var inner = new CapturingProgress();
        var previous = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        var accumulating = new AccumulatingProgress(inner, previous);

        accumulating.Report(new NestProgress { BestParts = null });

        Assert.NotNull(inner.Last);
        Assert.Null(inner.Last.BestParts);
    }
}
