using OpenNest.Data;

using Vector = OpenNest.Geometry.Vector;

namespace OpenNest.Tests.Data;

public class NestRecordFactoryTests
{
    [Fact]
    public void FromNest_CountsPlatesAndNonCutoffParts()
    {
        var nest = new Nest("Job 42")
        {
            Customer = "Cozy Cat",
            Material = new Material("Aluminum"),
            Thickness = 0.125,
            Status = NestStatus.ToBeCut,
            MadeBy = "AJ",
            Notes = "rush order",
            DateCreated = new DateTime(2026, 1, 1),
            DateLastModified = new DateTime(2026, 2, 2),
        };

        var plate1 = nest.CreatePlate();
        var drawingA = new Drawing("A");
        var drawingB = new Drawing("B");
        nest.Drawings.Add(drawingA);
        nest.Drawings.Add(drawingB);
        plate1.Parts.Add(new Part(drawingA));
        plate1.Parts.Add(new Part(drawingB));

        var plate2 = nest.CreatePlate();
        var drawingC = new Drawing("C");
        nest.Drawings.Add(drawingC);
        plate2.Parts.Add(new Part(drawingC));
        var cutoff = new CutOff(new Vector(0, 0), CutOffAxis.Vertical);
        plate2.CutOffs.Add(cutoff);
        plate2.Parts.Add(new Part(cutoff.Drawing));

        var record = NestRecordFactory.FromNest(nest, Guid.Empty, fileSize: 1024);

        Assert.Equal(Guid.Empty, record.Id);
        Assert.Equal("Job 42", record.Name);
        Assert.Equal("Cozy Cat", record.Customer);
        Assert.Equal("Aluminum", record.Material);
        Assert.Equal(0.125, record.Thickness);
        Assert.Equal(NestStatus.ToBeCut, record.Status);
        Assert.Equal("AJ", record.MadeBy);
        Assert.Equal("rush order", record.Comments);
        Assert.Equal(new DateTime(2026, 1, 1), record.DateCreated);
        Assert.Equal(new DateTime(2026, 2, 2), record.DateModified);
        Assert.Equal(2, record.PlateCount);
        // 2 real parts on plate1 + 1 real part on plate2; the cutoff part is excluded.
        Assert.Equal(3, record.PartCount);
        Assert.Equal(1024, record.FileSize);
    }

    [Fact]
    public void FromNest_EmptyNest_ZeroCounts()
    {
        var nest = new Nest("Empty");

        var record = NestRecordFactory.FromNest(nest, Guid.NewGuid(), fileSize: 0);

        Assert.Equal(0, record.PlateCount);
        Assert.Equal(0, record.PartCount);
        Assert.Equal("", record.Material);
        Assert.Equal("", record.Comments);
        Assert.Equal("", record.MadeBy);
    }

    [Fact]
    public void FromNest_PreservesGivenId()
    {
        var id = Guid.NewGuid();
        var nest = new Nest("Keep Id");

        var record = NestRecordFactory.FromNest(nest, id, fileSize: 5);

        Assert.Equal(id, record.Id);
    }
}
