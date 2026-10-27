using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.Actions
{
    /// <summary>
    /// Plate &gt; Edit Clamps: moves, adds and deletes the nest's machine clamps directly on
    /// the plate view. Drag a clamp along its edge or onto another edge, click near an edge
    /// to add one, click a clamp's locating dimension to type its position, Delete removes
    /// the selected clamp. Every change replaces the nest's <see cref="ClampRail"/> through
    /// <see cref="Nest.SetClampRail"/>, so all sheets follow it, exactly like the Clamps
    /// dialog. Dimensions measure from the plate origin to the clamp center or to the clamp
    /// edge nearer the origin (<c>ClampDimensionToCenter</c> setting).
    /// </summary>
    [DisplayName("Edit Clamps")]
    public class ActionClampEdit : Action
    {
        // Screen sizes, in pixels.
        private const float ExtensionLength = 22f;
        // End-edge labels sit beside the sheet: a shorter line keeps them inside zoom-to-fit's border.
        private const float EndExtensionLength = 8f;
        private const float LabelPadding = 3f;
        private const float EdgeReach = 16f;
        private const float HitTolerance = 3f;
        private const int DragThreshold = 3;

        private static readonly Color HandleColor = Color.FromArgb(220, 30, 110, 200);
        private static readonly Color HoverColor = Color.FromArgb(200, 255, 140, 0);
        private static readonly Color DimensionColor = Color.FromArgb(235, 230, 120, 20);

        private readonly List<(RailClamp Clamp, RectangleF Bounds)> labels = new();
        private RailClamp? selected;
        private RailClamp? hovered;
        private RailClamp? hoveredLabel;
        private RailClamp? ghost;
        private RailClamp? dragFrom;
        private RailClamp? dragTo;
        private double dragOffset;
        private Point dragStart;
        private bool dragMoved;
        private DimensionBox editor;
        private RailClamp editing;
        private ContextMenuStrip menu;
        private ClampSettings defaults;

        public ActionClampEdit(PlateView plateView)
            : base(plateView)
        {
            plateView.DeselectAll();
            ConnectEvents();
        }

        public override bool SurvivesPlateChange => true;

        public override bool ResumeOnEscape => false;

        private static bool DimensionToCenter
        {
            get => Properties.Settings.Default.ClampDimensionToCenter;
            set
            {
                Properties.Settings.Default.ClampDimensionToCenter = value;
                Properties.Settings.Default.Save();
            }
        }

        private Nest Nest => plateView.ClampNest?.Invoke();

        private Plate Plate => plateView.Plate;

        private ClampRail Rail => Nest?.PlateDefaults.ClampRail;

        private IReadOnlyList<RailClamp> Clamps => Rail?.Clamps ?? Array.Empty<RailClamp>();

        private Units Units => Nest?.Units ?? Units.Inches;

        // A nest without clamps takes its first clamp's size from its machine's clamps.
        private ClampSettings Defaults =>
            defaults ??= MachineSettings.LoadClampSettings(MachineSettings.FindMachine(Nest?.Machine)).ConvertedTo(Units);

        private double ClampWidth => Rail?.Width ?? Defaults.Width;

        private double ClampDepth => Rail?.Depth ?? Defaults.Depth;

        public override void ConnectEvents()
        {
            plateView.MouseDown += OnMouseDown;
            plateView.MouseMove += OnMouseMove;
            plateView.MouseUp += OnMouseUp;
            plateView.KeyDown += OnKeyDown;
            plateView.Paint += OnPaint;
            plateView.Invalidate();
        }

        public override void DisconnectEvents()
        {
            plateView.MouseDown -= OnMouseDown;
            plateView.MouseMove -= OnMouseMove;
            plateView.MouseUp -= OnMouseUp;
            plateView.KeyDown -= OnKeyDown;
            plateView.Paint -= OnPaint;
            CloseEditor(apply: false);
            menu?.Dispose();
            menu = null;
            plateView.Cursor = Cursors.Cross;
            plateView.Invalidate();
        }

        public override void CancelAction()
        {
            CloseEditor(apply: false);
            dragFrom = null;
            dragTo = null;
            plateView.Invalidate();
        }

        public override bool IsBusy() => editor != null || dragFrom != null;

        public override void OnPlateChanged()
        {
            CancelAction();
            hovered = hoveredLabel = ghost = null;
        }

        #region Geometry (the parts' placement frame: plate origin at 0,0)

        private int SignX => Plate.Quadrant is 2 or 3 ? -1 : 1;

        private int SignY => Plate.Quadrant is 3 or 4 ? -1 : 1;

        private double EdgeLength(ClampEdge edge) =>
            edge.RunsAlongX() ? Plate.Size.Length : Plate.Size.Width;

        /// <summary>Unit vector out of the sheet across <paramref name="edge"/>.</summary>
        private Vector Outward(ClampEdge edge) =>
            edge switch
            {
                ClampEdge.OriginSide => new Vector(0, -SignY),
                ClampEdge.FarSide => new Vector(0, SignY),
                ClampEdge.OriginEnd => new Vector(-SignX, 0),
                _ => new Vector(SignX, 0),
            };

        /// <summary>The point on <paramref name="edge"/> <paramref name="distance"/> from the origin.</summary>
        private Vector PointOnEdge(ClampEdge edge, double distance) =>
            edge switch
            {
                ClampEdge.OriginSide => new Vector(SignX * distance, 0),
                ClampEdge.FarSide => new Vector(SignX * distance, SignY * Plate.Size.Width),
                ClampEdge.OriginEnd => new Vector(0, SignY * distance),
                _ => new Vector(SignX * Plate.Size.Length, SignY * distance),
            };

        private Box Footprint(RailClamp clamp)
        {
            var box = ClampRail.Footprint(clamp, ClampWidth, ClampDepth, Plate.Size, Plate.Quadrant);
            return box?.Translate(SignX < 0 ? -Plate.Size.Length : 0, SignY < 0 ? -Plate.Size.Width : 0);
        }

        /// <summary>The value a dimension shows for a clamp centered at <paramref name="center"/>.</summary>
        private double Measure(double center) => DimensionToCenter ? center : center - ClampWidth / 2;

        private double CenterFor(double measure) => DimensionToCenter ? measure : measure + ClampWidth / 2;

        private double SnapIncrement => Units == Units.Millimeters ? 1.0 : 0.125;

        /// <summary>
        /// The clamp position on the sheet edge nearest <paramref name="point"/>, or null when
        /// that edge is farther than <paramref name="reach"/>. The along-edge distance stays on
        /// the sheet and snaps the dimension's value unless Shift is held.
        /// </summary>
        private RailClamp? Project(Vector point, double reach, double offset = 0, ClampEdge? offsetEdge = null)
        {
            RailClamp? best = null;
            var bestDistance = double.MaxValue;
            foreach (var edge in ClampEdgeText.All)
            {
                var length = EdgeLength(edge);
                var along = edge.RunsAlongX() ? SignX * point.X : SignY * point.Y;
                along = System.Math.Clamp(along, 0, length);
                var distance = PointOnEdge(edge, along).DistanceTo(point);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;

                var center = along + (edge == offsetEdge ? offset : 0);
                if ((Control.ModifierKeys & Keys.Shift) == 0)
                {
                    var step = SnapIncrement;
                    center = CenterFor(System.Math.Round(Measure(center) / step) * step);
                }
                best = new RailClamp(edge, System.Math.Clamp(center, 0, length));
            }
            return bestDistance <= reach ? best : null;
        }

        private RailClamp? ClampAt(Vector point)
        {
            var tolerance = HitTolerance / plateView.ViewScale;
            foreach (var clamp in Clamps)
            {
                var box = Footprint(clamp);
                if (box != null && box.Offset(tolerance).Contains(point))
                    return clamp;
            }
            return null;
        }

        private RailClamp? LabelAt(Point location)
        {
            var point = plateView.PointControlToGraph(location);
            foreach (var label in labels)
            {
                if (label.Bounds.Contains(point))
                    return label.Clamp;
            }
            return null;
        }

        #endregion

        #region Mouse and keys

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || Nest == null)
                return;

            var label = LabelAt(e.Location);
            if (label != null)
            {
                selected = label;
                OpenEditor(label.Value);
                return;
            }

            var clamp = ClampAt(plateView.CurrentPoint);
            if (clamp != null)
            {
                selected = clamp;
                dragFrom = clamp;
                dragTo = null;
                dragMoved = false;
                dragStart = e.Location;
                var c = clamp.Value;
                var grabbed = c.Edge.RunsAlongX() ? SignX * plateView.CurrentPoint.X : SignY * plateView.CurrentPoint.Y;
                dragOffset = c.Center - grabbed;
                plateView.Status = $"{Describe(c)}: drag along any edge (Shift: no snap)";
            }
            else if (ghost != null)
            {
                Add(ghost.Value);
                ghost = null;
            }
            else
            {
                selected = null;
            }
            plateView.Invalidate();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (Nest == null)
                return;

            if (dragFrom != null && e.Button == MouseButtons.Left)
            {
                if (!dragMoved
                    && System.Math.Abs(e.X - dragStart.X) <= DragThreshold
                    && System.Math.Abs(e.Y - dragStart.Y) <= DragThreshold)
                    return;
                dragMoved = true;
                dragTo = Project(plateView.CurrentPoint, double.MaxValue, dragOffset, dragFrom.Value.Edge);
                if (dragTo != null)
                    plateView.Status = $"Move to {Format(Measure(dragTo.Value.Center))} on {ClampEdgeText.Phrase(dragTo.Value.Edge)}";
                plateView.Invalidate();
                return;
            }

            if (e.Button != MouseButtons.None)
                return;

            var label = LabelAt(e.Location);
            var clamp = label == null ? ClampAt(plateView.CurrentPoint) : null;
            RailClamp? add = null;
            if (label == null && clamp == null)
            {
                var reach = EdgeReach / plateView.ViewScale + ClampDepth;
                add = Project(plateView.CurrentPoint, reach);
                if (add != null && Clamps.Contains(add.Value))
                    add = null;
            }

            if (label == hoveredLabel && clamp == hovered && add == ghost)
                return;

            hoveredLabel = label;
            hovered = clamp;
            ghost = add;
            plateView.Cursor = label != null ? Cursors.Hand : clamp != null ? Cursors.SizeAll : Cursors.Cross;
            plateView.Status =
                label != null ? $"Click to type the position of {Describe(label.Value)}"
                : clamp != null ? $"Drag {Describe(clamp.Value)} to move it; Delete removes the selected clamp"
                : add != null ? $"Click to add a clamp on {ClampEdgeText.Phrase(add.Value.Edge)} at {Format(Measure(add.Value.Center))}"
                : "Edit Clamps: drag clamps along any edge, click near an edge to add one, Esc to finish";
            plateView.Invalidate();
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && editor == null && dragFrom == null)
            {
                ShowMenu(e.Location);
                return;
            }

            if (e.Button != MouseButtons.Left || dragFrom == null)
                return;

            var from = dragFrom.Value;
            var to = dragTo;
            dragFrom = null;
            dragTo = null;
            if (dragMoved && to != null && to.Value != from)
                Move(from, to.Value);
            plateView.Invalidate();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && selected != null && editor == null)
            {
                Delete(selected.Value);
                e.Handled = true;
            }
        }

        private void ShowMenu(Point location)
        {
            if (Nest == null)
                return;

            var clamp = ClampAt(plateView.CurrentPoint) ?? LabelAt(location);
            var add = clamp == null ? ghost : null;
            if (clamp != null)
                selected = clamp;

            menu ??= new ContextMenuStrip();
            while (menu.Items.Count > 0)
                menu.Items[0].Dispose();

            if (clamp != null)
            {
                var c = clamp.Value;
                menu.Items.Add("Edit Position...", null, (_, _) => OpenEditor(c));
                menu.Items.Add($"Delete {Describe(c)}", null, (_, _) => Delete(c));
                menu.Items.Add(new ToolStripSeparator());
            }
            else if (add != null)
            {
                var a = add.Value;
                menu.Items.Add($"Add Clamp on {CultureInfo.CurrentCulture.TextInfo.ToTitleCase(ClampEdgeText.Phrase(a.Edge))}", null, (_, _) => Add(a));
                menu.Items.Add(new ToolStripSeparator());
            }

            var toCenter = new ToolStripMenuItem("Dimension to Clamp Center") { Checked = DimensionToCenter };
            toCenter.Click += (_, _) => SetDimensionToCenter(true);
            var toEdge = new ToolStripMenuItem("Dimension to Near Edge") { Checked = !DimensionToCenter };
            toEdge.Click += (_, _) => SetDimensionToCenter(false);
            menu.Items.Add(toCenter);
            menu.Items.Add(toEdge);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Finish Editing Clamps", null, (_, _) => plateView.SetAction(typeof(ActionSelect)));

            plateView.Invalidate();
            menu.Show(plateView, location);
        }

        private void SetDimensionToCenter(bool value)
        {
            DimensionToCenter = value;
            plateView.Status = value
                ? "Clamp dimensions now locate clamp centers"
                : "Clamp dimensions now locate the clamp edge nearer the origin";
            plateView.Invalidate();
        }

        #endregion

        #region Edits

        private void Add(RailClamp clamp)
        {
            if (Clamps.Contains(clamp))
                return;
            if (Commit("Add Clamp", Clamps.Append(clamp), $"Added a clamp on {ClampEdgeText.Phrase(clamp.Edge)} at {Format(Measure(clamp.Center))}"))
                selected = clamp;
        }

        private void Move(RailClamp from, RailClamp to)
        {
            if (Clamps.Contains(to))
            {
                plateView.Status = "A clamp is already at that position.";
                return;
            }
            if (Commit("Move Clamp", Clamps.Where(c => c != from).Append(to),
                $"Moved the clamp to {Format(Measure(to.Center))} on {ClampEdgeText.Phrase(to.Edge)}"))
                selected = to;
        }

        private void Delete(RailClamp clamp)
        {
            if (Commit("Delete Clamp", Clamps.Where(c => c != clamp), $"Deleted the clamp at {Format(Measure(clamp.Center))} on {ClampEdgeText.Phrase(clamp.Edge)}"))
                selected = null;
        }

        /// <summary>
        /// Makes <paramref name="clamps"/> the nest's rail (every sheet follows it), one undo
        /// step named <paramref name="label"/>. Refused while a nest run or fill is using the
        /// clamps or a proposal is on display.
        /// </summary>
        private bool Commit(string label, IEnumerable<RailClamp> clamps, string status)
        {
            var nest = Nest;
            if (nest == null)
                return false;

            if (plateView.IsFillInProgress || plateView.HasProposalDisplay || plateView.CanEditClamps?.Invoke() == false)
            {
                MessageBox.Show(
                    plateView.FindForm(),
                    "Finish or cancel the current nesting or plate action first.",
                    "Edit Clamps",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return false;
            }

            var list = clamps.ToList();
            ClampRail rail;
            try
            {
                rail = list.Count == 0
                    ? null
                    : new ClampRail(ClampWidth, ClampDepth, list, Rail?.RemovalMinutes ?? Defaults.RemovalMinutes);
            }
            catch (ArgumentException ex)
            {
                plateView.Status = ex.Message;
                return false;
            }

            // A new rail puts back every clamp a sheet had removed, as the Clamps dialog does.
            var restored = nest.Plates.Count(p => p.RemovedClamps.Count > 0);
            using var change = plateView.BeginChange(label);
            change.MarkChanged();
            nest.SetClampRail(rail);
            change.Complete();
            plateView.Status = restored == 0
                ? status
                : $"{status}; removed clamps were put back on {restored} sheet(s)";
            plateView.Invalidate();
            return true;
        }

        #endregion

        #region Dimension editor

        private void OpenEditor(RailClamp clamp)
        {
            CloseEditor(apply: false);
            var label = labels.FirstOrDefault(l => l.Clamp == clamp);
            if (label.Bounds.IsEmpty)
                return;

            editing = clamp;
            var location = plateView.PointGraphToControl(label.Bounds.Location);
            editor = new DimensionBox
            {
                Font = plateView.Font,
                Text = Measure(clamp.Center).ToString("0.####", CultureInfo.CurrentCulture),
                Location = location,
                Width = System.Math.Max((int)label.Bounds.Width + 24, 80),
            };
            editor.Commit += (_, _) => CloseEditor(apply: true);
            editor.LostFocus += (_, _) => CloseEditor(apply: true);
            plateView.Controls.Add(editor);
            editor.Focus();
            editor.SelectAll();
            plateView.Status = DimensionToCenter
                ? "Type the distance from the origin to the clamp center, then Enter (Esc cancels)"
                : "Type the distance from the origin to the clamp's near edge, then Enter (Esc cancels)";
        }

        private void CloseEditor(bool apply)
        {
            var box = editor;
            if (box == null)
                return;
            editor = null; // first: removing the box raises LostFocus again
            var text = box.Text;
            plateView.Controls.Remove(box);
            box.Dispose();
            if (plateView.CanFocus)
                plateView.Focus();

            if (apply && TryParseLength(text, out var value))
            {
                var center = CenterFor(value);
                if (!double.IsFinite(center) || center < 0)
                    plateView.Status = "The clamp center must be at or beyond the origin.";
                else if (center != editing.Center)
                    Move(editing, new RailClamp(editing.Edge, center));
            }
            else if (apply)
            {
                plateView.Status = $"'{text}' is not a distance.";
            }
            plateView.Invalidate();
        }

        private static bool TryParseLength(string text, out double value)
        {
            text = (text ?? "").Trim().TrimEnd('"').Replace("mm", "").Replace("in", "").Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>A text box that commits on Enter instead of letting the form handle it.</summary>
        private sealed class DimensionBox : TextBox
        {
            public event EventHandler Commit;

            protected override bool IsInputKey(Keys keyData) =>
                keyData == Keys.Enter || base.IsInputKey(keyData);

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Commit?.Invoke(this, EventArgs.Empty);
                    return;
                }
                base.OnKeyDown(e);
            }
        }

        #endregion

        #region Painting

        private void OnPaint(object sender, PaintEventArgs e)
        {
            labels.Clear();
            if (Nest == null || plateView.HasProposalDisplay)
                return;

            var g = e.Graphics;
            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var shown = Clamps.ToList();
            if (dragFrom != null && dragTo != null)
            {
                DrawFootprint(g, dragFrom.Value, Color.FromArgb(150, 120, 120, 120), 1.5f, DashStyle.Dash);
                shown.Remove(dragFrom.Value);
                shown.Add(dragTo.Value);
            }

            foreach (var clamp in shown)
            {
                var active = clamp == dragTo || clamp == selected;
                var color = active ? HandleColor : clamp == hovered || clamp == hoveredLabel ? HoverColor : HandleColor;
                DrawFootprint(g, clamp, color, active ? 3f : clamp == hovered ? 2.5f : 1f, DashStyle.Solid,
                    fill: clamp == dragTo);
            }

            if (ghost != null)
                DrawGhost(g, ghost.Value);

            DrawDimensions(g, shown);
            g.SmoothingMode = smoothing;
        }

        private RectangleF ToGraph(Box box)
        {
            var location = plateView.PointWorldToGraph(box.Location);
            var width = plateView.LengthWorldToGui(box.Length);
            var height = plateView.LengthWorldToGui(box.Width);
            return new RectangleF(location.X, location.Y - height, width, height);
        }

        private void DrawFootprint(Graphics g, RailClamp clamp, Color color, float width, DashStyle dash, bool fill = false)
        {
            var box = Footprint(clamp);
            if (box == null)
                return;
            var rect = ToGraph(box);
            if (fill)
            {
                using var brush = new SolidBrush(Color.FromArgb(70, HandleColor));
                g.FillRectangle(brush, rect);
            }
            using var pen = new Pen(color, width) { DashStyle = dash };
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        }

        private void DrawGhost(Graphics g, RailClamp clamp)
        {
            var box = Footprint(clamp);
            if (box == null)
                return;
            var rect = ToGraph(box);
            using var brush = new SolidBrush(Color.FromArgb(50, HoverColor));
            using var pen = new Pen(HoverColor, 1.5f) { DashStyle = DashStyle.Dash };
            g.FillRectangle(brush, rect);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

            var size = System.Math.Min(System.Math.Min(rect.Width, rect.Height) * 0.35f, 6f);
            var cx = rect.X + rect.Width / 2;
            var cy = rect.Y + rect.Height / 2;
            using var plus = new Pen(HoverColor, 1.5f);
            g.DrawLine(plus, cx - size, cy, cx + size, cy);
            g.DrawLine(plus, cx, cy - size, cx, cy + size);
        }

        /// <summary>
        /// Ordinate dimensions: from each clamp's reference point a line runs out across the
        /// sheet edge to a label with its distance from the origin. Labels that would overlap
        /// on the same edge are pushed farther out.
        /// </summary>
        private void DrawDimensions(Graphics g, IReadOnlyList<RailClamp> clamps)
        {
            using var linePen = new Pen(DimensionColor, 1.5f);
            using var font = new Font(plateView.Font.FontFamily, 8.25f);
            using var textBrush = new SolidBrush(Color.Black);
            var placed = new List<RectangleF>();

            foreach (var group in clamps.GroupBy(c => c.Edge))
            {
                var edge = group.Key;
                var outward = Outward(edge);
                var screenOut = new PointF((float)outward.X, (float)-outward.Y);
                var alongX = edge.RunsAlongX();

                foreach (var clamp in group.OrderBy(c => c.Center))
                {
                    if (Footprint(clamp) == null)
                        continue; // beyond this sheet
                    var measure = Measure(clamp.Center);
                    var text = Format(measure);
                    var textSize = g.MeasureString(text, font);
                    var labelSize = new SizeF(textSize.Width + 2 * LabelPadding, textSize.Height + LabelPadding);
                    // End-edge labels read vertically, as on a drawing, so they stay close to the sheet.
                    if (!alongX)
                        labelSize = new SizeF(labelSize.Height, labelSize.Width);

                    var anchor = plateView.PointWorldToGraph(PointOnEdge(edge, measure));
                    var depth = plateView.LengthWorldToGui(ClampDepth);
                    var start = new PointF(anchor.X - screenOut.X * depth, anchor.Y - screenOut.Y * depth);

                    RectangleF rect;
                    PointF end;
                    var length = alongX ? ExtensionLength : EndExtensionLength;
                    var tries = 0;
                    do
                    {
                        end = new PointF(anchor.X + screenOut.X * length, anchor.Y + screenOut.Y * length);
                        rect = alongX
                            ? new RectangleF(end.X - labelSize.Width / 2, screenOut.Y > 0 ? end.Y : end.Y - labelSize.Height,
                                labelSize.Width, labelSize.Height)
                            : new RectangleF(screenOut.X > 0 ? end.X : end.X - labelSize.Width, end.Y - labelSize.Height / 2,
                                labelSize.Width, labelSize.Height);
                        length += (alongX ? labelSize.Height : labelSize.Width) + 2;
                    } while (placed.Any(p => p.IntersectsWith(RectangleF.Inflate(rect, 2, 2))) && ++tries < 8);
                    placed.Add(rect);

                    g.DrawLine(linePen, start, end);
                    var active = clamp == selected || clamp == dragTo;
                    var hot = clamp == hoveredLabel;
                    using (var back = new SolidBrush(active ? Color.FromArgb(235, 215, 232, 252)
                        : hot ? Color.FromArgb(235, 255, 236, 210) : Color.FromArgb(235, 255, 255, 255)))
                        g.FillRectangle(back, rect);
                    using (var border = new Pen(active ? HandleColor : DimensionColor, active || hot ? 1.5f : 1f))
                        g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
                    if (alongX)
                    {
                        g.DrawString(text, font, textBrush, rect.X + LabelPadding, rect.Y + LabelPadding / 2);
                    }
                    else
                    {
                        var state = g.Save();
                        g.TranslateTransform(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                        g.RotateTransform(-90);
                        g.DrawString(text, font, textBrush, -textSize.Width / 2, -textSize.Height / 2);
                        g.Restore(state);
                    }

                    // The label being dragged is not a click target until it lands.
                    if (clamp != dragTo)
                        labels.Add((clamp, rect));
                }
            }
        }

        #endregion

        private string Format(double value) =>
            Units == Units.Millimeters
                ? $"{value.ToString("0.#", CultureInfo.CurrentCulture)} mm"
                : $"{value.ToString("0.###", CultureInfo.CurrentCulture)}\"";

        private string Describe(RailClamp clamp)
        {
            var index = Clamps.ToList().IndexOf(clamp);
            return index >= 0 ? ClampRail.IdOf(index) : "the clamp";
        }
    }
}
