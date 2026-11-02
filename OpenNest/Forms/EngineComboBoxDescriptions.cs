using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    /// <summary>
    /// Shows each engine's description beside its name in the open list of an engine combo box.
    /// Items stay plain engine names, so selection code is unchanged; the closed box shows the
    /// name alone.
    /// </summary>
    internal static class EngineComboBoxDescriptions
    {
        private const int DropDownWidth = 620;

        internal static void Apply(ComboBox comboBox)
        {
            comboBox.DrawMode = DrawMode.OwnerDrawFixed;
            comboBox.DropDownWidth = DropDownWidth;
            comboBox.DrawItem += DrawItem;
        }

        private static void DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();

            if (e.Index >= 0 && sender is ComboBox comboBox)
            {
                var name = comboBox.Items[e.Index] as string ?? string.Empty;
                var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                TextRenderer.DrawText(e.Graphics, name, e.Font, e.Bounds, e.ForeColor, flags);

                var inList = (e.State & DrawItemState.ComboBoxEdit) == 0;
                if (inList)
                {
                    const int nameColumn = 140;
                    var bounds = Rectangle.FromLTRB(e.Bounds.Left + nameColumn, e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom);
                    var selected = (e.State & DrawItemState.Selected) != 0;
                    TextRenderer.DrawText(e.Graphics, EngineSelection.Description(name), e.Font, bounds,
                        selected ? e.ForeColor : SystemColors.GrayText, flags);
                }
            }

            e.DrawFocusRectangle();
        }
    }
}
