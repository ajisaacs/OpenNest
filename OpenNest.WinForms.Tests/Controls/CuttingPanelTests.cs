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
                SupportTabs = true,
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
            Assert.True(built.SupportTabs);
        }, TimeSpan.FromSeconds(30), "The cutting panel did not build its parameters.");
    }

    [Fact]
    public void TabBySize_RoundTripsWithItsRange()
    {
        StaTestThread.Run(() =>
        {
            using var panel = new CuttingPanel();
            panel.LoadFromParameters(new CuttingParameters
            {
                SupportTabs = true,
                TabBySize = true,
                TabSizeMinWidth = 0.5,
                TabSizeMaxWidth = 3,
                TabConfig = new NormalTab { Size = 0.03 },
            });

            var built = panel.BuildParameters();

            Assert.True(built.SupportTabs);
            Assert.True(built.TabBySize);
            Assert.False(built.TabsEnabled);
            Assert.Equal(0.5, built.TabSizeMinWidth);
            Assert.Equal(3, built.TabSizeMaxWidth);
        }, TimeSpan.FromSeconds(30), "The cutting panel did not build tabs by size.");
    }

    [Fact]
    public void TabEveryPart_LoadsAsTabsBySizeOverEveryWidth()
    {
        StaTestThread.Run(() =>
        {
            using var panel = new CuttingPanel();
            var saved = new CuttingParameters
            {
                TabsEnabled = true,
                TabConfig = new NormalTab { Size = 0.03 },
                TabSizeMaxWidth = 2,
            };
            panel.LoadFromParameters(saved);

            var built = panel.BuildParameters();

            Assert.False(built.TabsEnabled);
            Assert.True(built.SupportTabs);
            Assert.True(built.TabBySize);
            Assert.Equal(0, built.TabSizeMinWidth);
            Assert.Equal(OpenNest.Support.SupportTabSettings.TabEveryPartMaxWidth, built.TabSizeMaxWidth);
            // The caller's settings are not changed.
            Assert.True(saved.TabsEnabled);
            Assert.False(saved.SupportTabs);
        }, TimeSpan.FromSeconds(30), "The cutting panel did not migrate Tab every part.");
    }
}
