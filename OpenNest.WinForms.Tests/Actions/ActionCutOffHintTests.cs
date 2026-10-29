using System.Reflection;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;
using OpenNest.Help;

namespace OpenNest.WinForms.Tests.Actions;

/// <summary>The manual cut-off tool says how to turn its line and where its help is.</summary>
public class ActionCutOffHintTests
{
    private static void PressKey(PlateView view, Keys key) =>
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { new KeyEventArgs(key) });

    [Fact]
    public void StatusNamesTheAxisAndSpaceTurnsIt() => StaTestThread.Run(() =>
    {
        using var view = new PlateView { Plate = new Plate(48, 120) };

        view.SetAction(typeof(ActionCutOff));
        Assert.Equal(
            "Sheet Cut-Off: click to add a vertical line, Space turns it horizontal, Esc finishes, F1 for help",
            view.Status);
        Assert.Equal(HelpTopics.SheetCutOff, view.ActionHelpTopic);

        PressKey(view, Keys.Space);
        Assert.StartsWith("Sheet Cut-Off: click to add a horizontal line, Space turns it vertical", view.Status);

        view.SetAction(typeof(ActionSelect));
        Assert.Null(view.ActionHelpTopic);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}
