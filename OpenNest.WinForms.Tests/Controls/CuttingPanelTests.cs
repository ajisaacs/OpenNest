using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;

namespace OpenNest.WinForms.Tests.Controls;

public class CuttingPanelTests
{
    [Fact]
    public void BuildParameters_KeepsTheSettingsThePanelHasNoFieldFor()
    {
        StaTestThread.Run(() =>
        {
            using var panel = new CuttingPanel();
            panel.LoadFromParameters(new CuttingParameters
            {
                MachineName = "CL-980",
                MaterialName = "Stainless Steel",
                Grade = "304",
                Thickness = 0.075,
                Kerf = 0.008,
                ExternalLeadIn = new ArcLeadIn { Radius = 0.0625 },
                ExternalLeadOut = new NoLeadOut(),
                TabsEnabled = true,
                TabConfig = new NormalTab { Size = 0.25 },
            });

            var built = panel.BuildParameters();

            // Without the stamp, the machine's leads would replace these edits on the next open.
            Assert.Equal("CL-980", built.MachineName);
            Assert.Equal("Stainless Steel", built.MaterialName);
            Assert.Equal("304", built.Grade);
            Assert.Equal(0.075, built.Thickness);
            Assert.Equal(0.008, built.Kerf);
            Assert.IsType<NoLeadOut>(built.ExternalLeadOut);
            Assert.True(built.TabsEnabled);
        }, TimeSpan.FromSeconds(30), "The cutting panel did not build its parameters.");
    }
}
