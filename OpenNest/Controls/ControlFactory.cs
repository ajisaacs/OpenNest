using System.Windows.Forms;

namespace OpenNest.Controls
{
    /// <summary>Controls that the code-built dialogs and panels lay out the same way.</summary>
    internal static class ControlFactory
    {
        /// <summary>Width from a settings grid's left edge to its input column.</summary>
        internal const int SettingsLabelWidth = 150;

        /// <summary>An unpadded tab page whose only child is <paramref name="content"/>.</summary>
        internal static TabPage Tab(string text, Control content)
        {
            var page = new TabPage(text) { Padding = new Padding(0) };
            page.Controls.Add(content);
            return page;
        }

        /// <summary>
        /// A label / input / hint grid whose input column lines up with every other settings
        /// grid's, even when the grid is indented by <paramref name="indent"/>.
        /// </summary>
        internal static TableLayoutPanel SettingsGrid(int indent)
        {
            var grid = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                Margin = new Padding(indent, 0, 0, 0),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SettingsLabelWidth - indent));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return grid;
        }
    }
}
