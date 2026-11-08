using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    // Reordering plates in the Plates list: drag a row to a new position, or Alt+Up/Alt+Down.
    // Ctrl+drag drops a copy of the plate instead, as does Duplicate Plate on the row's menu;
    // Delete Plate on the same menu removes it.
    // The trailing empty new-plate sentinel stays last and cannot be dragged.
    public partial class EditNestForm
    {
        private Panel plateDropIndicator;
        private int plateDropSlot = -1;

        /// <summary>Drag payload marking a plate row dragged within this window's list.</summary>
        private sealed class PlateDragData
        {
            public PlateDragData(EditNestForm owner, int index)
            {
                Owner = owner;
                Index = index;
            }

            public EditNestForm Owner { get; }
            public int Index { get; }
        }

        private void InitializePlateReordering()
        {
            platesListView.AllowDrop = true;
            platesListView.ItemDrag += PlatesListView_ItemDrag;
            platesListView.DragEnter += PlatesListView_DragOver;
            platesListView.DragOver += PlatesListView_DragOver;
            platesListView.DragDrop += PlatesListView_DragDrop;
            platesListView.DragLeave += (sender, e) => HidePlateDropIndicator();
            platesListView.KeyDown += PlatesListView_KeyDown;

            plateDropIndicator = new Panel
            {
                Height = 2,
                BackColor = SystemColors.Highlight,
                Visible = false,
                TabStop = false,
                Enabled = false,
            };
            platesListView.Controls.Add(plateDropIndicator);

            var duplicateItem = new ToolStripMenuItem("Duplicate Plate", null, (sender, e) => DuplicateCurrentPlate());
            var deleteItem = new ToolStripMenuItem("Delete Plate", null, (sender, e) => RemoveCurrentPlate());
            deleteItem.Name = "deletePlateMenuItem";
            var menu = new ContextMenuStrip();
            menu.Items.Add(duplicateItem);
            menu.Items.Add(deleteItem);
            menu.Opening += (sender, e) =>
            {
                var index = PlateManager.CurrentIndex;
                duplicateItem.Enabled = !IsInspecting && PlateManager.CanDuplicatePlate(index, index + 1);
                deleteItem.Enabled = !IsInspecting && PlateManager.CanRemoveCurrent;
            };
            platesListView.ContextMenuStrip = menu;
        }

        /// <summary>
        /// Inserts a copy of the plate at <paramref name="fromIndex"/> so it lands at
        /// <paramref name="toIndex"/>, as one undoable step; the copy becomes the current plate.
        /// </summary>
        /// <returns>Whether a plate was added.</returns>
        public bool DuplicatePlate(int fromIndex, int toIndex)
        {
            if (IsInspecting || !PlateManager.CanDuplicatePlate(fromIndex, toIndex))
                return false;

            using (var change = BeginChange("Duplicate Plate"))
            {
                change.MarkChanged();
                var copy = PlateManager.DuplicatePlate(fromIndex, toIndex);
                // The copy gets its own parameters, so editing one sheet's leaves the other's alone.
                if (copy.CuttingParameters != null)
                    copy.CuttingParameters = IO.CuttingParametersSerializer.FromDto(
                        IO.CuttingParametersSerializer.ToDto(copy.CuttingParameters));
                change.Complete();
            }

            FirePlateChanged();
            return true;
        }

        /// <summary>Inserts a copy of the current plate right after it.</summary>
        public bool DuplicateCurrentPlate()
        {
            var index = PlateManager.CurrentIndex;
            return DuplicatePlate(index, index + 1);
        }

        /// <summary>
        /// Moves the plate at <paramref name="fromIndex"/> to <paramref name="toIndex"/> as one
        /// undoable step, keeping it the current plate.
        /// </summary>
        /// <returns>Whether the order changed.</returns>
        public bool MovePlate(int fromIndex, int toIndex)
        {
            if (IsInspecting || !PlateManager.CanMovePlate(fromIndex, toIndex))
                return false;

            using (var change = BeginChange("Move Plate"))
            {
                change.MarkChanged();
                PlateManager.MovePlate(fromIndex, toIndex);
                change.Complete();
            }

            // Navigation and the status bar read the current plate's index.
            FirePlateChanged();
            return true;
        }

        private void PlatesListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Modifiers != Keys.Alt || (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down))
                return;

            var from = PlateManager.CurrentIndex;
            var to = e.KeyCode == Keys.Up ? from - 1 : from + 1;
            MovePlate(from, to);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void PlatesListView_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (IsInspecting || e.Button != MouseButtons.Left || e.Item is not ListViewItem item)
                return;
            if (item.Index >= PlateManager.MovableCount)
                return;

            var effects = DragDropEffects.None;
            if (PlateManager.MovableCount >= 2)
                effects |= DragDropEffects.Move;
            if (PlateManager.CanDuplicatePlate(item.Index, item.Index + 1))
                effects |= DragDropEffects.Copy;
            if (effects == DragDropEffects.None)
                return;

            try
            {
                platesListView.DoDragDrop(new PlateDragData(this, item.Index), effects);
            }
            finally
            {
                HidePlateDropIndicator();
            }
        }

        private PlateDragData GetPlateDragData(DragEventArgs e) =>
            e.Data?.GetData(typeof(PlateDragData)) is PlateDragData data && ReferenceEquals(data.Owner, this)
                ? data
                : null;

        private void PlatesListView_DragOver(object sender, DragEventArgs e)
        {
            var data = GetPlateDragData(e);
            if (data == null || IsInspecting)
            {
                e.Effect = DragDropEffects.None;
                HidePlateDropIndicator();
                return;
            }

            var client = platesListView.PointToClient(new Point(e.X, e.Y));
            AutoScrollPlateList(client);

            var slot = GetPlateDropSlot(client);
            var copying = IsCopyDrag(e);
            var allowed = copying
                ? PlateManager.CanDuplicatePlate(data.Index, slot)
                : (e.AllowedEffect & DragDropEffects.Move) != 0
                    && PlateManager.CanMovePlate(data.Index, PlateDropTarget(data.Index, slot));
            if (!allowed)
            {
                e.Effect = DragDropEffects.None;
                HidePlateDropIndicator();
                return;
            }

            e.Effect = copying ? DragDropEffects.Copy : DragDropEffects.Move;
            ShowPlateDropIndicator(slot);
        }

        // Ctrl (key state bit 8) during the drag drops a copy where the plate allows one.
        private static bool IsCopyDrag(DragEventArgs e) =>
            (e.KeyState & 8) != 0 && (e.AllowedEffect & DragDropEffects.Copy) != 0;

        private void PlatesListView_DragDrop(object sender, DragEventArgs e)
        {
            HidePlateDropIndicator();
            var data = GetPlateDragData(e);
            if (data == null)
                return;

            var client = platesListView.PointToClient(new Point(e.X, e.Y));
            var slot = GetPlateDropSlot(client);
            if (IsCopyDrag(e))
                DuplicatePlate(data.Index, slot);
            else
                MovePlate(data.Index, PlateDropTarget(data.Index, slot));
        }

        /// <summary>
        /// The gap the pointer is over, 0 (before the first plate) through the movable count
        /// (after the last movable plate): the sentinel row and below drop after the last plate.
        /// </summary>
        internal int GetPlateDropSlot(Point client)
        {
            var movable = PlateManager.MovableCount;
            var item = platesListView.GetItemAt(platesListView.ClientSize.Width / 2, client.Y);
            if (item == null)
            {
                var top = platesListView.TopItem;
                return top != null && client.Y < top.Bounds.Top ? top.Index : movable;
            }

            var bounds = item.Bounds;
            var slot = client.Y < bounds.Top + bounds.Height / 2 ? item.Index : item.Index + 1;
            return System.Math.Min(slot, movable);
        }

        /// <summary>The index a plate dragged from <paramref name="from"/> lands at when dropped in <paramref name="slot"/>.</summary>
        internal static int PlateDropTarget(int from, int slot) => slot > from ? slot - 1 : slot;

        private void ShowPlateDropIndicator(int slot)
        {
            if (slot == plateDropSlot && plateDropIndicator.Visible)
                return;

            plateDropSlot = slot;
            var items = platesListView.Items;
            if (items.Count == 0)
                return;

            var y = slot < items.Count ? items[slot].Bounds.Top : items[slot - 1].Bounds.Bottom;
            plateDropIndicator.SetBounds(0, System.Math.Max(0, y - 1), platesListView.ClientSize.Width, 2);
            plateDropIndicator.Visible = true;
            plateDropIndicator.BringToFront();
        }

        private void HidePlateDropIndicator()
        {
            plateDropSlot = -1;
            if (plateDropIndicator != null)
                plateDropIndicator.Visible = false;
        }

        // Scrolls one row when the pointer nears the top or bottom edge during a drag.
        private void AutoScrollPlateList(Point client)
        {
            var items = platesListView.Items;
            var top = platesListView.TopItem;
            if (items.Count == 0 || top == null)
                return;

            var margin = top.Bounds.Height;
            if (client.Y < margin && top.Index > 0)
            {
                HidePlateDropIndicator();
                items[top.Index - 1].EnsureVisible();
            }
            else if (client.Y > platesListView.ClientSize.Height - margin)
            {
                var below = platesListView.GetItemAt(1, platesListView.ClientSize.Height - 1);
                var next = below == null ? -1 : below.Index + 1;
                if (next > 0 && next < items.Count)
                {
                    HidePlateDropIndicator();
                    items[next].EnsureVisible();
                }
            }
        }
    }
}
