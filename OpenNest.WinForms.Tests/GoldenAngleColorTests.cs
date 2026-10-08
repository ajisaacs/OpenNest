using System.Drawing;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests;

[Collection("Fill operation lifetime")]
public class GoldenAngleColorTests
{
    [Fact]
    public void WorkshopRecolorsEachDrawingBeyondAShortPaletteAndKeepsCopiesTogether()
        => StaTestThread.Run(() =>
        {
            var previous = ColorSchemeSerializer.Deserialize(ColorSchemeSerializer.Serialize(ColorScheme.Default));
            try
            {
                var nest = new Nest("Generated drawing colors");
                var cutoff = new Drawing("Cutoff") { IsCutOff = true, Color = Color.Gray };
                for (var i = 0; i < 24; i++)
                {
                    var program = new CNC.Program();
                    program.MoveTo(0, 0);
                    program.LineTo(100, 0);
                    program.LineTo(100, 100);
                    program.LineTo(0, 100);
                    program.LineTo(0, 0);
                    nest.Drawings.Add(new Drawing($"Drawing {i}", program) { Color = Color.Coral });
                    if (i == 0)
                        nest.Drawings.Add(cutoff);
                }
                var plate = nest.CreatePlate();
                plate.Parts.Add(new Part(nest.Drawings.First()));
                plate.Parts.Add(new Part(nest.Drawings.First()));
                using var editor = new EditNestForm(nest);
                editor.PlateView.SetOverlapAutoCheck(null);
                editor.Show();
                ColorSchemeRegistry.Apply(ColorSchemeRegistry.Get("Workshop"));

                var drawings = nest.Drawings.Where(d => !d.IsCutOff).ToArray();
                Assert.Equal(24, drawings.Select(d => d.Color.ToArgb()).Distinct().Count());
                Assert.Equal(Color.FromArgb(216, 49, 49), drawings[0].Color);
                Assert.Equal(Color.FromArgb(98, 160, 234), drawings[1].Color);
                Assert.Equal(Color.Gray, cutoff.Color);
                Assert.Same(plate.Parts[0].BaseDrawing, plate.Parts[1].BaseDrawing);
                var colors = drawings.Select(d => d.Color).ToArray();
                ColorSchemeRegistry.Apply(ColorSchemeRegistry.Get("Workshop"));
                Assert.Equal(colors, drawings.Select(d => d.Color));
            }
            finally
            {
                ColorSchemeRegistry.Apply(previous);
            }
        }, TimeSpan.FromMinutes(1), "Generated palette recolor timed out.");

    [Fact]
    public void NewlyImportedColorsUseGeneratedHuesAndSwitchBackToLegacyPalettes()
        => StaTestThread.Run(() =>
        {
            var previous = ColorSchemeSerializer.Deserialize(ColorSchemeSerializer.Serialize(ColorScheme.Default));
            try
            {
                ColorSchemeRegistry.Apply(ColorSchemeRegistry.Get("Workshop"));
                var colors = Enumerable.Range(0, 100).Select(_ => Drawing.GetNextColor()).ToArray();
                Assert.True(colors.Select(c => c.ToArgb()).Distinct().Count() > 90);
                Assert.All(colors, color =>
                {
                    Assert.InRange(color.GetSaturation(), 0.44f, 0.99f);
                    Assert.InRange(color.GetBrightness(), 0.4f, 0.82f);
                    var hue = color.GetHue();
                    Assert.True(hue < 95 || hue >= 165);
                });
                var classic = ColorSchemeRegistry.Get("Classic");
                ColorSchemeRegistry.Apply(classic);
                Assert.Contains(Drawing.GetNextColor(), classic.PartColors);
            }
            finally
            {
                ColorSchemeRegistry.Apply(previous);
            }
        }, TimeSpan.FromMinutes(1), "Generated import colors timed out.");
}
