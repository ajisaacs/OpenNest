using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.CutOffs;

/// <summary>
/// A cut-off's place in <c>Plate.Parts</c> is its cut sequence number, which the
/// user sets and the posts follow. Regenerating cut-offs (after a part drag, fill
/// or cut-off move) and saving must keep it.
/// </summary>
public class CutOffSequenceTests
{
    private static Drawing MakeSquare()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("square", pgm);
    }

    /// <summary>Three parts along X with vertical cut-offs between them, the
    /// cut-offs sequenced as 2 and 4: part, cut, part, cut, part.</summary>
    private static Plate MakeSequencedPlate(Drawing drawing)
    {
        var plate = new Plate(60, 120);
        plate.Parts.Add(new Part(drawing, new Vector(1, 2)));
        plate.Parts.Add(new Part(drawing, new Vector(30, 2)));
        plate.Parts.Add(new Part(drawing, new Vector(60, 2)));

        plate.CutOffs.Add(new CutOff(new Vector(20, 0), CutOffAxis.Vertical));
        plate.CutOffs.Add(new CutOff(new Vector(50, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());

        // As ActionSetSequence does: remove, then insert at the chosen number.
        SetSequence(plate, plate.CutOffs[0], 1);
        SetSequence(plate, plate.CutOffs[1], 3);
        return plate;
    }

    private static void SetSequence(Plate plate, CutOff cutOff, int index)
    {
        var part = plate.Parts.First(p => ReferenceEquals(p.BaseDrawing, cutOff.Drawing));
        plate.Parts.Remove(part);
        plate.Parts.Insert(index, part);
    }

    /// <summary>The cut sequence as names: "part" or the cut-off's X position.</summary>
    private static string[] Sequence(Plate plate) =>
        plate
            .Parts.Select(p =>
                p.BaseDrawing.IsCutOff ? $"cut@{p.BoundingBox.X:F0}" : $"part@{p.Location.X:F0}"
            )
            .ToArray();

    private static readonly string[] Expected =
    {
        "part@1",
        "cut@20",
        "part@30",
        "cut@50",
        "part@60",
    };

    [Fact]
    public void RegenerateCutOffs_KeepsEachCutOffInItsSequencePlace()
    {
        var plate = MakeSequencedPlate(MakeSquare());
        Assert.Equal(Expected, Sequence(plate));

        plate.RegenerateCutOffs(new CutOffSettings());

        Assert.Equal(Expected, Sequence(plate));
    }

    [Fact]
    public void RegenerateCutOffs_AddsNewCutOffAtTheEnd()
    {
        var plate = MakeSequencedPlate(MakeSquare());

        plate.CutOffs.Add(new CutOff(new Vector(0, 40), CutOffAxis.Horizontal));
        plate.RegenerateCutOffs(new CutOffSettings());

        Assert.Equal(Expected, Sequence(plate).Take(5));
        Assert.Equal(6, plate.Parts.Count);
        Assert.Same(plate.CutOffs[2].Drawing, plate.Parts[5].BaseDrawing);
    }

    [Fact]
    public void SaveAndReopen_KeepsCutOffSequence()
    {
        var drawing = MakeSquare();
        var nest = new Nest("seq") { DateCreated = DateTime.Now, DateLastModified = DateTime.Now };
        nest.Drawings.Add(drawing);
        nest.Plates.Add(MakeSequencedPlate(drawing));

        using var stream = new MemoryStream();
        new NestWriter(nest).Write(stream);
        stream.Position = 0;
        var loaded = new NestReader(stream).Read();

        Assert.Equal(Expected, Sequence(loaded.Plates[0]));
    }

    [Fact]
    public void RegenerateCutOffs_OutOfRangeSequence_AppendsInsteadOfThrowing()
    {
        // A damaged file can name a sequence past the end of the plate.
        var plate = new Plate(60, 120);
        plate.Parts.Add(new Part(MakeSquare(), new Vector(1, 2)));
        var cutOff = new CutOff(new Vector(20, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutOff);

        plate.RegenerateCutOffs(new CutOffSettings(), new Dictionary<CutOff, int> { [cutOff] = 99 });

        Assert.Equal(2, plate.Parts.Count);
        Assert.Same(cutOff.Drawing, plate.Parts[1].BaseDrawing);
    }
}
