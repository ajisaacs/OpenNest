using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using OpenNest;
using OpenNest.Geometry;
using OpenNest.IO;
using CncProgram = OpenNest.CNC.Program;

namespace OpenNest.SyntheticNests;

internal static class Program
{
    private static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var output = args.Length == 1 ? args[0] : "test-data/synthetic-nests";
        Directory.CreateDirectory(output);
        Write(output, "single-triangle", new Size(20, 20),
            Drawing("triangle", Polyline((0, 0), (6, 0), (2, 5)), 1));
        Write(output, "paired-wedges", new Size(15, 30),
            Drawing("wedge", Polyline((0, 0), (8, 0), (6, 20), (0, 20)), 2));
        Write(output, "repeated-ell-fill", new Size(40, 60), Drawing("ell", Ell(), 7));
        Write(output, "mixed-irregular", new Size(25, 30),
            Drawing("triangle", Polyline((0, 0), (6, 0), (2, 5)), 1),
            Drawing("ell", Ell(), 2),
            Drawing("pentagon", Polyline((0, 0), (4, 0), (5, 3), (2, 5), (0, 3)), 3));
        Write(output, "rotation-edge-fit", new Size(8.52, 3.52),
            Drawing("long-bar", Rectangle(8, 3), 1));
        Write(output, "multi-sheet", new Size(10, 10), Drawing("square", Rectangle(8, 8), 3));
    }

    private static CncProgram Ell() =>
        Polyline((0, 0), (9, 0), (9, 3), (3, 3), (3, 7), (0, 7));

    private static CncProgram Rectangle(double x, double y) =>
        Polyline((0, 0), (x, 0), (x, y), (0, y));

    private static CncProgram Polyline(params (double X, double Y)[] points)
    {
        var program = new CncProgram();
        program.MoveTo(points[0].X, points[0].Y);
        foreach (var point in points.Skip(1))
            program.LineTo(point.X, point.Y);
        program.LineTo(points[0].X, points[0].Y);
        return program;
    }

    private static Drawing Drawing(string name, CncProgram program, int quantity)
    {
        var drawing = new Drawing(name, program);
        drawing.Quantity.Required = quantity;
        drawing.Constraints.AllowAnyRotation();
        return drawing;
    }

    private static void Write(string output, string name, Size size, params Drawing[] drawings)
    {
        var nest = new Nest(name) { SalvageRate = 0, Units = Units.Inches };
        nest.PlateDefaults.Size = size;
        nest.PlateDefaults.Quadrant = 1;
        nest.PlateDefaults.PartSpacing = 0.25;
        nest.PlateDefaults.EdgeSpacing = new Spacing(0.25, 0.25);
        foreach (var drawing in drawings)
            nest.Drawings.Add(drawing);
        var path = Path.Combine(output, name + ".nest");
        if (!new NestWriter(nest).Write(path))
            throw new IOException("Fixture write failed: " + name);

        // Remove wall-clock metadata so regeneration is byte-stable. No customer data is read.
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = archive.GetEntry("nest.json");
        JsonNode metadata;
        using (var reader = new StreamReader(entry.Open()))
            metadata = JsonNode.Parse(reader.ReadToEnd());
        metadata["dateCreated"] = "2026-01-01T00:00:00";
        metadata["dateLastModified"] = "2026-01-01T00:00:00";
        entry.Delete();
        entry = archive.CreateEntry("nest.json");
        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            writer.Write(metadata.ToJsonString());
        foreach (var item in archive.Entries)
            item.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Console.WriteLine(name);
    }
}
