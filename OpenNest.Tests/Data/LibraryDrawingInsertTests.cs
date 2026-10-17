using System.Security.Cryptography;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Data;
using OpenNest.Geometry;
using OpenNest.Shapes;

namespace OpenNest.Tests.Data;

public class LibraryDrawingInsertTests
{
    private const string Server = "http://library.test:8090";
    private static readonly Guid LibraryId = Guid.Parse("3f1e2d4c-5b6a-4987-8a1b-2c3d4e5f6a7b");
    private static readonly byte[] PackageBytes = { (byte)'P', (byte)'K', 3, 4, 1, 2 };

    private static Nest MatchingNest() => new("Synthetic job")
    {
        Units = Units.Inches,
        Thickness = 0.25,
        Material = new Material("Synthetic Steel", "Grade A"),
    };

    private static DrawingLibraryRecord Record(int revision = 3, string name = "SYN-101", byte[]? package = null) => new()
    {
        Id = LibraryId,
        Revision = revision,
        Name = name,
        Customer = "Synthetic customer",
        MaterialName = "Synthetic Steel",
        MaterialGrade = "Grade A",
        Thickness = 0.25,
        Units = Units.Inches,
        CutType = CutType.Plasma,
        Notes = "Deburr",
        PackageSha256 = Convert.ToHexString(SHA256.HashData(package ?? PackageBytes)).ToLowerInvariant(),
    };

    private static LibraryPackage Package(Units units = Units.Inches, IReadOnlyList<string>? warnings = null)
    {
        var drawing = new RectangleShape { Name = "old package name", Length = 4, Width = 2 }.GetDrawing();
        drawing.Material = new Material("Package material", "Package grade", 0.284);
        drawing.Customer = "Package customer";
        drawing.CutPins.Add(new ContourCutPin(new Vector(4, 2), new LineLeadIn { Length = 0.25 }, new NoLeadOut(),
            new NormalTab { Size = 0.03 }));
        return new LibraryPackage(PackageBytes, drawing, units, warnings);
    }

    [Fact]
    public void Matching_AddsAnExactCopyCarryingTheLinkAndKeepsPins()
    {
        var nest = MatchingNest();
        var package = Package();

        var insert = LibraryDrawingInsert.Plan(nest, Server + "/", Record(), package, 5);

        Assert.Equal(LibraryInsertKind.AddDrawing, insert.Kind);
        Assert.True(insert.KeepPins);
        Assert.Empty(insert.Warnings);
        Assert.Empty(nest.Drawings);

        insert.Apply();

        var drawing = Assert.Single(nest.Drawings);
        Assert.Same(package.Drawing, drawing);
        Assert.Equal(new Vector(4, 2), Assert.Single(drawing.CutPins).Point);
        Assert.Equal(("SYN-101", "Synthetic customer", 5), (drawing.Name, drawing.Customer, drawing.Quantity.Required));
        Assert.Equal(("Synthetic Steel", "Grade A", 0.284), (drawing.Material.Name, drawing.Material.Grade, drawing.Material.Density));
        var link = drawing.Library!;
        Assert.Equal(
            (Server, LibraryId, 3, CutType.Plasma, 0.25, "Synthetic Steel", "Grade A", Units.Inches, "Deburr", Record().PackageSha256),
            (link.ServerKey, link.Id, link.Revision, link.CutType, link.Thickness, link.MaterialName, link.MaterialGrade,
                link.Units, link.Notes, link.PackageSha256));
    }

    [Fact]
    public void SpacingCaseAndThicknessRounding_StillMatch()
    {
        var nest = MatchingNest();
        nest.Material = new Material("  synthetic   STEEL ", "grade a");
        nest.Thickness = 0.250004;

        var insert = LibraryDrawingInsert.Plan(nest, Server, Record(), Package(), 1);

        Assert.True(insert.KeepPins);
        Assert.Empty(insert.Warnings);
    }

    [Theory]
    [InlineData("Other Steel", "Grade A", 0.25, "material Synthetic Steel (nest Other Steel)")]
    [InlineData("Synthetic Steel", "Grade B", 0.25, "grade Grade A (nest Grade B)")]
    [InlineData("Synthetic Steel", "Grade A", 0.1875, "thickness 0.25 (nest 0.1875)")]
    [InlineData("", "", 0.0, "material Synthetic Steel (nest not set)")]
    public void Mismatch_AddsButDropsPinsAndNamesTheDifference(string material, string grade, double thickness, string expected)
    {
        var nest = MatchingNest();
        nest.Material = new Material(material, grade);
        nest.Thickness = thickness;

        var insert = LibraryDrawingInsert.Plan(nest, Server, Record(), Package(), 2);

        Assert.Equal(LibraryInsertKind.AddDrawing, insert.Kind);
        Assert.False(insert.KeepPins);
        var warning = Assert.Single(insert.Warnings);
        Assert.Contains(expected, warning);
        Assert.Contains("'SYN-101'", warning);
        Assert.Contains("cut pins are dropped", warning);
        Assert.Empty(insert.Drawing!.CutPins);
        insert.Apply();
        var drawing = Assert.Single(nest.Drawings);
        Assert.Empty(drawing.CutPins);
        Assert.NotNull(drawing.Library);
    }

