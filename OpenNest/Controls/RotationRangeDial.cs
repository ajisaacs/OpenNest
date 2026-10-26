using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using OpenNest.Engine.Jobs;
using OpenNest.Math;

namespace OpenNest.Controls
{
    /// <summary>
    /// A dial for a drawing's rotation range. The shaded wedge runs counterclockwise from the
    /// start angle to the end angle, with 0° at the right as plate rotations are measured, and
    /// a tick marks each angle the nesting engines try (<see cref="RotationPolicy.FromLegacy"/>).
    /// A zero step is the engines' automatic 0°/90°/180°/270°, so the wedge is drawn faded.
    /// Allowed 180° equivalents show as a hatched wedge opposite the range.
    /// <para>
    /// Drag a handle to move that angle in whole degrees (hold Shift for 15°); clicking the dial
    /// moves the nearer handle there. With focus, the arrow keys move the active handle and
    /// Space switches handles. Angles stay within 0° &lt;= start &lt;= end &lt;= 360°.
    /// </para>
    /// </summary>
    public class RotationRangeDial : Control
    {
        public const double MaxDegrees = 360;
        private const double Snap = 1;
        private const double ShiftSnap = 15;

        private double startAngle;
        private double endAngle = MaxDegrees;
        private double stepAngle;
        private bool allow180Equivalent;
        private DialHandle active = DialHandle.Start;
        private DialHandle hover = DialHandle.None;
        private DialHandle dragging = DialHandle.None;
        private bool dragUndecided;
        private double lastPointerAngle;
        private double dragValue;

        public RotationRangeDial()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true;
        }

        /// <summary>Raised when the operator drags a handle or uses the keyboard, not when a property is set.</summary>
        public event EventHandler AnglesChanged;

        /// <summary>The range start in degrees, 0 to <see cref="EndAngle"/>.</summary>
        [DefaultValue(0d)]
        public double StartAngle
        {
            get { return startAngle; }
            set { SetAngles(value, System.Math.Max(value, endAngle)); }
        }

        /// <summary>The range end in degrees, <see cref="StartAngle"/> to 360.</summary>
        [DefaultValue(MaxDegrees)]
        public double EndAngle
        {
            get { return endAngle; }
            set { SetAngles(System.Math.Min(startAngle, value), value); }
        }

        /// <summary>The step in degrees; zero means the engines' automatic rotations.</summary>
        [DefaultValue(0d)]
        public double StepAngle
        {
            get { return stepAngle; }
            set
            {
                var step = double.IsFinite(value) && value > 0 ? value : 0;
                if (step == stepAngle)
                    return;
                stepAngle = step;
                Invalidate();
            }
        }

        [DefaultValue(false)]
        public bool Allow180Equivalent
        {
            get { return allow180Equivalent; }
            set
            {
                if (value == allow180Equivalent)
                    return;
                allow180Equivalent = value;
                Invalidate();
            }
        }

        /// <summary>The handle the keyboard moves.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public DialHandle ActiveHandle
        {
            get { return active; }
            set
            {
                active = value == DialHandle.End ? DialHandle.End : DialHandle.Start;
                Invalidate();
            }
        }

        /// <summary>The angles, in degrees, the nesting engines try for the current settings.</summary>
        public IReadOnlyList<double> TriedAngles() =>
            TriedAngles(startAngle, endAngle, stepAngle, allow180Equivalent);

        /// <summary>The angles, in degrees in [0, 360), the nesting engines try for these settings.</summary>
        public static IReadOnlyList<double> TriedAngles(double start, double end, double step, bool allow180)
        {
            var angles = new List<double>();
            if (!double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(step) || step < 0
                || (step > 0 && end < start))
                return angles;
            var policy = RotationPolicy.FromLegacy(Angle.ToRadians(step), Angle.ToRadians(start),
                Angle.ToRadians(end), allow180);
            foreach (var radians in policy.EnumerateAngles())
                // Rounded so whole degrees survive the trip through radians.
                angles.Add(System.Math.Round(Angle.ToDegrees(radians), 9));
            return angles;
        }

        protected override Size DefaultSize => new(150, 150);

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var center = Center;
            var radius = Radius;
            if (radius < 4)
                return;
            var dial = new RectangleF(center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
            var accent = SystemColors.Highlight;
            var automatic = stepAngle <= 0;

            using (var face = new SolidBrush(SystemColors.Window))
                g.FillEllipse(face, dial);

            // Quarter guides, with their angles outside the dial.
            using (var guide = new Pen(Color.FromArgb(60, SystemColors.ControlDark)) { DashStyle = DashStyle.Dot })
            using (var labelBrush = new SolidBrush(SystemColors.GrayText))
            using (var labelFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (var quarter = 0; quarter < 4; quarter++)
                {
                    var angle = quarter * 90.0;
                    g.DrawLine(guide, center, PointAt(angle, radius));
                    g.DrawString($"{angle:0}°", Font, labelBrush, PointAt(angle, radius + LabelGap), labelFormat);
                }
            }

            if (allow180Equivalent && !automatic)
            {
                using var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(110, accent), Color.Transparent);
                FillWedge(g, hatch, dial, startAngle + 180, endAngle - startAngle);
            }

