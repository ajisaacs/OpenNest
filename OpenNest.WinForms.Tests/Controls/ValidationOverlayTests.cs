using System.Drawing;
using OpenNest.Controls;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Controls;

public class ValidationOverlayTests
{
    [Fact]
    public void RapidContactCrosshairIsDrawnAtContactAndClearedWithFinding() => RunSta(() =>
    {
        using var view = new PlateView();
        view.ShowValidationFinding(new PostVerificationFinding(PostVerificationKind.RapidCrossing, 1, 2, 1, "crossing")
        {
            RapidStart = new Vector(-1, 0),
            RapidEnd = new Vector(1, 0),
            ContactPoints = Array.AsReadOnly(new[] { Vector.Zero })
        });
        using var bitmap = new Bitmap(120, 120);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TranslateTransform(60, 60);
        graphics.Clear(Color.Black);
        view.ValidationOverlay.Draw(graphics);
        var pixel = bitmap.GetPixel(60, 65);
        Assert.True(pixel.R > 150 && pixel.B > 50 && pixel.G < 100);
        view.ClearValidationFinding();
        graphics.Clear(Color.Black);
        view.ValidationOverlay.Draw(graphics);
        Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(60, 65).ToArgb());
    });

    [Fact]
    public void PlateSwitchClearsAnimation() => RunSta(() =>
    {
        using var view = new PlateView();
        view.ShowValidationFinding(new PostVerificationFinding(PostVerificationKind.MissingLeadIn, 1, 1, null, "missing")
        { Location = new Vector(3, 4) });
        Assert.True(view.ValidationOverlay.IsAnimating);
        view.Plate = new Plate();
        Assert.False(view.ValidationOverlay.IsAnimating);
        Assert.Null(view.ValidationOverlay.Finding);
    });

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(30), "Validation overlay test timed out.");
}
