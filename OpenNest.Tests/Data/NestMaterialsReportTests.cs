using OpenNest.CNC;
using OpenNest.CutOffs;
using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Tests.Data;

public class NestMaterialsReportTests
{
    [Fact]
    public void Add_GroupsSheetsByMaterialThicknessAndSize_AcrossNests()
    {
        var report = new NestMaterialsReport();
        report.Add(Record("4734-01", "Mild Steel", "A36", 0.25),
            NestWithPlates((48, 96, 2), (60, 120, 1)));
        report.Add(Record("4734-02", "mild steel", "a36", 0.25),
            NestWithPlates((48, 96, 3)));
        report.Add(Record("4734-03", "Aluminum", "5052", 0.125),
            NestWithPlates((48, 96, 1)));

        Assert.Collection(
            report.Rows,
            row =>
            {
                Assert.Equal("Aluminum", row.Material);
                Assert.Equal(0.125, row.Thickness);
                Assert.Equal(1, row.Quantity);
                Assert.Equal("4734-03", row.NestList);
            },
            row =>
            {
                Assert.Equal("Mild Steel", row.Material);
                Assert.Equal((48.0, 96.0), (row.Width, row.Length));
                Assert.Equal(5, row.Quantity);
                Assert.Equal("4734-01 (2), 4734-02 (3)", row.NestList);
            },
            row =>
            {
                Assert.Equal((60.0, 120.0), (row.Width, row.Length));
                Assert.Equal(1, row.Quantity);
                Assert.Equal("4734-01", row.NestList);
            });
        Assert.Equal(3, report.NestNames.Count);
    }

    [Fact]
    public void Add_SkipsPlatesWithoutParts_AndCutoffOnlyPlates()
    {
        var nest = NestWithPlates((48, 96, 1));
        nest.CreatePlate().Size = new Size(60, 120);
        var cutoffOnly = nest.CreatePlate();
        cutoffOnly.Size = new Size(72, 144);
        var cutoff = new CutOff(new Vector(30, 0), CutOffAxis.Vertical);
        cutoffOnly.CutOffs.Add(cutoff);
        cutoffOnly.Parts.Add(new Part(cutoff.Drawing));

        var report = new NestMaterialsReport();
        report.Add(Record("Job", "Steel", "", 0.5), nest);

        var row = Assert.Single(report.Rows);
        Assert.Equal((48.0, 96.0), (row.Width, row.Length));
    }

    [Fact]
    public void ToCsv_QuotesFieldsContainingCommas()
    {
        var report = new NestMaterialsReport();
        report.Add(Record("A", "Steel, HR", "", 0.25), NestWithPlates((48, 96, 1)));
        report.Add(Record("B", "Steel, HR", "", 0.25), NestWithPlates((48, 96, 1)));

        var lines = report.ToCsv().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Material,Grade,Thickness,Units,Width,Length,Quantity,Nests", lines[0]);
        Assert.Equal("\"Steel, HR\",,0.25,in,48,96,2,\"A, B\"", lines[1]);
    }

    private static NestRecord Record(string name, string material, string grade, double thickness) =>
        new() { Id = Guid.NewGuid(), Name = name, Material = material, MaterialGrade = grade, Thickness = thickness };

    private static Nest NestWithPlates(params (double Width, double Length, int Quantity)[] plates)
    {
        var nest = new Nest("Job") { Units = Units.Inches };
        var drawing = Rectangle("Part", 10, 10);
        nest.Drawings.Add(drawing);
        foreach (var (width, length, quantity) in plates)
        {
            var plate = nest.CreatePlate();
            plate.Size = new Size(width, length);
            plate.Quantity = quantity;
            plate.Parts.Add(new Part(drawing));
        }

        return nest;
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