            using (var wedge = new SolidBrush(Color.FromArgb(automatic ? 35 : 90, accent)))
                FillWedge(g, wedge, dial, startAngle, endAngle - startAngle);

            using (var edge = new Pen(Color.FromArgb(automatic ? 110 : 255, accent), 1.5f))
            {
                if (automatic)
                    edge.DashStyle = DashStyle.Dash;
                g.DrawLine(edge, center, PointAt(startAngle, radius));
                g.DrawLine(edge, center, PointAt(endAngle, radius));
            }

            using (var rim = new Pen(SystemColors.ControlDark))
                g.DrawEllipse(rim, dial);

            using (var tick = new Pen(SystemColors.ControlText, 1.5f))
            {
                foreach (var angle in TriedAngles())
                    g.DrawLine(tick, PointAt(angle, radius * 0.80f), PointAt(angle, radius));
            }

            DrawHandle(g, DialHandle.Start, startAngle, accent);
            DrawHandle(g, DialHandle.End, endAngle, accent);

            if (Focused && ShowFocusCues)
            {
                var focus = Rectangle.Round(dial);
                focus.Inflate(2, 2);
                ControlPaint.DrawFocusRectangle(g, focus);
            }
        }

        private void DrawHandle(Graphics g, DialHandle handle, double angle, Color accent)
        {
            var size = HandleRadius * (handle == hover || handle == dragging ? 1.25f : 1f);
            var point = PointAt(angle, Radius);
            var bounds = new RectangleF(point.X - size, point.Y - size, 2 * size, 2 * size);
            // The start handle is hollow and the end handle solid, so they read apart when close.
            using (var fill = new SolidBrush(handle == DialHandle.End ? accent : SystemColors.Window))
                g.FillEllipse(fill, bounds);
            using (var ring = new Pen(handle == active && Focused ? SystemColors.ControlText : accent, 2f))
                g.DrawEllipse(ring, bounds);
        }

        private static void FillWedge(Graphics g, Brush brush, RectangleF dial, double from, double extent)
        {
            if (extent >= MaxDegrees - 1e-9)
                g.FillEllipse(brush, dial);
            else if (extent > 0)
                // GDI+ measures angles clockwise; a negative sweep runs counterclockwise.
                g.FillPie(brush, dial.X, dial.Y, dial.Width, dial.Height, (float)-from, (float)-extent);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            Focus();

            var pointer = AngleAt(e.Location);
            var hit = HitHandles(e.Location);
            if (hit == DialHandle.None)
            {
                // Away from the handles, jump the nearer one to the pointer and keep dragging it;
                // a tie (a full turn puts both at 0°) goes to the end.
                hit = AngularDistance(pointer, startAngle) < AngularDistance(pointer, endAngle)
                    ? DialHandle.Start
                    : DialHandle.End;
                var target = NearestWithin(pointer, Limits(hit));
                BeginDrag(hit, pointer, target);
                Drag(target, ModifierKeys.HasFlag(Keys.Shift));
                return;
            }

            // Overlapping handles are told apart by the direction of the first move.
            dragUndecided = hit == DialHandle.Both;
            BeginDrag(hit == DialHandle.Both ? DialHandle.End : hit, pointer,
                hit == DialHandle.Start ? startAngle : endAngle);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging == DialHandle.None)
            {
                var hit = HitHandles(e.Location);
                var over = hit == DialHandle.Both ? DialHandle.End : hit;
                Cursor = hit == DialHandle.None ? Cursors.Default : Cursors.Hand;
                if (over != hover)
                {
                    hover = over;
                    Invalidate();
                }
                return;
            }

            var pointer = AngleAt(e.Location);
            var delta = pointer - lastPointerAngle;
            delta -= MaxDegrees * System.Math.Round(delta / MaxDegrees);
            lastPointerAngle = pointer;
            if (delta == 0)
                return;

            if (dragUndecided)
            {
                dragUndecided = false;
                dragging = delta < 0 ? DialHandle.Start : DialHandle.End;
                active = dragging;
                dragValue = dragging == DialHandle.Start ? startAngle : endAngle;
            }

            // The drag follows the pointer continuously, so the end can sweep past 0° up to a full turn.
            Drag(dragValue + delta, ModifierKeys.HasFlag(Keys.Shift));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging == DialHandle.None)
                return;
            dragging = DialHandle.None;
            dragUndecided = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover == DialHandle.None)
                return;
            hover = DialHandle.None;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var amount = e.Shift ? ShiftSnap : Snap;
            switch (e.KeyCode)
            {
                case Keys.Right:
                case Keys.Up:
                    MoveActive(amount);
                    break;
                case Keys.Left:
                case Keys.Down:
                    MoveActive(-amount);
                    break;
                case Keys.Space:
                    ActiveHandle = active == DialHandle.Start ? DialHandle.End : DialHandle.Start;
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        /// <summary>Moves the active handle by <paramref name="degrees"/>, to the next multiple of the move.</summary>
        internal void MoveActive(double degrees)
        {
            var current = active == DialHandle.Start ? startAngle : endAngle;
            var amount = System.Math.Abs(degrees);
            var next = degrees > 0
                ? System.Math.Floor(current / amount + 1e-9) * amount + amount
                : System.Math.Ceiling(current / amount - 1e-9) * amount - amount;
            SetActive(next);
        }

        private void BeginDrag(DialHandle handle, double pointer, double value)
        {
            dragging = handle;
            active = handle;
            lastPointerAngle = pointer;
            dragValue = value;
            Capture = true;
            Invalidate();
        }

        private void Drag(double value, bool coarse)
        {
            var (low, high) = Limits(dragging);
            dragValue = System.Math.Clamp(value, low, high);
            var snap = coarse ? ShiftSnap : Snap;
            SetActive(System.Math.Round(dragValue / snap) * snap);
        }

        // Keeps the handles ordered: each moves between 0°, the other handle and 360°.
        private (double Low, double High) Limits(DialHandle handle) =>
            handle == DialHandle.Start ? (0, endAngle) : (startAngle, MaxDegrees);

        private void SetActive(double value)
        {
            var (low, high) = Limits(active);
            value = System.Math.Clamp(value, low, high);
            var changed = active == DialHandle.Start
                ? SetAngles(value, endAngle)
                : SetAngles(startAngle, value);
            if (changed)
                AnglesChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool SetAngles(double start, double end)
        {
            start = double.IsFinite(start) ? System.Math.Clamp(start, 0, MaxDegrees) : 0;
            end = double.IsFinite(end) ? System.Math.Clamp(end, start, MaxDegrees) : start;
            if (start == startAngle && end == endAngle)
                return false;
            startAngle = start;
            endAngle = end;
            Invalidate();
            return true;
        }

        private DialHandle HitHandles(Point location)
        {
            var reach = HandleRadius + 3;
            var onStart = Distance(location, PointAt(startAngle, Radius)) <= reach;
            var onEnd = Distance(location, PointAt(endAngle, Radius)) <= reach;
            if (onStart && onEnd)
                return DialHandle.Both;
            return onStart ? DialHandle.Start : onEnd ? DialHandle.End : DialHandle.None;
        }

        private PointF Center => new(Width / 2f, Height / 2f);

        // Room outside the rim for the quarter labels.
        private float LabelGap => Font.Height * 0.95f;

        private float Radius => System.Math.Min(Width, Height) / 2f - LabelGap - Font.Height * 0.6f;

        private float HandleRadius => System.Math.Max(5f, Radius * 0.09f);

        private PointF PointAt(double degrees, float radius)
        {
            var radians = Angle.ToRadians(degrees);
            var center = Center;
            return new PointF(center.X + radius * (float)System.Math.Cos(radians),
                center.Y - radius * (float)System.Math.Sin(radians));
        }

        private double AngleAt(Point location)
        {
            var center = Center;
            var degrees = Angle.ToDegrees(System.Math.Atan2(center.Y - location.Y, location.X - center.X));
            return degrees < 0 ? degrees + MaxDegrees : degrees;
        }

        // The turn of a pointer angle that falls within the limits, else the limit around the dial nearer to it.
        private static double NearestWithin(double angle, (double Low, double High) limits)
        {
            foreach (var candidate in new[] { angle, angle + MaxDegrees })
            {
                if (candidate >= limits.Low && candidate <= limits.High)
                    return candidate;
            }
            return AngularDistance(angle, limits.Low) <= AngularDistance(angle, limits.High) ? limits.Low : limits.High;
        }

        private static double AngularDistance(double a, double b)
        {
            var delta = System.Math.Abs(a - b) % MaxDegrees;
            return System.Math.Min(delta, MaxDegrees - delta);
        }

        private static float Distance(PointF a, PointF b) =>
            (float)System.Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    public enum DialHandle
    {
        None,
        Start,
        End,
        Both,
    }
}
