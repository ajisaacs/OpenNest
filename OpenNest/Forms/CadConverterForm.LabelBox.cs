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
            menu.Items.Add("Rotate 90°", null, (_, _) => TurnLabelBox(box => box.Rotation + Math.Angle.HalfPI));
            menu.Items.Add("Make Horizontal", null, (_, _) => TurnLabelBox(_ => 0));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Remove", null, (_, _) =>
            {
                if (CurrentItem is not { } item)
                    return;
                item.LabelBox = null;
                RefreshLabelBox(item);
            });
            return menu;
        }

        // A label that does not fit is tried at these shares of its size, to find the roomiest
        // flat spot to start a hand placement from.
        private static readonly double[] FallbackLabelScales = { 0.75, 0.5, 0.25 };

        /// <summary>
        /// Places the label box on the best flat area of the part as it will be cut, at the largest
        /// text size that fits (<see cref="LabelBoxOptions.Sizes"/>). When even the smallest label
        /// does not fit, a box is still placed for the user to adjust by hand: the smallest-size box
        /// centered on the roomiest flat spot a smaller label fits, else on the middle of the part,
        /// and the user is told so.
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

            // The largest label size that fits; only when even the smallest does not is a box
            // placed for hand adjustment, at that smallest size.
            var entities = DimensionedEntities(item);
            var sizes = LabelBoxOptions.Sizes(DrawingUnits);
            var box = sizes.Select(size => LabelBoxFinder.Find(entities, item.Bends, size, scrap))
                .FirstOrDefault(found => found != null);
            var options = sizes[^1];
            if (box != null)
            {
                item.LabelBox = box;
                RefreshLabelBox(item);
                return;
            }

            var spot = FallbackLabelScales
                .Select(scale => LabelBoxFinder.Find(entities, item.Bends, new LabelBoxOptions
                {
                    Width = options.Width * scale,
                    Height = options.Height * scale,
                    Clearance = options.Clearance,
                    BendClearance = options.BendClearance,
                }, scrap))
                .FirstOrDefault(b => b != null);
            var center = spot?.Center ?? PartCenter(entities);
            if (center == null)
            {
                ShowLabelBoxMessage("This part has no closed outline to put the part number label on.");
                return;
            }

            item.LabelBox = new LabelBox(center.Value, options.Width, options.Height, spot?.Rotation ?? 0);
            RefreshLabelBox(item);
            ShowLabelBoxMessage("No flat area of this part is big enough for the part number label, "
                + "clear of its edges, holes and bend lines. A box has been placed "
                + (spot != null ? "on the roomiest flat spot" : "in the middle of the part")
                + " for you to adjust: drag its body to move it, a corner to resize it and the round "
                + "handle to turn it. Label Box... > Remove leaves it off.");
        }

        private static Geometry.Vector? PartCenter(System.Collections.Generic.List<Geometry.Entity> entities)
        {
            if (entities.Count == 0)
                return null;
            var bounds = Geometry.BoundingBox.GetBoundingBox(entities.Cast<Geometry.IBoundable>());
            return bounds.Width > 0 || bounds.Length > 0 ? bounds.Center : null;
        }

        private void TurnLabelBox(Func<LabelBox, double> rotation)
        {
            if (CurrentItem is not { LabelBox: { } box } item)
                return;
            item.LabelBox = box.WithRotation(rotation(box));
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

        private string bendLabelPreviewKey;
        private System.Collections.Generic.List<Stroke> bendLabelPreview;

        /// <summary>
        /// The bend group letters as Plan Cutting would etch them beside the bend lines
        /// (<see cref="BendLabels.Place"/>), for the part unrotated on a sheet whose grain runs
        /// along X, like the label's own preview; null for a part without bends. Kept until the box, the bends or the cut
        /// geometry's makeup change, since it is asked for at every paint.
        /// </summary>
        internal System.Collections.Generic.IReadOnlyList<Stroke> BendLabelPreview(FileListItem item)
        {
            if (item?.Bends == null || item.Bends.Count == 0)
                return null;

            // The label box is only kept clear of; a part without one still gets its bend letters.
            var box = item.LabelBox is { IsValid: true } valid ? valid : null;
            var key = new System.Text.StringBuilder();
            key.Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item)).Append('|').Append(DrawingUnits).Append('|')
                .Append(box?.Center.X).Append(',').Append(box?.Center.Y).Append(',').Append(box?.Width).Append(',')
                .Append(box?.Height).Append(',').Append(box?.Rotation).Append('|')
                .Append(item.Entities.Count(en => en.Layer.IsVisible && en.IsVisible)).Append(',')
                .Append(item.Breakoffs.Count).Append(',').Append(item.TabbedCutouts.Count).Append(',')
                .Append(item.EntityRoles?.Count ?? 0);
            foreach (var bend in item.Bends)
                key.Append('|').Append(bend.StartPoint.X).Append(',').Append(bend.StartPoint.Y).Append(',')
                    .Append(bend.EndPoint.X).Append(',').Append(bend.EndPoint.Y).Append(',')
                    .Append((int)bend.Direction).Append(',').Append(bend.Angle).Append(',').Append(bend.Radius);

            var text = key.ToString();
            if (text != bendLabelPreviewKey)
            {
                bendLabelPreviewKey = text;
                bendLabelPreview = BendLabels.Layout(
                    BendLabels.Place(DimensionedEntities(item), item.Bends, box, DrawingUnits), item.Bends, 0, 0);
            }
            return bendLabelPreview;
        }

        internal virtual void ShowLabelBoxMessage(string message) =>
            MessageBox.Show(this, message, "Label Box", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
