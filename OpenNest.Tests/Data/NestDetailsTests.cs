using OpenNest.CNC;
using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Tests.Data;

public class NestDetailsTests
{
    [Fact]
    public void FromNest_ListsEachPlateWithItsDuplicatesSizePartsAndUtilization()
    {
        var nest = new Nest("Job") { Units = Units.Millimeters };
        var bracket = Rectangle("Bracket", 10, 10);
        var gusset = Rectangle("Gusset", 20, 5);
        nest.Drawings.Add(bracket);
        nest.Drawings.Add(gusset);

        var first = nest.CreatePlate();
        first.Size = new Size(48, 120);
        first.Quantity = 2;
        first.Parts.Add(new Part(bracket));
        first.Parts.Add(new Part(bracket));
        first.Parts.Add(new Part(gusset));
        var cutoff = new CutOff(new Vector(30, 0), CutOffAxis.Vertical);
        first.CutOffs.Add(cutoff);
        first.Parts.Add(new Part(cutoff.Drawing));

        var second = nest.CreatePlate();
        second.Size = new Size(60, 60);
        second.Quantity = 1;

        var details = NestDetails.FromNest(nest);

        Assert.Equal(Units.Millimeters, details.Units);
        Assert.Collection(
            details.Plates,
            plate =>
            {
                Assert.Equal(1, plate.Number);
                Assert.Equal(2, plate.Duplicates);
                Assert.Equal(48, plate.Width);
                Assert.Equal(120, plate.Length);
                Assert.Equal(3, plate.PartCount);
                Assert.Equal(2, plate.DrawingCount);
                Assert.Equal((100 + 100 + 100) / (48.0 * 120), plate.Utilization, 12);
            },
            plate =>
            {
                Assert.Equal(2, plate.Number);
                Assert.Equal(1, plate.Duplicates);
                Assert.Equal(0, plate.PartCount);
                Assert.Equal(0, plate.DrawingCount);
                Assert.Equal(0, plate.Utilization);
            });
    }

    [Fact]
    public void FromNest_KeepsEachPlateForThePreview_InNestOrder()
    {
        var nest = new Nest("Job");
        var first = nest.CreatePlate();
        var second = nest.CreatePlate();
        var third = nest.CreatePlate();

        var details = NestDetails.FromNest(nest);

        Assert.Collection(
            details.PlateLayouts,
            plate => Assert.Same(first, plate),
            plate => Assert.Same(second, plate),
            plate => Assert.Same(third, plate));
    }

    [Fact]
    public void FromNest_CountsNestedDrawingsAcrossPlateDuplicates_AndSkipsCutoffs()
    {
        var nest = new Nest("Job");
        var bracket = Rectangle("Bracket", 10, 10);
        bracket.Customer = "Acme";
        bracket.Quantity.Required = 10;
        var gusset = Rectangle("Gusset", 20, 5);
        gusset.Quantity.Required = 1;
        var unplaced = Rectangle("Spare", 4, 4);
        unplaced.Quantity.Required = 3;
        nest.Drawings.Add(bracket);
        nest.Drawings.Add(gusset);
        nest.Drawings.Add(unplaced);

        var first = nest.CreatePlate();
        first.Quantity = 3;
        first.Parts.Add(new Part(bracket));
        first.Parts.Add(new Part(bracket));
        var second = nest.CreatePlate();
        second.Quantity = 1;
        second.Parts.Add(new Part(bracket));
        second.Parts.Add(new Part(gusset));
        second.Parts.Add(new Part(gusset));
        var cutoff = new CutOff(new Vector(5, 0), CutOffAxis.Vertical);
        second.CutOffs.Add(cutoff);
        second.Parts.Add(new Part(cutoff.Drawing));

        var details = NestDetails.FromNest(nest);

        Assert.Collection(
            details.Drawings,
            drawing =>
            {
                Assert.Equal("Bracket", drawing.Name);
                Assert.Equal("Acme", drawing.Customer);
                Assert.Equal(10, drawing.Required);
                Assert.Equal(2 * 3 + 1, drawing.Nested);
                Assert.Equal(3, drawing.Remaining);
                Assert.Equal(100, drawing.Area, 9);
            },
            drawing =>
            {
                Assert.Equal("Gusset", drawing.Name);
                Assert.Equal(2, drawing.Nested);
                Assert.Equal(0, drawing.Remaining);
            },
            drawing =>
            {
                Assert.Equal("Spare", drawing.Name);
                Assert.Equal(0, drawing.Nested);
                Assert.Equal(3, drawing.Remaining);
            });
    }

    [Fact]
    public void FromNest_ZeroSizePlate_ReportsZeroUtilizationInsteadOfNaN()
    {
        var nest = new Nest("Job");
        var bracket = Rectangle("Bracket", 10, 10);
        nest.Drawings.Add(bracket);
        var plate = nest.CreatePlate();
        plate.Size = new Size(0, 0);
        plate.Parts.Add(new Part(bracket));

        var detail = Assert.Single(NestDetails.FromNest(nest).Plates);

        Assert.Equal(0, detail.Utilization);
        Assert.Equal(1, detail.PartCount);
    }

    [Fact]
    public void FromNest_OfAReadBackArchive_KeepsDuplicatesRequiredQuantitiesAndUnits()
    {
        // The nest browser builds details from the downloaded .nest archive, not a live nest.
        var nest = new Nest("Job") { Units = Units.Millimeters };
        var bracket = Rectangle("Bracket", 10, 10);
        bracket.Quantity.Required = 8;
        nest.Drawings.Add(bracket);
        var plate = nest.CreatePlate();
        plate.Size = new Size(1500, 3000);
        plate.Quantity = 4;
        plate.Parts.Add(new Part(bracket));
        plate.Parts.Add(new Part(bracket, new Vector(20, 0)));

        using var stream = new MemoryStream();
        new OpenNest.IO.NestWriter(nest).Write(stream);
        stream.Position = 0;
        var details = NestDetails.FromNest(new OpenNest.IO.NestReader(stream).Read());

        Assert.Equal(Units.Millimeters, details.Units);
        var plateDetail = Assert.Single(details.Plates);
        Assert.Equal(4, plateDetail.Duplicates);
        Assert.Equal(2, plateDetail.PartCount);
        Assert.Equal(1500, plateDetail.Width);
        Assert.Equal(3000, plateDetail.Length);
        var drawing = Assert.Single(details.Drawings);
        Assert.Equal("Bracket", drawing.Name);
        Assert.Equal(8, drawing.Required);
        Assert.Equal(8, drawing.Nested);
        Assert.Equal(0, drawing.Remaining);
    }

    [Fact]
    public void FromNest_EmptyNest_HasNoRows()
    {
        var details = NestDetails.FromNest(new Nest("Empty"));

        Assert.Empty(details.Plates);
        Assert.Empty(details.Drawings);
    }

    private static Drawing Rectangle(string name, double width, double height)
    {
        var program = new OpenNest.CNC.Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(width, 0)));
        program.Codes.Add(new LinearMove(new Vector(width, height)));
        program.Codes.Add(new LinearMove(new Vector(0, height)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing(name, program);
    }
}
