using OpenNest.CNC;
using OpenNest.CommonLines;
using OpenNest.CutOffs;
using OpenNest.Desktop.Interaction;
using OpenNest.Desktop.Rendering;
using OpenNest.Geometry;
using OpenNest.Undo;

namespace OpenNest.Desktop.Tests.Interaction;

/// <summary>A plate view without a toolkit: the tools' host in tests.</summary>
internal sealed class TestToolHost : IPlateToolHost
{
    public TestToolHost(Plate plate)
    {
        Plate = plate;
        Selection.Plate = plate;
        // About ten pixels per unit, so a cut-off is picked within half a unit.
        View.Resize(1100, 1100);
        View.ZoomToArea(new Box(0, 0, 100, 100));
        Tools = new PlateToolManager(this);
    }

    public Plate Plate { get; set; }

    public PlateSelection Selection { get; } = new();

    public PlateHitTester HitTester { get; } = new();

    public ViewTransform View { get; } = new();

    public PlateToolManager Tools { get; }

    public int Invalidations { get; private set; }

    public void Invalidate() => Invalidations++;

    /// <summary>The history edits record into, or null to record nothing.</summary>
    public NestUndoRecorder Recorder { get; set; }

    /// <summary>The label of every change begun, in order.</summary>
    public List<string> ChangeLabels { get; } = new();

    private CutOffSettings cutOffSettings = new();

    public CutOffSettings CutOffSettings
    {
        get => ThrowOnCutOffSettings
            ? throw new InvalidOperationException("cut-off settings unavailable")
            : cutOffSettings;
        set => cutOffSettings = value;
    }

    /// <summary>Makes reading <see cref="CutOffSettings"/> throw, as a failed rebuild.</summary>
    public bool ThrowOnCutOffSettings { get; set; }

    public bool EditsLocked { get; set; }

    public ChangeScope BeginChange(string label, object coalesceKey = null)
    {
        ChangeLabels.Add(label);
        return Recorder?.BeginChange(label, coalesceKey) ?? ChangeScope.Detached();
    }

    /// <summary>Puts the plate in a nest with a fresh history that the edits record into.</summary>
    public NestUndoRecorder RecordHistory()
    {
        var nest = new Nest { Name = "test" };
        foreach (var drawing in Plate.Parts.Select(p => p.BaseDrawing).Distinct())
        {
            if (!drawing.IsCutOff)
                nest.Drawings.Add(drawing);
        }
        nest.Plates.Add(Plate);
        Recorder = new NestUndoRecorder(new Document { Nest = nest });
        Recorder.Reset();
        return Recorder;
    }

    public ToolPointer At(double x, double y, ToolButton button = ToolButton.Left,
        ToolModifiers modifiers = ToolModifiers.None) =>
        new(new Vector(x, y), View.WorldToScreen(x, y), button, modifiers);

    public void Click(double x, double y, ToolModifiers modifiers = ToolModifiers.None)
    {
        Tools.Current.PointerPressed(At(x, y, modifiers: modifiers));
        Tools.Current.PointerReleased(At(x, y, modifiers: modifiers));
    }

    public void Drag(double x1, double y1, double x2, double y2)
    {
        Tools.Current.PointerPressed(At(x1, y1));
        Tools.Current.PointerMoved(At((x1 + x2) / 2, (y1 + y2) / 2));
        Tools.Current.PointerMoved(At(x2, y2));
        Tools.Current.PointerReleased(At(x2, y2));
    }
}

internal static class InteractionFixtures
{
    public static Program Rect(double width, double height, double x = 0, double y = 0)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(x, y)));
        program.Codes.Add(new LinearMove(new Vector(x + width, y)));
        program.Codes.Add(new LinearMove(new Vector(x + width, y + height)));
        program.Codes.Add(new LinearMove(new Vector(x, y + height)));
        program.Codes.Add(new LinearMove(new Vector(x, y)));
        return program;
    }

    /// <summary>A 10x10 square with a 3-unit-radius round hole at its centre.</summary>
    public static Drawing Holed()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(8, 5)));
        program.Codes.Add(new ArcMove(new Vector(8, 5), new Vector(5, 5), RotationType.CW));
        foreach (var code in Rect(10, 10).Codes)
            program.Codes.Add(code);
        return new Drawing("holed", program);
    }

    /// <summary>
    /// A 100x100 plate: a holed square at (0,0), a square at (20,0), two squares one kerf apart in a
    /// common line at (40,0) and (50.05,0), a square at (20,20) on top of nothing, and a vertical
    /// cut-off at x = 80.
    /// </summary>
    public static Plate Plate()
    {
        var square = new Drawing("square", Rect(10, 10));
        var plate = new Plate(new Size(100, 100));
        plate.Parts.Add(new Part(Holed(), new Vector(0, 0)));
        plate.Parts.Add(new Part(square, new Vector(20, 0)));
        var a = new Part(square, new Vector(40, 0));
        var b = new Part(square, new Vector(50.05, 0));
        plate.Parts.Add(a);
        plate.Parts.Add(b);
        plate.Parts.Add(new Part(square, new Vector(20, 20)));
        plate.CommonLines.Add(new CommonLineGroup(new[] { a, b }, 0.05));
        plate.CutOffs.Add(new CutOff(new Vector(80, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());
        return plate;
    }
}
