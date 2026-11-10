using System.Windows.Forms;
using OpenNest.Forms;
using OpenNest.Geometry;
using static OpenNest.WinForms.Tests.TestReflection;

namespace OpenNest.WinForms.Tests.Forms;

// Reads Application.OpenForms for the busy guard, so it must not overlap fill-lifetime tests
// that transiently open NestProgressForm.
[Collection("Fill operation lifetime")]
public class FixSpacingMenuTests
{
    [Fact]
    public void FixSpacingOpensTheTooClosePair() => StaTestThread.Run(() =>
    {
        using var host = new MenuHost();
        host.Show();
        var (nest, plate) = TooClosePair();
        using var editor = Open(host, nest);

        Menu(host, "mnuPlateFixSpacing").PerformClick();

        Assert.Equal(new Vector(1, 1), plate.Parts[0].Location);
        Assert.InRange(plate.Parts[1].Location.X, 11.25, 11.252);
        Assert.StartsWith("Fixed 1 of 1 flagged pair", editor.PlateView.Status);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void NothingFlaggedReportsItWithoutAnEdit() => StaTestThread.Run(() =>
    {
        using var host = new MenuHost();
        host.Show();
        var (nest, plate) = TooClosePair();
        plate.PartSpacing = 0.1;
        using var editor = Open(host, nest);
        var count = editor.History.UndoCount;

        Menu(host, "mnuPlateFixSpacing").PerformClick();

        Assert.Equal("No overlapping or too-close parts to fix", editor.PlateView.Status);
        Assert.Equal(count, editor.History.UndoCount);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void RunningFillOnTheNestRefusesFixWithoutMovingParts() => StaTestThread.Run(() =>
    {
        using var host = new MenuHost();
        host.Show();
        var (nest, plate) = TooClosePair();
        using var editor = Open(host, nest);
        var before = plate.Parts.Select(p => p.Location).ToArray();
        var notices = new List<string>();
        host.ExpandSpacingNotice = notices.Add;

        SetBackingField(editor.PlateView, "IsFillInProgress", true);
        try
        {
            Menu(host, "mnuPlateFixSpacing").PerformClick();

            Assert.Contains("before Fix Spacing", Assert.Single(notices));
            Assert.Equal(before, plate.Parts.Select(p => p.Location).ToArray());
        }
        finally
        {
            SetBackingField(editor.PlateView, "IsFillInProgress", false);
        }
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    /// <summary>Two squares 0.1 apart on a plate whose part spacing is 0.25.</summary>
    private static (Nest Nest, Plate Plate) TooClosePair()
    {
        var nest = new Nest("fix job");
        var plate = nest.CreatePlate();
        plate.Size = new Size(100, 100);
        plate.PartSpacing = 0.25;
        plate.Parts.Add(new Part(new Drawing("left", TestPrograms.ClockwiseSquare(10)), new Vector(1, 1)));
        plate.Parts.Add(new Part(new Drawing("right", TestPrograms.ClockwiseSquare(10)), new Vector(11.1, 1)));
        return (nest, plate);
    }

    private static EditNestForm Open(MainForm host, Nest nest)
    {
        var editor = new EditNestForm(nest) { MdiParent = host };
        editor.PlateView.SetOverlapAutoCheck(null); // Background overlap workers are irrelevant here.
        editor.Show();
        editor.Activate();
        editor.PlateManager.LoadAt(0);
        return editor;
    }

    private static ToolStripMenuItem Menu(MainForm host, string name) =>
        (ToolStripMenuItem)host.MainMenuStrip!.Items.Find(name, true).Single();

    private sealed class MenuHost : MainForm
    {
        // No startup migrations/automatic new document, and no settings save at close.
        protected override void OnLoad(EventArgs e) { }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { }
    }
}
