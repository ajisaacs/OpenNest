using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Engine;
using OpenNest.Engine.Strategies;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

// Each desktop edit records one labelled undo entry and Undo puts the nest back. Reads
// Application.OpenForms through the busy guards, so it must not overlap fill-lifetime tests.
[Collection("Fill operation lifetime")]
public class UndoRecordingTests
{
    // Placement

    [Fact]
    public void AFillRecordsOneEntryAtItsCommit() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(2);
        var square = env.Nest.Drawings.Single();
        using var fill = UseFill(context => [new Part(context.Item.Drawing, context.WorkArea.Location)]);

        env.AssertOneUndoableEdit("Fill", () =>
        {
            env.View.FillWithProgress([new Part(square)], env.View.Plate.WorkArea());
            WaitForFill(env);
        });
    });

    [Fact]
    public void AFillThatPlacesNothingRecordsNothing() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(2);
        using var fill = UseFill(_ => []);

        env.View.FillWithProgress([new Part(env.Nest.Drawings.Single())], env.View.Plate.WorkArea());
        WaitForFill(env);

        Assert.Equal(0, env.Editor.History.UndoCount);
    });

    [Fact]
    public void FillAreaRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(2);
        using var fill = UseFill(context => [new Part(context.Item.Drawing, context.WorkArea.Location)]);
        env.View.SetAction(typeof(ActionFillArea), env.Nest.Drawings.Single());
        var action = (ActionFillArea)env.View.Actions.CurrentAction;
        UndoTestEnv.SetBackingField(action, "SelectedArea", new Box(0, 0, 50, 50));

        env.AssertOneUndoableEdit("Fill Area", () => UndoTestEnv.Invoke(action, "FillArea"));
    });

    [Theory]
    [InlineData(PatternTileTarget.CurrentPlate)]
    [InlineData(PatternTileTarget.NewPlate)]
    public void PatternTileRecordsOneEntry(PatternTileTarget target) => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        var square = env.Nest.Drawings.Single();
        var result = new PatternTileResult
        {
            Target = target,
            PlateSize = new Geometry.Size(60, 60),
            Parts = [new Part(square, new Vector(1, 1)), new Part(square, new Vector(12, 1)), new Part(square, new Vector(23, 1))],
        };

        env.AssertOneUndoableEdit("Pattern Tile", () => MainForm.ApplyPatternTile(env.Editor, result));
    });

    [Fact]
    public void ClearNestRecordsOneEntry() => UndoTestEnv.Run(env =>
        env.AssertOneUndoableEdit("Clear Nest", env.Editor.ClearNest));

    [Fact]
    public void RemoveEmptyPlatesRecordsOneEntry() => UndoTestEnv.Run(env =>
        env.AssertOneUndoableEdit("Remove Empty Plates", env.Editor.RemoveEmptyPlates));

    [Fact]
    public void CenterPartsRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.AssertOneUndoableEdit("Center Parts", () => env.Click("CenterParts_Click"));
    });

    // Plate

    [Theory]
    [InlineData(nameof(EditNestForm.RotateCw))]
    [InlineData(nameof(EditNestForm.RotateCcw))]
    [InlineData(nameof(EditNestForm.Rotate180))]
    public void RotatingAPlateRecordsOneEntry(string command) => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.AssertOneUndoableEdit("Rotate Plate", () => UndoTestEnv.Invoke(env.Editor, command));
    }, () =>
    {
        var nest = UndoTestEnv.BuildNest();
        nest.Plates[0].Size = new Geometry.Size(60, 100);
        return nest;
    });

    [Fact]
    public void ResizingAPlateToItsPartsRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.AssertOneUndoableEdit("Resize Plate", env.Editor.ResizePlateToFitParts);
    });

    [Fact]
    public void EditPlateRecordsOneEntryOnOk() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        UndoTestEnv.AcceptWhenShown<EditPlateForm>(form => form.Quantity = 3);
        env.AssertOneUndoableEdit("Edit Plate", env.Editor.EditPlate);
        Assert.Equal(3, env.Nest.Plates[0].Quantity);
    });

    [Fact]
    public void CancellingEditPlateRecordsNothing() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(2);
        UndoTestEnv.WhenShown<EditPlateForm>(form => form.DialogResult = DialogResult.Cancel);
        var before = UndoTestEnv.Describe(env.Nest);

        env.Editor.EditPlate();

        Assert.Equal(0, env.Editor.History.UndoCount);
        Assert.Equal(before, UndoTestEnv.Describe(env.Nest));
    });

    [Fact]
    public void SettingTheNestDefaultPlateRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.AssertOneUndoableEdit("Set Nest Default Plate", env.Editor.SetCurrentPlateAsNestDefault);
    });

    [Fact]
    public void TheClampsDialogRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        UndoTestEnv.AcceptWhenShown<ClampsForm>(form =>
            Assert.True(form.Editor.TryAddClamp(new RailClamp(ClampEdge.OriginSide, 20), out _)));
        env.AssertOneUndoableEdit("Clamps", () => env.Click("Clamps_Click"));
    });

    [Fact]
    public void TheSupportGridDialogRecordsOneEntry() => UndoTestEnv.Run(env =>
    {
        UndoTestEnv.AcceptWhenShown<SupportGridForm>(form =>
            AllControls(form).OfType<CheckBox>().First(box => box.Text.StartsWith("This nest's sheets")).Checked = true);
        env.AssertOneUndoableEdit("Support Grid", () => env.Click("SupportGrid_Click"));
    });

    [Fact]
    public void RemovingAndPuttingBackAClampRecordOneEntryEach() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        var plate = env.View.Plate;
        var id = plate.ClampZones[0].Id;

        env.AssertOneUndoableEdit("Remove Clamp", () => UndoTestEnv.Invoke(env.View.ClampMenu, "Apply", plate, id, true));
        env.AssertOneUndoableEdit("Put Clamp Back", () => UndoTestEnv.Invoke(env.View.ClampMenu, "Apply", env.View.Plate, id, false));
    }, NestWithClamps);

    [Fact]
    public void EditingClampsOnThePlateRecordsOneEntryPerEdit() => UndoTestEnv.Run(env =>
    {
        env.Editor.PlateManager.LoadAt(0);
        env.View.SetAction(typeof(ActionClampEdit));
        var action = env.View.Actions.CurrentAction;
        var first = new RailClamp(ClampEdge.OriginSide, 20);
        var added = new RailClamp(ClampEdge.OriginSide, 50);

        env.AssertOneUndoableEdit("Add Clamp", () => UndoTestEnv.Invoke(action, "Add", added));
        env.View.SetAction(typeof(ActionClampEdit));
        action = env.View.Actions.CurrentAction;
        env.AssertOneUndoableEdit("Move Clamp", () => UndoTestEnv.Invoke(action, "Move", first, new RailClamp(ClampEdge.OriginSide, 30)));
        env.View.SetAction(typeof(ActionClampEdit));
        action = env.View.Actions.CurrentAction;
        env.AssertOneUndoableEdit("Delete Clamp", () => UndoTestEnv.Invoke(action, "Delete", added));
    }, NestWithClamps);

    private static Nest NestWithClamps()
    {
        var nest = UndoTestEnv.BuildNest();
        nest.SetClampRail(new ClampRail(1.5, 0.625, new[] { 20.0, 60.0 }));
        return nest;
    }

    private static IEnumerable<Control> AllControls(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => AllControls(child).Prepend(child));

    private static IDisposable UseFill(Func<FillContext, List<Part>> fill)
    {
        var prior = EngineSelection.EngineName;
        EngineSelection.EngineName = EngineSelection.DefaultEngineName;
        var strategies = FillStrategyRegistry.UseForTesting([new TestFill(fill)]);
        return new Restore(() =>
        {
            strategies.Dispose();
            EngineSelection.EngineName = prior;
        });
    }

    private static void WaitForFill(UndoTestEnv env)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (env.View.IsFillInProgress)
        {
            Assert.True(DateTime.UtcNow < deadline, "The fill did not finish.");
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private sealed class TestFill(Func<FillContext, List<Part>> fill) : IFillStrategy
    {
        public string Name => "Undo recording test fill";
        public NestPhase Phase => NestPhase.Linear;
        public int Order => 0;
        public List<Part> Fill(FillContext context) => fill(context);
    }

    private sealed class Restore(System.Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
