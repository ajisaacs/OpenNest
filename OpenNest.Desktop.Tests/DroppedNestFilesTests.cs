namespace OpenNest.Desktop.Tests;

public sealed class DroppedNestFilesTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "drop");

    private static string In(string name) => Path.Combine(Folder, name);

    [Fact]
    public void Select_KeepsOnlyNestFiles()
    {
        var selected = DroppedNestFiles.Select(new[]
        {
            In("part.dxf"), In("a.nest"), In("notes.txt"), In("nest"), In("a.nest.bak"),
        });

        Assert.Equal(new[] { In("a.nest") }, selected);
    }

    [Fact]
    public void Select_MatchesTheExtensionInAnyCase()
    {
        var selected = DroppedNestFiles.Select(new[] { In("upper.NEST"), In("mixed.Nest") });

        Assert.Equal(new[] { In("upper.NEST"), In("mixed.Nest") }, selected);
    }

    [Fact]
    public void Select_KeepsTheOrderDropped()
    {
        var selected = DroppedNestFiles.Select(new[] { In("c.nest"), In("a.nest"), In("b.nest") });

        Assert.Equal(new[] { In("c.nest"), In("a.nest"), In("b.nest") }, selected);
    }

    [Fact]
    public void Select_ListsAFileDroppedTwiceOnce_HoweverItsPathIsWritten()
    {
        var selected = DroppedNestFiles.Select(new[]
        {
            In("a.nest"), In("b.nest"), Path.Combine(Folder, ".", "a.nest"), In("a.nest"),
        });

        Assert.Equal(new[] { In("a.nest"), In("b.nest") }, selected);
    }

    [Fact]
    public void Select_ReturnsFullPaths()
    {
        var selected = DroppedNestFiles.Select(new[] { "relative.nest" });

        Assert.Equal(new[] { Path.GetFullPath("relative.nest") }, selected);
    }

    [Fact]
    public void Select_IgnoresMissingAndBlankPaths()
    {
        Assert.Empty(DroppedNestFiles.Select(null));
        Assert.Equal(new[] { In("a.nest") },
            DroppedNestFiles.Select(new[] { null, "", "  ", In("a.nest") }));
    }

    [Fact]
    public void Any_IsTrueOnlyWhenADroppedFileIsANest()
    {
        Assert.True(DroppedNestFiles.Any(new[] { In("part.dxf"), In("a.NEST") }));
        Assert.False(DroppedNestFiles.Any(new[] { In("part.dxf"), In("notes.txt") }));
        Assert.False(DroppedNestFiles.Any(Array.Empty<string>()));
    }
}
