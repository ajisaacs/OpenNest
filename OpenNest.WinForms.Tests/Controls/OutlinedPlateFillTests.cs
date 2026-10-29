using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;
using OpenNest.Engine;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.WinForms.Tests.Forms;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// The interactive fills stop before the fill service on a plate with a sheet outline: nothing
/// throws, no part is added, no progress window opens, and the status bar says why.
/// </summary>
[Collection("Fill operation lifetime")]
public class OutlinedPlateFillTests
{
    private static PlateView OutlinedView()
    {
        var plate = new Plate(60, 120) { PartSpacing = 0.5 };
        plate.Outline = new SheetOutline(new[]
        {
            new Vector(0, 0), new Vector(120, 0), new Vector(120, 30), new Vector(60, 30), new Vector(60, 60), new Vector(0, 60),
        });
        return new PlateView { Plate = plate };
    }

    private static Drawing Square() => new("square", UndoTestEnv.Square(10));

    [Fact]
    public void PlateViewFillIsRefusedWithAStatusMessage() => RunSta(() =>
    {
        using var view = OutlinedView();
        var statuses = new List<string>();
        view.StatusChanged += (_, _) => statuses.Add(view.Status);

        view.FillWithProgress([new Part(Square())], view.Plate.WorkArea());

        Assert.False(view.IsFillInProgress);
        Assert.Empty(Application.OpenForms.OfType<NestProgressForm>());
        Assert.Empty(view.Plate.Parts);
        Assert.Equal(new[] { PlateFillService.OutlinedSheetMessage }, statuses);
        Assert.Contains("rectangular sheet", view.Status);
    });

    [Fact]
    public void FillAreaIsRefusedWithAStatusMessage() => RunSta(() =>
    {
        using var view = OutlinedView();
        view.SetAction(typeof(ActionFillArea), Square());
        var action = (ActionFillArea)view.Actions.CurrentAction;
        UndoTestEnv.SetBackingField(action, "SelectedArea", new Box(0, 0, 50, 50));

        UndoTestEnv.Invoke(action, "FillArea");

        Assert.Empty(view.Plate.Parts);
        Assert.Equal(PlateFillService.OutlinedSheetMessage, view.Status);
    });

    [Fact]
    public void FillAreaWithProgressCompletesEmptyWithoutStarting() => RunSta(() =>
    {
        using var view = OutlinedView();
        using var cts = new CancellationTokenSource();
        var reported = new List<NestProgress>();
        List<Part>? completed = null;
        var action = new ActionFillArea(view, Square(), new Progress<NestProgress>(reported.Add), cts,
            parts => completed = parts);
        UndoTestEnv.SetBackingField(action, "SelectedArea", new Box(0, 0, 50, 50));

        UndoTestEnv.Invoke(action, "FillArea");

        Assert.NotNull(completed);
        Assert.Empty(completed!);
        Assert.Empty(reported);
        Assert.Empty(view.Plate.Parts);
        Assert.Equal(PlateFillService.OutlinedSheetMessage, view.Status);
        action.DisconnectEvents();
    });

    [Fact]
    public void ARectangleStillFills() => RunSta(() =>
    {
        using var view = new PlateView { Plate = new Plate(60, 120) { PartSpacing = 0.5 } };
        view.SetAction(typeof(ActionFillArea), Square());
        var action = (ActionFillArea)view.Actions.CurrentAction;
        UndoTestEnv.SetBackingField(action, "SelectedArea", new Box(0, 0, 50, 50));

        UndoTestEnv.Invoke(action, "FillArea");

        Assert.NotEmpty(view.Plate.Parts);
        Assert.Equal("Fill Area", view.Status); // the action's name, as SetAction left it
    });

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(60), "Outlined plate fill test timed out.");
}
