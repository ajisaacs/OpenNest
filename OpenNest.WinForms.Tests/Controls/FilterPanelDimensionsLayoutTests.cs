using System.Reflection;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.WinForms.Tests.Controls;

/// <summary>
/// The CAD Converter's expanded Dimensions section is the bottom of the side panel: it fills
/// the space down to the panel's bottom edge so its list is not a short scrolling box.
/// </summary>
public class FilterPanelDimensionsLayoutTests
{
    [Fact]
    public void ExpandedDimensions_FillToTheBottom_AndKeepAMinimumWhenCramped() =>
        StaTestThread.Run(() =>
        {
            using var form = new Form { Width = 260, Height = 1100, StartPosition = FormStartPosition.Manual };
            var panel = new FilterPanel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();

            panel.LoadDimensions(["Horizontal 10", "Vertical 6", "Radius 1", "Diameter 2"], -1);
            Application.DoEvents();

            var dimensions = (CollapsiblePanel)typeof(FilterPanel)
                .GetField("dimensionsPanel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(panel)!;

            Assert.True(dimensions.IsExpanded);
            Assert.Equal(panel.ClientSize.Height, dimensions.Bottom);
            Assert.True(dimensions.Height > FilterPanel.DimensionsMinimumHeight);

            // A short window keeps the minimum and scrolls the whole panel instead.
            form.Height = 400;
            Application.DoEvents();
            Assert.Equal(FilterPanel.DimensionsMinimumHeight, dimensions.Height);

            // Collapsing still shrinks it to its header.
            dimensions.Toggle();
            Application.DoEvents();
            Assert.True(dimensions.Height < FilterPanel.DimensionsMinimumHeight);
            form.Close();
        }, TimeSpan.FromSeconds(60), "The STA test did not complete.");
}
