using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Controls
{
    /// <summary>
    /// Draws a sample part with one hole and the leads <see cref="CuttingParameters"/> would cut
    /// them with. The part is sized from the leads so they stay readable whatever their size;
    /// the leads of <see cref="Emphasis"/> are drawn strong, the other contour's faint.
    /// </summary>
    public class LeadPreview : Control
    {
        private const float Inset = 10;

        // Perimeters run CW and holes CCW, as most parts are drawn.
        private static readonly RotationType OutsideTurn =
            ContourCuttingStrategy.LeadTurn(ContourType.External, RotationType.CW);
        private static readonly RotationType HoleTurn =
            ContourCuttingStrategy.LeadTurn(ContourType.Internal, RotationType.CCW);

        private static readonly Color MaterialColor = Color.FromArgb(232, 236, 240);
        private static readonly Color ContourColor = Color.FromArgb(70, 70, 70);
        private static readonly Color LeadColor = Color.OrangeRed;
        private static readonly Color FaintLeadColor = Color.FromArgb(110, Color.OrangeRed);

        private CuttingParameters parameters;

        public LeadPreview()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                true
            );
            BackColor = Color.White;
        }

        public ContourType Emphasis { get; set; } = ContourType.External;

        public CuttingParameters Parameters
        {
            get => parameters;
            set
            {
                parameters = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (parameters == null)
                return;

            var leadIns = new[] { parameters.ExternalLeadIn, parameters.InternalLeadIn };
            var leadOuts = new[] { parameters.ExternalLeadOut, parameters.InternalLeadOut };
            var outsideReach = Reach(leadIns[0], leadOuts[0], OutsideTurn);
            var holeReach = Reach(leadIns[1], leadOuts[1], HoleTurn);
            var unit = System.Math.Max(System.Math.Max(outsideReach, holeReach), 0.1);

            // The hole holds its own leads with room to spare; the part holds the hole.
            var holeRadius = System.Math.Max(holeReach * 1.4, unit * 0.6);
            var height = 2 * holeRadius + 2 * unit;
            var width = height * 1.6;
            var holeCenter = new Vector(width * 0.4, height / 2);

            var outsidePoint = new Vector(width, height / 2);
            var holePoint = new Vector(holeCenter.X + holeRadius, holeCenter.Y);

            using var material = new GraphicsPath(FillMode.Alternate);
            material.AddRectangle(new RectangleF(0, 0, (float)width, (float)height));
            material.AddEllipse(
                (float)(holeCenter.X - holeRadius),
                (float)(holeCenter.Y - holeRadius),
                (float)(2 * holeRadius),
                (float)(2 * holeRadius)
            );

            using var outsideLeads = LeadPath(leadIns[0], leadOuts[0], outsidePoint, 0, OutsideTurn);
            using var holeLeads = LeadPath(leadIns[1], leadOuts[1], holePoint, System.Math.PI, HoleTurn);
            var outsidePierce = Pierce(leadIns[0], outsidePoint, 0, OutsideTurn);
            var holePierce = Pierce(leadIns[1], holePoint, System.Math.PI, HoleTurn);

            var bounds = material.GetBounds();
            bounds = Union(bounds, outsideLeads.GetBounds(), holeLeads.GetBounds());
            bounds = Union(bounds, new RectangleF(outsidePierce, SizeF.Empty), new RectangleF(holePierce, SizeF.Empty));

            var view = new RectangleF(Inset, Inset, Width - 2 * Inset, Height - 2 * Inset);
            if (view.Width <= 0 || view.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
                return;

            // Fit and center, flipping Y so the preview reads like the plate view.
            var scale = System.Math.Min(view.Width / bounds.Width, view.Height / bounds.Height);
            using var transform = new Matrix();
            transform.Translate(
                view.Left + (view.Width - bounds.Width * scale) / 2,
                view.Top + (view.Height + bounds.Height * scale) / 2
            );
            transform.Scale(scale, -scale);
            transform.Translate(-bounds.Left, -bounds.Top);

            material.Transform(transform);
            outsideLeads.Transform(transform);
            holeLeads.Transform(transform);
            var pierces = new[] { outsidePierce, holePierce };
            transform.TransformPoints(pierces);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var fill = new SolidBrush(MaterialColor))
                g.FillPath(fill, material);
            using (var outline = new Pen(ContourColor, 1.5f))
                g.DrawPath(outline, material);

            var outsideStrong = Emphasis == ContourType.External;
            DrawLeads(g, outsideLeads, pierces[0], outsideStrong);
            DrawLeads(g, holeLeads, pierces[1], !outsideStrong);
        }

        private static void DrawLeads(Graphics g, GraphicsPath leads, PointF pierce, bool strong)
        {
            var color = strong ? LeadColor : FaintLeadColor;
            using var pen = new Pen(color, strong ? 2f : 1.25f);
            g.DrawPath(pen, leads);

            var size = strong ? 6f : 4f;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, pierce.X - size / 2, pierce.Y - size / 2, size, size);
        }

        /// <summary>How far a contour's leads reach from their contour point.</summary>
        private static double Reach(LeadIn leadIn, LeadOut leadOut, RotationType turn)
        {
            using var path = LeadPath(leadIn, leadOut, Vector.Zero, 0, turn);
            var bounds = Union(path.GetBounds(), new RectangleF(Pierce(leadIn, Vector.Zero, 0, turn), SizeF.Empty));
            return System.Math.Max(
                System.Math.Max(System.Math.Abs(bounds.Left), System.Math.Abs(bounds.Right)),
                System.Math.Max(System.Math.Abs(bounds.Top), System.Math.Abs(bounds.Bottom))
            );
        }

        private static GraphicsPath LeadPath(LeadIn leadIn, LeadOut leadOut, Vector point, double normal,
            RotationType turn)
        {
            var program = new Program(Mode.Absolute);
            program.Codes.Add(new RapidMove(point));
            if (leadIn != null)
                program.Codes.AddRange(leadIn.Generate(point, normal, turn));
            // The lead-out leaves from where the cut ends: back at the lead-in's contour point.
            program.Codes.Add(new RapidMove(point));
            if (leadOut != null)
                program.Codes.AddRange(leadOut.Generate(point, normal, turn));

            program.GetGraphicsPaths(Vector.Zero, out var cutPath, out var leadPath);
            cutPath.Dispose();
            return leadPath;
        }

        private static PointF Pierce(LeadIn leadIn, Vector point, double normal, RotationType turn)
        {
            var pierce = leadIn?.GetPiercePoint(point, normal, turn) ?? point;
            return new PointF((float)pierce.X, (float)pierce.Y);
        }

        private static RectangleF Union(RectangleF bounds, params RectangleF[] others)
        {
            foreach (var other in others)
            {
                // An empty lead path reports an empty rectangle at the origin; skip it.
                if (other.Width <= 0 && other.Height <= 0 && other.Location == PointF.Empty)
                    continue;
                bounds = RectangleF.FromLTRB(
                    System.Math.Min(bounds.Left, other.Left),
                    System.Math.Min(bounds.Top, other.Top),
                    System.Math.Max(bounds.Right, other.Right),
                    System.Math.Max(bounds.Bottom, other.Bottom)
                );
            }
            return bounds;
        }
    }
}
