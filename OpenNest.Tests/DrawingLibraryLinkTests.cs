using OpenNest.Engine;
using OpenNest.Shapes;

namespace OpenNest.Tests;

public class DrawingLibraryLinkTests
{
    private static readonly Guid LibraryId = Guid.Parse("0b3c5a8e-41d2-4c6f-9e0a-7d1b2c3e4f50");

    private static DrawingLibraryLink Link(int revision = 2) => new(
        "http://library.test:8090", LibraryId, revision, CutType.Laser, 0.25,
        "Synthetic Steel", "Grade A", Units.Inches, "", "");

    [Fact]
    public void Constructor_RefusesAnIncompleteLink()
    {
        Assert.Throws<ArgumentException>(() => new DrawingLibraryLink(
            " ", LibraryId, 1, CutType.Laser, 0, "", "", Units.Inches, "", ""));
        Assert.Throws<ArgumentException>(() => new DrawingLibraryLink(
            "http://library.test", Guid.Empty, 1, CutType.Laser, 0, "", "", Units.Inches, "", ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawingLibraryLink(
            "http://library.test", LibraryId, 0, CutType.Laser, 0, "", "", Units.Inches, "", ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawingLibraryLink(
            "http://library.test", LibraryId, 1, (CutType)99, 0, "", "", Units.Inches, "", ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawingLibraryLink(
            "http://library.test", LibraryId, 1, CutType.Laser, double.NaN, "", "", Units.Inches, "", ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawingLibraryLink(
            "http://library.test", LibraryId, 1, CutType.Laser, -0.1, "", "", Units.Inches, "", ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawingLibraryLink(
            "http://library.test", LibraryId, 1, CutType.Laser, 0, "", "", (Units)99, "", ""));
    }

    [Fact]
    public void IsSameRevision_ComparesServerIdAndRevision()
    {
        var link = Link();

        Assert.True(link.IsSameRevision(Link()));
        Assert.False(link.IsSameRevision(Link(revision: 3)));
        Assert.False(link.IsSameRevision(new DrawingLibraryLink(
            "http://other.test:8090", LibraryId, 2, CutType.Laser, 0.25, "", "", Units.Inches, "", "")));
        Assert.False(link.IsSameRevision(null!));
    }

    [Fact]
    public void CanonicalCopy_KeepsTheLink()
    {
        var drawing = new RectangleShape { Name = "LINKED", Length = 10, Width = 5 }.GetDrawing();
        drawing.Library = Link();

        var copy = CanonicalFrame.AsCanonicalCopy(drawing);

        Assert.Same(drawing.Library, copy.Library);
    }

    [Fact]
    public void SplitPieces_AreNotTheLibraryDrawing()
    {
        var drawing = new RectangleShape { Name = "LINKED", Length = 100, Width = 50 }.GetDrawing();
        drawing.Library = Link();

        var pieces = DrawingSplitter.Split(drawing,
            new List<SplitLine> { new(50.0, CutOffAxis.Vertical) },
            new SplitParameters { Type = SplitType.Straight });

        Assert.Equal(2, pieces.Count);
        Assert.All(pieces, piece => Assert.Null(piece.Library));
        Assert.NotNull(drawing.Library);
    }
}