    [Fact]
    public void EveryDifference_IsNamed()
    {
        var nest = new Nest("Other job") { Units = Units.Inches, Thickness = 0.1875, Material = new Material("Other Steel", "Grade B") };

        var warning = Assert.Single(LibraryDrawingInsert.Plan(nest, Server, Record(), Package(), 1).Warnings);

        Assert.Contains("material Synthetic Steel (nest Other Steel)", warning);
        Assert.Contains("grade Grade A (nest Grade B)", warning);
        Assert.Contains("thickness 0.25 (nest 0.1875)", warning);
    }

    [Fact]
    public void PackageWarnings_AreShownBeforeTheAdd()
    {
        var insert = LibraryDrawingInsert.Plan(MatchingNest(), Server, Record(),
            Package(warnings: new[] { "reader note" }), 1);

        Assert.Equal(new[] { "reader note" }, insert.Warnings);
        Assert.True(insert.KeepPins);
    }

    [Fact]
    public void DifferentUnits_AreRefused()
    {
        var nest = MatchingNest();
        nest.Units = Units.Millimeters;

        var insert = LibraryDrawingInsert.Plan(nest, Server, Record(), Package(), 1);

        AssertRefused(insert, nest, "inches", "millimeters");
    }

    [Fact]
    public void PackageDisagreeingWithItsRecord_IsRefused()
    {
        var nest = MatchingNest();

        AssertRefused(LibraryDrawingInsert.Plan(nest, Server, Record(), Package(Units.Millimeters), 1), nest, "library record says inches");
        AssertRefused(LibraryDrawingInsert.Plan(nest, Server, Record(package: new byte[] { 9 }), Package(), 1), nest, "does not match");
    }

    [Fact]
    public void SameRevisionAlreadyInTheNest_AddsQuantityOnly()
    {
        var nest = MatchingNest();
        LibraryDrawingInsert.Plan(nest, Server, Record(), Package(), 4).Apply();
        var existing = Assert.Single(nest.Drawings);

        var insert = LibraryDrawingInsert.Plan(nest, Server, Record(), Package(), 3);

        Assert.Equal(LibraryInsertKind.AddQuantity, insert.Kind);
        Assert.Same(existing, insert.Drawing);
        insert.Apply();
        Assert.Same(existing, Assert.Single(nest.Drawings));
        Assert.Equal(7, existing.Quantity.Required);
    }

    [Fact]
    public void SameRevisionRenamedInTheNest_StillAddsQuantity()
    {
        var nest = MatchingNest();
        var first = LibraryDrawingInsert.Plan(nest, Server, Record(), Package(), 1);
        first.Drawing!.Name = "renamed";
        first.Apply();

        var insert = LibraryDrawingInsert.Plan(nest, Server, Record(), Package(), 1);

        Assert.Equal(LibraryInsertKind.AddQuantity, insert.Kind);
    }

    [Fact]
    public void OtherRevisionOfTheSameDrawing_IsRefused()
    {
        var nest = MatchingNest();
        var first = LibraryDrawingInsert.Plan(nest, Server, Record(revision: 2), Package(), 1);
        first.Drawing!.Name = "renamed";
        first.Apply();

        AssertRefused(LibraryDrawingInsert.Plan(nest, Server, Record(revision: 3), Package(), 1), nest,
            "revision 2", "'renamed'", "Update Drawings");
    }

    [Fact]
    public void DifferentDrawingWithTheSameName_IsRefusedNamingBoth()
    {
        var nest = MatchingNest();
        nest.Drawings.Add(new RectangleShape { Name = "SYN-101", Length = 1, Width = 1 }.GetDrawing());

        AssertRefused(LibraryDrawingInsert.Plan(nest, Server, Record(), Package(), 1), nest,
            "'SYN-101' (not from the drawing library)", "library drawing 'SYN-101' (Synthetic customer) revision 3");
    }

    [Fact]
    public void SameRevisionFromAnotherServer_IsADifferentDrawing()
    {
        var nest = MatchingNest();
        LibraryDrawingInsert.Plan(nest, "http://other.test:8090", Record(), Package(), 1).Apply();

        AssertRefused(LibraryDrawingInsert.Plan(nest, Server, Record(), Package(), 1), nest,
            "library revision 3 from http://other.test:8090");
    }

    [Fact]
    public void Apply_RunsOnce()
    {
        var insert = LibraryDrawingInsert.Plan(MatchingNest(), Server, Record(), Package(), 1);
        insert.Apply();

        Assert.Throws<InvalidOperationException>(insert.Apply);
    }

    [Fact]
    public void Plan_RefusesMissingInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LibraryDrawingInsert.Plan(MatchingNest(), Server, Record(), Package(), 0));
        Assert.Throws<ArgumentException>(() => LibraryDrawingInsert.Plan(MatchingNest(), " / ", Record(), Package(), 1));
    }

    private static void AssertRefused(LibraryDrawingInsert insert, Nest nest, params string[] fragments)
    {
        var before = nest.Drawings.Select(d => (d, d.Quantity.Required)).ToList();

        Assert.True(insert.IsRefused);
        Assert.Null(insert.Drawing);
        foreach (var fragment in fragments)
            Assert.Contains(fragment, insert.Refusal);
        Assert.Throws<InvalidOperationException>(insert.Apply);
        Assert.Equal(before, nest.Drawings.Select(d => (d, d.Quantity.Required)).ToList());
    }
}
