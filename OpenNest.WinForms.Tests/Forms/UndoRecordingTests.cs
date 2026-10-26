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
