using System;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Breakoffs;
using OpenNest.Controls;
using OpenNest.Labels;

namespace OpenNest.Forms
{
    // The part number label box: where the part number would be etched, found on the flat material
    // clear of edges, holes and bend lines, and adjustable by dragging. It stores placement only.
    public partial class CadConverterForm
    {
        private ContextMenuStrip labelBoxMenu;

        private void OnLabelBoxClick(object sender, EventArgs e)
        {
            var item = CurrentItem;
            if (item == null)
                return;

            if (item.LabelBox == null)
            {
                FindLabelBox(item);
                return;
            }

            labelBoxMenu ??= BuildLabelBoxMenu();
            labelBoxMenu.Show(btnLabelBox, new System.Drawing.Point(0, btnLabelBox.Height));
        }

        private ContextMenuStrip BuildLabelBoxMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Find Best Location", null, (_, _) =>
            {
                if (CurrentItem is { } item)
                    FindLabelBox(item);
            });
            menu.Items.Add("Remove", null, (_, _) =>
            {
                if (CurrentItem is not { } item)
                    return;
                item.LabelBox = null;
                RefreshLabelBox(item);
            });
            return menu;
        }

        /// <summary>
        /// Places the label box on the best flat area of the part as it will be cut. A part with no
        /// room keeps its current box (if any) and is told so.
        /// </summary>
        internal void FindLabelBox(FileListItem item)
        {
            var visible = VisibleEntities(item);
            var allEdges = item.Breakoffs.SelectMany(b => b.EdgeIds).ToList();
            var scrap = item.Breakoffs
                .Select(b => BreakoffBuilder.Layout(visible, b, allEdges))
                .Where(l => l.IsValid)
                .Select(l => (System.Collections.Generic.IEnumerable<Geometry.Entity>)l.Scrap)
                .ToList();

            var box = LabelBoxFinder.Find(DimensionedEntities(item), item.Bends, LabelBoxOptions.Default(DrawingUnits), scrap);
            if (box == null)
            {
                ShowLabelBoxMessage("No flat area of this part is big enough for the part number label, "
                    + "clear of its edges, holes and bend lines. Place the box by hand or leave it off.");
                return;
            }

            item.LabelBox = box;
            RefreshLabelBox(item);
        }

        private void RefreshLabelBox(FileListItem item)
        {
            entityView1.LabelBox = item?.LabelBox?.Clone();
            entityView1.Invalidate();
        }

        private void OnLabelBoxChanged(object sender, LabelBox box)
        {
            var item = CurrentItem;
            if (item == null)
                return;

            item.LabelBox = box;
            entityView1.LabelBox = box;
            entityView1.Invalidate();
        }

        internal virtual void ShowLabelBoxMessage(string message) =>
            MessageBox.Show(this, message, "Label Box", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
