using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    // Reordering plates in the Plates list: drag a row to a new position, or Alt+Up/Alt+Down.
    // Ctrl+drag drops a copy of the plate instead, as does Duplicate Plate on the row's menu;
    // Delete Plate on the same menu removes it; Sort Plates by Size on the toolbar reorders them all.
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
            platesListView.RowDragStarted += PlatesListView_RowDragStarted;
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

            InitializeSortPlatesButton();
        }

        // Sort Plates by Size reorders the nest once, as an undoable edit; the list has no
        // standing sort, since the plate order is the nest's own (cutting and report order).
        private void InitializeSortPlatesButton()
        {
            var largestItem = new ToolStripMenuItem("Largest First", null, (sender, e) => SortPlatesBySize(true));
            var smallestItem = new ToolStripMenuItem("Smallest First", null, (sender, e) => SortPlatesBySize(false));
            var button = new ToolStripDropDownButton
            {
                Name = "sortPlatesButton",
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = sortDrawingsImage ??= CreateSortDrawingsImage(),
                Padding = new Padding(5, 0, 5, 0),
                Text = "Sort Plates by Size",
                ToolTipText = "Sort plates by size",
            };
            button.DropDownItems.Add(largestItem);
            button.DropDownItems.Add(smallestItem);
            button.DropDownOpening += (sender, e) =>
            {
                largestItem.Enabled = !IsInspecting && PlateManager.CanSortPlatesBySize(true);
                smallestItem.Enabled = !IsInspecting && PlateManager.CanSortPlatesBySize(false);
            };
            toolStrip1.Items.Add(button);
        }

        /// <summary>
        /// Orders the plates by sheet size, largest or smallest first, as one undoable step,
        /// keeping the current plate current. The trailing empty new-plate sentinel stays last.
        /// </summary>
        /// <returns>Whether the order changed.</returns>
        public bool SortPlatesBySize(bool largestFirst)
        {
            if (IsInspecting || !PlateManager.CanSortPlatesBySize(largestFirst))
                return false;

            using (var change = BeginChange("Sort Plates"))
            {
                change.MarkChanged();
                PlateManager.SortPlatesBySize(largestFirst);
                change.Complete();
            }

            FirePlateChanged();
            return true;
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

        private void PlatesListView_RowDragStarted(object sender, int index)
        {
            if (IsInspecting || index < 0 || index >= PlateManager.MovableCount)
                return;

            var effects = DragDropEffects.None;
            if (PlateManager.MovableCount >= 2)
                effects |= DragDropEffects.Move;
            if (PlateManager.CanDuplicatePlate(index, index + 1))
                effects |= DragDropEffects.Copy;
            if (effects == DragDropEffects.None)
                return;

            try
            {
                platesListView.DoDragDrop(new PlateDragData(this, index), effects);
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
            var index = platesListView.RowAt(new Point(platesListView.ClientSize.Width / 2, client.Y));
            if (index < 0)
            {
                var top = platesListView.TopIndex;
                return top < platesListView.Items.Count && client.Y < platesListView.GetItemRectangle(top).Top
                    ? top
                    : movable;
            }

            var bounds = platesListView.GetItemRectangle(index);
            var slot = client.Y < bounds.Top + bounds.Height / 2 ? index : index + 1;
            return System.Math.Min(slot, movable);
        }

        /// <summary>The index a plate dragged from <paramref name="from"/> lands at when dropped in <paramref name="slot"/>.</summary>
        internal static int PlateDropTarget(int from, int slot) => slot > from ? slot - 1 : slot;

        private void ShowPlateDropIndicator(int slot)
        {
            if (slot == plateDropSlot && plateDropIndicator.Visible)
                return;

            plateDropSlot = slot;
            var count = platesListView.Items.Count;
            if (count == 0)
                return;

            var y = slot < count
                ? platesListView.GetItemRectangle(slot).Top
                : platesListView.GetItemRectangle(slot - 1).Bottom;
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
            var count = platesListView.Items.Count;
            var top = platesListView.TopIndex;
            if (count == 0 || top >= count)
                return;

            var margin = platesListView.ItemHeight;
            var visibleRows = System.Math.Max(1, platesListView.ClientSize.Height / margin);
            if (client.Y < margin && top > 0)
            {
                HidePlateDropIndicator();
                platesListView.TopIndex = top - 1;
            }
            else if (client.Y > platesListView.ClientSize.Height - margin && top + visibleRows < count)
            {
                HidePlateDropIndicator();
                platesListView.TopIndex = top + 1;
            }
        }
    }
}
