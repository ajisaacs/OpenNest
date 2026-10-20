namespace OpenNest.IO.Tests;

/// <summary>
/// An ordinary save records when it happened; a converter can keep the source system's
/// modified date through write and read.
/// </summary>
public class NestWriterModifiedDateTests
{
    private static readonly DateTime SourceDate = new(2019, 5, 2, 13, 50, 15, DateTimeKind.Unspecified);

    [Fact]
    public void Write_StampsTheSaveTimeByDefault()
    {
        var nest = new Nest("Stamped") { DateLastModified = SourceDate };
        var before = DateTime.Now;

        var restored = RoundTrip(new NestWriter(nest));

        Assert.InRange(restored.DateLastModified, before.AddSeconds(-1), DateTime.Now.AddSeconds(1));
        Assert.Equal(restored.DateLastModified, nest.DateLastModified);
    }

    [Fact]
    public void Write_PreserveDateLastModified_KeepsTheNestsOwnDate()
    {
        var nest = new Nest("Preserved") { DateLastModified = SourceDate };

        var restored = RoundTrip(new NestWriter(nest) { PreserveDateLastModified = true });

        Assert.Equal(SourceDate, restored.DateLastModified);
        Assert.Equal(SourceDate, nest.DateLastModified);
    }

    private static Nest RoundTrip(NestWriter writer)
    {
        using var stream = new MemoryStream();
        Assert.True(writer.Write(stream));
        stream.Position = 0;
        return new NestReader(stream).Read();
    }
}
