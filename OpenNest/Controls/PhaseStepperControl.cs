using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    public class PhaseStepperControl : UserControl
    {
        private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
        private static readonly Color GlowColor = Color.FromArgb(60, 0, 120, 212);
        private static readonly Color PendingBorder = Color.FromArgb(192, 192, 192);
        private static readonly Color LineColor = Color.FromArgb(208, 208, 208);
        private static readonly Color PendingTextColor = Color.FromArgb(153, 153, 153);
        private static readonly Color ActiveTextColor = Color.FromArgb(51, 51, 51);

        private static readonly Font LabelFont = new Font("Segoe UI", 8f, FontStyle.Regular);
        private static readonly Font BoldLabelFont = new Font("Segoe UI", 8f, FontStyle.Bold);

        private static readonly NestPhase[] Phases = (NestPhase[])Enum.GetValues(typeof(NestPhase));

        private readonly HashSet<NestPhase> visitedPhases = new();
        private NestPhase? activePhase;
        private bool isComplete;

        public PhaseStepperControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Height = 60;
        }

        public NestPhase? ActivePhase
        {
            get => activePhase;
            set
            {
                activePhase = value;
                if (value.HasValue)
                    visitedPhases.Add(value.Value);
                Invalidate();
            }
        }

        public bool IsComplete
        {
            get => isComplete;
            set
            {
                isComplete = value;
                if (value)
                {
                    foreach (var phase in Phases)
                        visitedPhases.Add(phase);
                    activePhase = null;
                }
                Invalidate();
            }
        }

        private static string GetDisplayName(NestPhase phase)
        {
            switch (phase)
            {
                case NestPhase.RectBestFit: return "BestFit";
                case NestPhase.Nfp: return "NFP";
                default: return phase.ToString();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var count = Phases.Length;
            if (count == 0) return;

            var padding = 30;
            var usableWidth = Width - padding * 2;
            var spacing = usableWidth / (count - 1);
            var circleY = 18;
            var normalRadius = 9;
            var activeRadius = 11;

            using var linePen = new Pen(LineColor, 2f);
            using var accentBrush = new SolidBrush(AccentColor);
            using var glowBrush = new SolidBrush(GlowColor);
            using var pendingPen = new Pen(PendingBorder, 2f);
            using var activeTextBrush = new SolidBrush(ActiveTextColor);
            using var pendingTextBrush = new SolidBrush(PendingTextColor);

            // Draw connecting lines
            for (var i = 0; i < count - 1; i++)
            {
                var x1 = padding + i * spacing;
                var x2 = padding + (i + 1) * spacing;
                g.DrawLine(linePen, x1, circleY, x2, circleY);
            }

            // Draw circles and labels
            for (var i = 0; i < count; i++)
            {
                var phase = Phases[i];
                var cx = padding + i * spacing;
                var isActive = activePhase == phase && !isComplete;
                var isVisited = visitedPhases.Contains(phase) || isComplete;

                if (isActive)
                {
                    // Glow
                    g.FillEllipse(glowBrush,
                        cx - activeRadius - 3, circleY - activeRadius - 3,
                        (activeRadius + 3) * 2, (activeRadius + 3) * 2);
                    // Filled circle
                    g.FillEllipse(accentBrush,
                        cx - activeRadius, circleY - activeRadius,
                        activeRadius * 2, activeRadius * 2);
                }
                else if (isVisited)
                {
                    g.FillEllipse(accentBrush,
                        cx - normalRadius, circleY - normalRadius,
                        normalRadius * 2, normalRadius * 2);
                }
                else
                {
                    g.DrawEllipse(pendingPen,
                        cx - normalRadius, circleY - normalRadius,
                        normalRadius * 2, normalRadius * 2);
                }

                // Label
                var label = GetDisplayName(phase);
                var font = isVisited || isActive ? BoldLabelFont : LabelFont;
                var brush = isVisited || isActive ? activeTextBrush : pendingTextBrush;
                var labelSize = g.MeasureString(label, font);
                g.DrawString(label, font, brush,
                    cx - labelSize.Width / 2, circleY + activeRadius + 5);
            }
        }
    }
}
