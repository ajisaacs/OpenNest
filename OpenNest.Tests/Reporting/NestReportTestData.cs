using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.Reporting;

/// <summary>Small, synthetic report examples; never production job data.</summary>
public static class NestReportTestData
{
    public static DateTimeOffset GeneratedAt { get; } = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    public static Nest CreateNest()
    {
        var nest = new Nest("Report test job")
        {
            Customer = "Test customer",
            Notes = "Synthetic report test data",
            Material = new Material("Steel", "A36"),
            Thickness = 0.125,
            Units = Units.Inches,
            DateCreated = new DateTime(2026, 9, 1),
            DateLastModified = new DateTime(2026, 9, 28),
        };
        var holed = CreateHoledDrawing("Bracket", 5);
        var rotated = Rectangle("Rotated", 4, 2, 1);
        var sameName = Rectangle("Bracket", 3, 2, 7);
        var unplaced = Rectangle("Unplaced", 2, 1, 3);
        nest.Drawings.Add(unplaced);
        nest.Drawings.Add(rotated);
        nest.Drawings.Add(holed);
        // DrawingCollection is name-keyed; this reference deliberately exists only in placements.
        var plate = new Plate(24, 48) { Quantity = 2, PartSpacing = 0.125 };
        plate.Parts.Add(new Part(holed, new Vector(2, 2)));
        plate.Parts.Add(new Part(holed, new Vector(14, 2)));
        var turned = new Part(rotated);
        turned.Rotate(System.Math.PI / 2);
        turned.Location = new Vector(30, 2);
        plate.Parts.Add(turned);
        plate.Parts.Add(new Part(sameName, new Vector(32, 2)));
        var cutoffProgram = new Program();
        cutoffProgram.Codes.Add(new RapidMove(40, 0));
        cutoffProgram.Codes.Add(new LinearMove(40, 24));
        var cutoff = new Drawing("Cutoff test", cutoffProgram) { IsCutOff = true };
        cutoff.Quantity.Required = 99;
        plate.Parts.Add(new Part(cutoff));
        nest.Drawings.Add(cutoff);
        nest.Plates.Add(plate);
        return nest;
    }

    /// <summary>
    /// Four layouts: "Bracket" R001 on plates 1, 2 and 4; R002 on plates 1 and 3; the distinct
    /// same-named "Bracket" R003 on plates 1 and 3; R004 demanded but unplaced.
    /// </summary>
    public static Nest CreateMultiPlateNest()
    {
        var nest = CreateNest();
        var first = nest.Plates[0];
        var holed = first.Parts[0].BaseDrawing;
        var rotated = first.Parts[2].BaseDrawing;
        var second = new Plate(24, 48) { Quantity = 3, PartSpacing = 0.25 };
        second.Parts.Add(new Part(holed, new Vector(2, 2)));
        nest.Plates.Add(second);
        var third = new Plate(30, 60) { Quantity = 1, PartSpacing = 0.125 };
        third.Parts.Add(new Part(rotated, new Vector(10, 2)));
        third.Parts.Add(new Part(first.Parts[3].BaseDrawing, new Vector(20, 2)));
        nest.Plates.Add(third);
        var fourth = new Plate(24, 48) { Quantity = 1 };
        fourth.Parts.Add(new Part(holed, new Vector(20, 8)));
        nest.Plates.Add(fourth);
        return nest;
    }

    /// <summary>
    /// One 120 x 60 sheet: two large parts and a cluster of tiny holed washers whose IDs cannot
    /// be labeled legibly at overview scale, so they need detail views.
    /// </summary>
    public static Nest CreateDenseNest()
    {
        var nest = new Nest("Dense label test") { Units = Units.Inches };
        var large = CreateHoledDrawing("Large bracket", 2);
        var washer = new Drawing("Washer", HoledSquare(1, 0.25));
        washer.Quantity.Required = 24;
        nest.Drawings.Add(large);
        nest.Drawings.Add(washer);
        var plate = new Plate(60, 120) { Quantity = 2, PartSpacing = 0.25 };
        plate.Parts.Add(new Part(large, new Vector(5, 5)));
        plate.Parts.Add(new Part(large, new Vector(20, 5)));
        for (var row = 0; row < 4; row++)
            for (var column = 0; column < 6; column++)
                plate.Parts.Add(new Part(washer, new Vector(62 + column * 1.5, 30 + row * 1.5)));
        nest.Plates.Add(plate);
        return nest;
    }

    public static Nest CreateTabbedNest()
    {
        var drawing = CreateHoledDrawing("Tabbed with hole", 1);
        var part = new Part(drawing);
        part.Rotate(System.Math.PI / 2);
        part.Offset(20, 5);
        part.ApplyLeadIns(new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 },
            ExternalLeadOut = new LineLeadOut { Length = 0.25 },
            ArcCircleLeadIn = new LineLeadIn { Length = 0.3, ApproachAngle = 90 },
            TabsEnabled = true,
            TabConfig = new NormalTab { Size = 0.15 },
        }, new Vector(-5, -5));
        part.LeadInsLocked = true;
        var nest = new Nest("Tabbed report test") { Units = Units.Inches };
        nest.Drawings.Add(drawing);
        var plate = new Plate(24, 48) { Quantity = 1 };
        plate.Parts.Add(part);
        nest.Plates.Add(plate);
        return nest;
    }

    public static Drawing CreateHoledDrawing(string name, int required)
    {
        var program = RectangleProgram(10, 10);
        program.Codes.Add(new RapidMove(6, 5));
        program.Codes.Add(new ArcMove(new Vector(6, 5), new Vector(5, 5), RotationType.CW));
        var drawing = new Drawing(name, program);
        drawing.Quantity.Required = required;
        return drawing;
    }

    private static Program HoledSquare(double size, double radius)
    {
        var program = RectangleProgram(size, size);
        var center = size / 2;
        program.Codes.Add(new RapidMove(center + radius, center));
        program.Codes.Add(new ArcMove(new Vector(center + radius, center), new Vector(center, center), RotationType.CW));
        return program;
    }

    public static Drawing Rectangle(string name, double length, double width, int required = 1)
    {
        var drawing = new Drawing(name, RectangleProgram(length, width));
        drawing.Quantity.Required = required;
        return drawing;
    }

    private static Program RectangleProgram(double length, double width)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(0, width));
        program.Codes.Add(new LinearMove(length, width));
        program.Codes.Add(new LinearMove(length, 0));
        program.Codes.Add(new LinearMove(0, 0));
        return program;
    }
}
