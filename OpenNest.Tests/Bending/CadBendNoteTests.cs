using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using OpenNest.Bending;
using OpenNest.Controls;
using OpenNest.IO.Bending;

namespace OpenNest.Tests.Bending;

public class CadBendNoteTests
{
    [Fact]
    public void DetectedNote_HidesOnlyItsSourceText_AndReturnsWhenBendRemoved()
    {
        var doc = new CadDocument();
        doc.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0))
        {
            Layer = new Layer("BEND"),
            LineType = new LineType("CENTER")
        });
        var note = new MText { Value = "UP 90° R0.125", InsertPoint = new XYZ(5, 0.1, 0), Height = 0.2 };
        doc.Entities.Add(note);
        var unrelated = new MText { Value = note.Value, InsertPoint = new XYZ(50, 50, 0), Height = 0.2 };
        doc.Entities.Add(unrelated);

        var bends = new SolidWorksBendDetector().DetectBends(doc);
        var bend = Assert.Single(bends);
        Assert.Equal(note.Handle, bend.SourceNoteHandle);
        var text = new CadText { SourceHandle = note.Handle, Value = note.Value };
        Assert.True(text.IsReplacedByBendNote(bends));
        Assert.False(new CadText { SourceHandle = unrelated.Handle, Value = note.Value }.IsReplacedByBendNote(bends));

        bends.Clear();
        Assert.False(text.IsReplacedByBendNote(bends));
    }

    [Fact]
    public void MissingSourceOrReplacement_DoesNotHideText()
    {
        var text = new CadText { SourceHandle = 42, Value = "UP 90° R0.125" };
        Assert.False(text.IsReplacedByBendNote(null));
        Assert.False(text.IsReplacedByBendNote(new[] { new Bend { NoteText = text.Value } }));
        Assert.False(text.IsReplacedByBendNote(new[] { new Bend { SourceNoteHandle = 42 } }));
        Assert.False(new CadText().IsReplacedByBendNote(new[] { new Bend { NoteText = text.Value } }));
    }
}
