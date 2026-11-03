using OpenNest.Engine.Jobs.Remote;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Version 2 job documents nest higher priority numbers first. Version 1 documents came from
/// desktops that nested lower numbers first and are converted when read, so a job queued before
/// an upgrade keeps its order.
/// </summary>
public class NestJobDocumentPriorityTests
{
    [Fact]
    public void CurrentVersionKeepsPriority()
    {
        var item = Assert.Single(Document(NestJobDocument.CurrentVersion, 3).ToRequest(Drawings()).Items);

        Assert.Equal(3, item.Priority);
    }

    [Fact]
    public void VersionOneConvertsLowerFirstPriority()
    {
        var json = Document(1, 3).ToJson();

        var item = Assert.Single(NestJobDocument.FromJson(json).ToRequest(Drawings()).Items);

        Assert.Equal(-3, item.Priority);
    }

    [Fact]
    public void UnknownVersionIsRefused()
    {
        Assert.Throws<FormatException>(() => Document(NestJobDocument.CurrentVersion + 1, 0).ToRequest(Drawings()));
    }

    private static Drawing[] Drawings() => [new Drawing("a", TestDrawingFactory.Rectangle(10, 20))];

    private static NestJobDocument Document(int version, int priority) => new()
    {
        Version = version,
        EngineName = "Rectangles",
        DrawingNames = ["a"],
        Items = [new NestJobDocument.ItemJson { Drawing = 0, Quantity = 1, Priority = priority }],
    };
}
