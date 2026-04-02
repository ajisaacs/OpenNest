using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingStrategy;

public class LeadInAssignerTests
{
    private static Part MakeSquarePartAt(double x, double y)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("test", pgm);
        return new Part(drawing, new Vector(x, y));
    }

    [Fact]
    public void Assign_SetsHasManualLeadInsOnAllParts()
    {
        var plate = new Plate(60, 120);
        plate.Parts.Add(MakeSquarePartAt(10, 10));
        plate.Parts.Add(MakeSquarePartAt(30, 30));
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);

        Assert.All(plate.Parts, p => Assert.True(p.HasManualLeadIns));
    }

    [Fact]
    public void Assign_SkipsLockedParts()
    {
        var plate = new Plate(60, 120);
        var lockedPart = MakeSquarePartAt(10, 10);
        lockedPart.LeadInsLocked = true;
        lockedPart.HasManualLeadIns = true;
        var originalProgram = lockedPart.Program;

        plate.Parts.Add(lockedPart);
        plate.Parts.Add(MakeSquarePartAt(30, 30));
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);

        Assert.Same(originalProgram, lockedPart.Program);
    }

    [Fact]
    public void Assign_RemovesExistingLeadInsBeforeReapply()
    {
        var plate = new Plate(60, 120);
        plate.Parts.Add(MakeSquarePartAt(10, 10));
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);
        var countAfterFirst = plate.Parts[0].Program.Codes.Count;

        assigner.Assign(plate);
        var countAfterSecond = plate.Parts[0].Program.Codes.Count;

        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    [Fact]
    public void Assign_PartsContainLeadinLayerCodes()
    {
        var plate = new Plate(60, 120);
        plate.Parts.Add(MakeSquarePartAt(10, 10));
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);

        var hasLeadin = plate.Parts[0].Program.Codes.OfType<LinearMove>().Any(m => m.Layer == LayerType.Leadin);
        Assert.True(hasLeadin);
    }

    [Fact]
    public void Assign_PreservesRotationOnRotatedParts()
    {
        var drawing = MakeSquareDrawing();
        var rotation = System.Math.PI / 2; // 90 degrees
        var part = Part.CreateAtOrigin(drawing, rotation);
        part.Offset(10, 10);

        var plate = new Plate(60, 120);
        plate.Parts.Add(part);
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);

        Assert.Equal(rotation, part.Rotation, 6);
    }

    [Fact]
    public void Assign_ThenRemove_PreservesRotation()
    {
        var drawing = MakeSquareDrawing();
        var rotation = System.Math.PI / 4; // 45 degrees
        var part = Part.CreateAtOrigin(drawing, rotation);
        part.Offset(15, 15);

        var plate = new Plate(60, 120);
        plate.Parts.Add(part);
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);
        Assert.Equal(rotation, part.Rotation, 6);

        part.RemoveLeadIns();
        Assert.Equal(rotation, part.Rotation, 6);
    }

    [Fact]
    public void Assign_PreservesLocationOnRotatedParts()
    {
        var drawing = MakeSquareDrawing();
        var part = Part.CreateAtOrigin(drawing, System.Math.PI / 2);
        part.Offset(20, 30);
        var location = part.Location;

        var plate = new Plate(60, 120);
        plate.Parts.Add(part);
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);

        Assert.Equal(location.X, part.Location.X, 6);
        Assert.Equal(location.Y, part.Location.Y, 6);
    }

    [Fact]
    public void Assign_ThenRemove_RestoresBoundingBox()
    {
        var drawing = MakeSquareDrawing();
        var rotation = System.Math.PI / 2;
        var part = Part.CreateAtOrigin(drawing, rotation);
        part.Offset(10, 10);
        var originalBbox = part.BoundingBox;

        var plate = new Plate(60, 120);
        plate.Parts.Add(part);
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);
        part.RemoveLeadIns();

        Assert.Equal(originalBbox.X, part.BoundingBox.X, 4);
        Assert.Equal(originalBbox.Y, part.BoundingBox.Y, 4);
        Assert.Equal(originalBbox.Width, part.BoundingBox.Width, 4);
        Assert.Equal(originalBbox.Length, part.BoundingBox.Length, 4);
    }

    [Fact]
    public void Assign_ProgramGeometryMatchesRotatedOriginal()
    {
        var drawing = MakeSquareDrawing();
        var rotation = System.Math.PI / 2;
        var part = Part.CreateAtOrigin(drawing, rotation);
        part.Offset(10, 10);

        // Get the original rotated geometry
        var originalGeometry = OpenNest.Converters.ConvertProgram.ToGeometry(part.Program);
        var originalNonRapid = originalGeometry.Where(e => e.Layer != SpecialLayers.Rapid).ToList();

        var plate = new Plate(60, 120);
        plate.Parts.Add(part);
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);

        // The lead-in program should produce geometry that contains the
        // original rotated shape (plus lead-in/out extensions)
        var leadInGeometry = OpenNest.Converters.ConvertProgram.ToGeometry(part.Program);
        var leadInNonRapid = leadInGeometry.Where(e =>
            e.Layer != SpecialLayers.Rapid &&
            e.Layer != SpecialLayers.Leadin &&
            e.Layer != SpecialLayers.Leadout).ToList();

        // The bounding box of the cut geometry should be close to original
        var origBbox = GetEntityBounds(originalNonRapid);
        var leadBbox = GetEntityBounds(leadInNonRapid);

        Assert.Equal(origBbox.Width, leadBbox.Width, 2);
        Assert.Equal(origBbox.Length, leadBbox.Length, 2);
    }

    [Fact]
    public void Assign_MultipleRotatedParts_AllPreserveRotation()
    {
        var drawing = MakeSquareDrawing();
        var rotations = new[] { 0.0, System.Math.PI / 6, System.Math.PI / 4, System.Math.PI / 2 };

        var plate = new Plate(60, 120);
        for (var i = 0; i < rotations.Length; i++)
        {
            var part = Part.CreateAtOrigin(drawing, rotations[i]);
            part.Offset(10 + i * 15, 10);
            plate.Parts.Add(part);
        }

        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);

        for (var i = 0; i < rotations.Length; i++)
            Assert.Equal(rotations[i], plate.Parts[i].Rotation, 6);
    }

    [Fact]
    public void Assign_Twice_PreservesRotation()
    {
        var drawing = MakeSquareDrawing();
        var rotation = System.Math.PI / 3; // 60 degrees
        var part = Part.CreateAtOrigin(drawing, rotation);
        part.Offset(10, 10);

        var plate = new Plate(60, 120);
        plate.Parts.Add(part);
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);
        Assert.Equal(rotation, part.Rotation, 6);

        // Assign again (this removes first, then re-applies)
        assigner.Assign(plate);
        Assert.Equal(rotation, part.Rotation, 6);
    }

    [Fact]
    public void Assign_AfterExternalHasManualLeadIns_PreservesRotation()
    {
        // Simulates loading a nest where HasManualLeadIns was saved as true
        // but the program doesn't actually contain lead-in codes.
        var drawing = MakeSquareDrawing();
        var rotation = System.Math.PI / 2;
        var part = Part.CreateAtOrigin(drawing, rotation);
        part.Offset(10, 10);
        part.HasManualLeadIns = true; // externally set (e.g., by NestReader)

        var plate = new Plate(60, 120);
        plate.Parts.Add(part);
        plate.CuttingParameters = new CuttingParameters
        {
            ExternalLeadIn = new LineLeadIn { Length = 0.5, ApproachAngle = 90 }
        };

        var assigner = new LeadInAssigner { Sequencer = new LeftSideSequencer() };
        assigner.Assign(plate);

        Assert.Equal(rotation, part.Rotation, 6);
    }

    [Fact]
    public void RemoveLeadIns_AfterExternalHasManualLeadIns_PreservesRotation()
    {
        // Simulates the case where HasManualLeadIns is set externally
        // and then lead-ins are removed.
        var drawing = MakeSquareDrawing();
        var rotation = System.Math.PI / 3;
        var part = Part.CreateAtOrigin(drawing, rotation);
        part.Offset(10, 10);
        part.HasManualLeadIns = true;

        part.RemoveLeadIns();

        Assert.Equal(rotation, part.Rotation, 6);
    }

    private static Drawing MakeSquareDrawing()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 10)));
        pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("test", pgm);
    }

    private static Box GetEntityBounds(List<OpenNest.Geometry.Entity> entities)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var entity in entities)
        {
            if (entity is OpenNest.Geometry.Line line)
            {
                UpdateBounds(line.StartPoint, ref minX, ref minY, ref maxX, ref maxY);
                UpdateBounds(line.EndPoint, ref minX, ref minY, ref maxX, ref maxY);
            }
        }

        return new Box(minX, minY, maxX - minX, maxY - minY);
    }

    private static void UpdateBounds(Vector pt, ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        if (pt.X < minX) minX = pt.X;
        if (pt.Y < minY) minY = pt.Y;
        if (pt.X > maxX) maxX = pt.X;
        if (pt.Y > maxY) maxY = pt.Y;
    }
}
