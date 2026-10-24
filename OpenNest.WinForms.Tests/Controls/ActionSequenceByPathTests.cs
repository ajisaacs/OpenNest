using OpenNest.Actions;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// Sequence by Path mutes every part until a line reaches it, however the tool is started.
/// </summary>
public class ActionSequenceByPathTests
{
    private static PlateView View()
    {
        var drawing = new Drawing("square", Square());
        var plate = new Plate(48, 48);
        plate.Parts.Add(new Part(drawing, new Vector(2, 2)));
        plate.Parts.Add(new Part(drawing, new Vector(20, 2)));
        var view = new PlateView { Plate = plate, Size = new System.Drawing.Size(600, 600) };
        view.ZoomToFit();
        return view;
    }

    private static Program Square()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(4, 0));
        program.Codes.Add(new LinearMove(4, 4));
        program.Codes.Add(new LinearMove(0, 4));
        program.Codes.Add(new LinearMove(0, 0));
        return program;
    }

    [Fact]
    public void StartingTheToolMutesEveryPart() => StaTestThread.Run(() =>
    {
        using var view = View();

        view.SetAction(typeof(ActionSequenceByPath));

        Assert.All(view.Plate.Parts, part => Assert.Contains(part, view.MutedParts!));
        view.SetAction(typeof(ActionSelect));
        Assert.Null(view.MutedParts);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");

    [Fact]
    public void RestartingTheToolKeepsEveryPartMuted() => StaTestThread.Run(() =>
    {
        using var view = View();

        view.SetAction(typeof(ActionSequenceByPath));
        view.SetAction(typeof(ActionSequenceByPath));

        Assert.NotNull(view.MutedParts);
        Assert.All(view.Plate.Parts, part => Assert.Contains(part, view.MutedParts!));
        view.SetAction(typeof(ActionSelect));
        Assert.Null(view.MutedParts);
    }, TimeSpan.FromMinutes(1), "The STA test did not complete.");
}
