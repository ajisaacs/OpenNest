using System.Text.Json;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Reporting;

namespace OpenNest.Tests.Reporting;

public class NestReportBuilderTests
{
    [Fact]
    public void Capture_ReconcilesOrderedReferenceUnionWithoutCountingCutoffs()
    {
        var nest = NestReportTestData.CreateNest();
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);

        Assert.Equal("Report test job", snapshot.Name);
        Assert.Equal("Test customer", snapshot.Customer);
        Assert.Equal("Synthetic report test data", snapshot.Notes);
        Assert.Equal("Steel", snapshot.Material);
        Assert.Equal("A36", snapshot.Grade);
        Assert.Equal(0.125, snapshot.Thickness);
        Assert.Equal("in", snapshot.Units);
        Assert.Equal(NestReportTestData.GeneratedAt, snapshot.GeneratedAt);
        Assert.Equal(new[] { "R001", "R002", "R003", "R004" }, snapshot.Drawings.Select(d => d.Id));
        Assert.Equal(new[] { "Bracket", "Rotated", "Bracket", "Unplaced" }, snapshot.Drawings.Select(d => d.Name));
        Assert.Equal(new long[] { 5, 1, 7, 3 }, snapshot.Drawings.Select(d => d.Required));
        Assert.Equal(new long[] { 4, 2, 2, 0 }, snapshot.Drawings.Select(d => d.Nested));
        Assert.Equal(new long[] { 1, 0, 5, 3 }, snapshot.Drawings.Select(d => d.Shortage));
        Assert.Equal(new long[] { 0, 1, 0, 0 }, snapshot.Drawings.Select(d => d.Extra));
        Assert.All(snapshot.Drawings.Take(3), d => Assert.Equal(new[] { 1 }, d.Plates));
        Assert.Empty(snapshot.Drawings[3].Plates);
        var plate = Assert.Single(snapshot.Plates);
        Assert.Equal(new[] { "R001", "R001", "R002", "R003" }, plate.Parts.Select(p => p.ReportId));
        Assert.Single(plate.Cutoffs);
        Assert.Equal(2, snapshot.TotalSheets);
        Assert.Equal(new ReportBounds(0, 0, 48, 24), plate.Bounds);
        Assert.Equal(0.125, plate.PartSpacing);
        Assert.Equal(nest.Plates[0].Utilization(), plate.Utilization);
    }

    [Fact]
    public void Capture_RecountsAcrossPlateCopiesWithWideIntegersAndUniquePlateNumbers()
    {
        var nest = NestReportTestData.CreateNest();
        var drawing = nest.Plates[0].Parts[0].BaseDrawing;
        // Change copies after adding: the legacy cached Nested count stays stale.
        nest.Plates[0].Quantity = int.MaxValue;
        var second = new Plate(24, 48) { Quantity = 3 };
        second.Parts.Add(new Part(drawing));
        second.Parts.Add(new Part(drawing));
        nest.Plates.Add(second);
        var cached = drawing.Quantity.Nested;

        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);

        Assert.Equal(checked(2L * int.MaxValue + 6), snapshot.Drawings[0].Nested);
        Assert.Equal(new[] { 1, 2 }, snapshot.Drawings[0].Plates);
        Assert.Equal(checked((long)int.MaxValue + 3), snapshot.TotalSheets);
        Assert.Equal(cached, drawing.Quantity.Nested);
    }

    [Fact]
    public void Capture_IsRepeatableDoesNotMutateSourcesAndRetainsNoMutableReferences()
    {
        var nest = NestReportTestData.CreateNest();
        var part = nest.Plates[0].Parts[0];
        var drawing = part.BaseDrawing;
        var source = Fingerprint(nest);
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);
        var serialized = JsonSerializer.Serialize(snapshot);

        Assert.Equal(source, Fingerprint(nest));
        Assert.Equal(serialized, JsonSerializer.Serialize(NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt)));
        Assert.NotEmpty(snapshot.Plates);
        nest.Name = "Edited";
        nest.Material.Name = "Edited material";
        nest.Plates[0].Size = new Size(100, 200);
        nest.Plates[0].Quantity = 42;
        part.Location = new Vector(500, 600);
        ((LinearMove)part.Program.Codes[1]).EndPoint = new Vector(99, 99);
        ((LinearMove)drawing.Program.Codes[1]).EndPoint = new Vector(88, 88);
        drawing.Name = "Edited drawing";
        drawing.Quantity.Required = 123;
        nest.Drawings.Clear();
        nest.Plates.Clear();
        Assert.Equal(serialized, JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public void Capture_EmptyAndDemandOnlyJobsKeepExplicitEmptyCollectionsAndOrdinalOrder()
    {
        var nest = new Nest("Empty test");
        var empty = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);
        Assert.Equal("Empty test", empty.Name);
        Assert.Empty(empty.Drawings);
        Assert.Empty(empty.Plates);
        Assert.Equal(0, empty.TotalSheets);
        nest.Drawings.Add(NestReportTestData.Rectangle("zeta", 1, 1, 5));
        nest.Drawings.Add(NestReportTestData.Rectangle("Alpha", 1, 1, 2));
        var demand = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);
        Assert.Equal(new[] { "Alpha", "zeta" }, demand.Drawings.Select(d => d.Name));
        Assert.All(demand.Drawings, d => { Assert.Equal(0, d.Nested); Assert.Equal(d.Required, d.Shortage); });
        Assert.Empty(demand.Plates);
    }

    [Theory]
    [InlineData(0, 48, 1)]
    [InlineData(24, -1, 1)]
    [InlineData(double.NaN, 48, 1)]
    [InlineData(24, double.PositiveInfinity, 1)]
    [InlineData(24, 48, 0)]
    [InlineData(24, 48, -1)]
    public void Capture_RejectsInvalidPlateBeforeProducingSnapshot(double width, double length, int copies)
    {
        var nest = NestReportTestData.CreateNest();
        nest.Plates[0].Size = new Size(width, length);
        nest.Plates[0].Quantity = copies;
        var before = Fingerprint(nest);
        var error = Assert.Throws<InvalidOperationException>(() => NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt));
        Assert.Contains("Plate 1", error.Message);
        Assert.Equal(before, Fingerprint(nest));
    }

    [Theory]
    [InlineData(1, 0, 0, 48, 24)]
    [InlineData(2, -48, 0, 0, 24)]
    [InlineData(3, -48, -24, 0, 0)]
    [InlineData(4, 0, -24, 48, 0)]
    public void Capture_UsesQuadrantAwareStockBounds(int quadrant, double left, double bottom, double right, double top)
    {
        var nest = NestReportTestData.CreateNest();
        nest.Plates[0].Quadrant = quadrant;
        var snapshot = NestReportBuilder.Capture(nest, NestReportTestData.GeneratedAt);
        Assert.Equal(new ReportBounds(left, bottom, right, top), Assert.Single(snapshot.Plates).Bounds);
    }

    internal static string Fingerprint(Nest nest) => JsonSerializer.Serialize(new
    {
        nest.Name,
        nest.Customer,
        nest.Notes,
        nest.Material,
        nest.Thickness,
        nest.Units,
        nest.DateCreated,
        nest.DateLastModified,
        Drawings = nest.Drawings.Select(d => new { d.Id, d.Name, d.Quantity, Main = NestWriter.GetProgramText(d.Program), Subs = NestWriter.GetSubProgramsText(d.Program) }),
        Plates = nest.Plates.Select(p => new
        {
            p.Size,
            p.Quantity,
            p.PartSpacing,
            p.Quadrant,
            Parts = p.Parts.Select(part => new
            {
                part.BaseDrawing.Id,
                part.Location,
                part.Rotation,
                part.HasManualLeadIns,
                part.LeadInsLocked,
                Main = NestWriter.GetProgramText(part.Program),
                Subs = NestWriter.GetSubProgramsText(part.Program),
            }),
        }),
    }, new JsonSerializerOptions { IncludeFields = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
}
