using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace OpenNest.Controls
{
    internal class PlateRenderer
    {
        private readonly PlateView view;

        public PlateRenderer(PlateView view)
        {
            this.view = view;
        }

        public void DrawPlate(Graphics g)
        {
            var plate = view.Plate;
            var plateRect = new RectangleF
            {
                Width = view.LengthWorldToGui(plate.Size.Length),
                Height = view.LengthWorldToGui(plate.Size.Width)
            };

            var edgeSpacingRect = new RectangleF
            {
                Width = view.LengthWorldToGui(plate.Size.Length - plate.EdgeSpacing.Left - plate.EdgeSpacing.Right),
                Height = view.LengthWorldToGui(plate.Size.Width - plate.EdgeSpacing.Top - plate.EdgeSpacing.Bottom)
            };

            switch (plate.Quadrant)
            {
                case 1:
                    plateRect.Location = view.PointWorldToGraph(0, 0);
                    edgeSpacingRect.Location = view.PointWorldToGraph(
                        plate.EdgeSpacing.Left,
                        plate.EdgeSpacing.Bottom);
                    break;

                case 2:
                    plateRect.Location = view.PointWorldToGraph(-plate.Size.Length, 0);
                    edgeSpacingRect.Location = view.PointWorldToGraph(
                        plate.EdgeSpacing.Left - plate.Size.Length,
                        plate.EdgeSpacing.Bottom);
                    break;

                case 3:
                    plateRect.Location = view.PointWorldToGraph(-plate.Size.Length, -plate.Size.Width);
                    edgeSpacingRect.Location = view.PointWorldToGraph(
                        plate.EdgeSpacing.Left - plate.Size.Length,
                        plate.EdgeSpacing.Bottom - plate.Size.Width);
                    break;

                case 4:
                    plateRect.Location = view.PointWorldToGraph(0, -plate.Size.Width);
                    edgeSpacingRect.Location = view.PointWorldToGraph(
                        plate.EdgeSpacing.Left,
                        plate.EdgeSpacing.Bottom - plate.Size.Width);
                    break;

                default:
                    return;
            }

            plateRect.Y -= plateRect.Height;
            edgeSpacingRect.Y -= edgeSpacingRect.Height;

            g.FillRectangle(view.ColorScheme.LayoutFillBrush, plateRect);

            var viewBounds = view.GetViewBounds();

            if (!edgeSpacingRect.Contains(viewBounds))
            {
                g.DrawRectangle(view.ColorScheme.EdgeSpacingPen,
                   edgeSpacingRect.X,
                   edgeSpacingRect.Y,
                   edgeSpacingRect.Width,
                   edgeSpacingRect.Height);
            }

            g.DrawRectangle(view.ColorScheme.LayoutOutlinePen,
                plateRect.X,
                plateRect.Y,
                plateRect.Width,
                plateRect.Height);
        }

        public void DrawParts(Graphics g)
        {
            var viewBounds = view.GetViewBounds();
            var layoutParts = view.LayoutParts;

            for (var i = 0; i < layoutParts.Count; ++i)
            {
                var part = layoutParts[i];

                if (part.IsDirty)
                    part.Update(view);

                var path = part.Path;
                var pathBounds = path.GetBounds();

                if (!pathBounds.IntersectsWith(viewBounds))
                    continue;

                part.Draw(g, (i + 1).ToString());
                DrawBendLines(g, part.BasePart);
                DrawEtchMarks(g, part.BasePart);
                DrawGrainWarning(g, part.BasePart);
            }

            var previewParts = view.PreviewParts;
            var previewBrush = view.PreviewBrush;
            var previewPen = view.PreviewPen;

            for (var i = 0; i < previewParts.Count; i++)
            {
                var part = previewParts[i];

                if (part.IsDirty)
                    part.Update(view);

                var path = part.Path;
                if (!path.GetBounds().IntersectsWith(viewBounds))
                    continue;

                g.FillPath(previewBrush, path);
                g.DrawPath(previewPen, path);
            }

            if (view.DrawOffset && view.Plate.PartSpacing > 0)
                DrawOffsetGeometry(g);

            if (view.DrawBounds)
            {
                var bounds = view.SelectedParts.Select(p => p.BasePart).ToList().GetBoundingBox();
                DrawBox(g, bounds);
            }

            if (view.DrawRapid)
                DrawRapids(g);

            if (view.DrawPiercePoints)
                DrawAllPiercePoints(g);

            if (view.DrawCutDirection)
                DrawAllCutDirectionArrows(g);
        }

        public void DrawCutOffs(Graphics g)
        {
            var plate = view.Plate;
            if (plate?.CutOffs == null || plate.CutOffs.Count == 0)
                return;

            using var pen = new Pen(Color.FromArgb(64, 64, 64), 1.5f);
            using var selectedPen = new Pen(Color.FromArgb(0, 120, 255), 3.5f);

            foreach (var cutoff in plate.CutOffs)
            {
                var program = cutoff.Drawing?.Program;
                if (program == null || program.Codes.Count == 0)
                    continue;

                var activePen = view.Selection.SelectedCutOffs.Contains(cutoff) ? selectedPen : pen;

                for (var i = 0; i < program.Codes.Count - 1; i += 2)
                {
                    if (program.Codes[i] is RapidMove rapid &&
                        program.Codes[i + 1] is LinearMove linear)
                    {
                        DrawLine(g, rapid.EndPoint, linear.EndPoint, activePen);
                    }
                }
            }
        }

        public void DrawActiveWorkArea(Graphics g)
        {
            var workArea = view.ActiveWorkArea;
            if (workArea == null)
                return;

            var rect = new RectangleF
            {
                Location = view.PointWorldToGraph(workArea.Location),
                Width = view.LengthWorldToGui(workArea.Length),
                Height = view.LengthWorldToGui(workArea.Width)
            };
            rect.Y -= rect.Height;

            using var pen = new Pen(Color.Red, 1.5f)
            {
                DashStyle = DashStyle.Dash
            };
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static readonly Color[] PriorityFills =
        {
            Color.FromArgb(60, Color.LimeGreen),
            Color.FromArgb(60, Color.Gold),
            Color.FromArgb(60, Color.Salmon),
        };

        private static readonly Color[] PriorityBorders =
        {
            Color.FromArgb(180, Color.Green),
            Color.FromArgb(180, Color.DarkGoldenrod),
            Color.FromArgb(180, Color.DarkRed),
        };

        public void DrawDebugRemnants(Graphics g)
        {
            var remnants = view.DebugRemnants;
            if (remnants == null || remnants.Count == 0)
                return;

            for (var i = 0; i < remnants.Count; i++)
            {
                var box = remnants[i];
                var loc = view.PointWorldToGraph(box.Location);
                var w = view.LengthWorldToGui(box.Length);
                var h = view.LengthWorldToGui(box.Width);
                var rect = new RectangleF(loc.X, loc.Y - h, w, h);

                var priority = view.DebugRemnantPriorities != null && i < view.DebugRemnantPriorities.Count
                    ? System.Math.Min(view.DebugRemnantPriorities[i], 2)
                    : 0;

                using var brush = new SolidBrush(PriorityFills[priority]);
                g.FillRectangle(brush, rect);

                using var pen = new Pen(PriorityBorders[priority], 1.5f);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

                var label = $"P{priority} {box.Width:F1}x{box.Length:F1}";
                using var font = new Font("Segoe UI", 8f);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, font, Brushes.Black, rect, sf);
            }
        }

        private void DrawBendLines(Graphics g, Part part)
        {
            if (!view.ShowBendLines || part.BaseDrawing.Bends == null || part.BaseDrawing.Bends.Count == 0)
                return;

            using var bendPen = new Pen(Color.Yellow, 1.5f)
            {
                DashStyle = System.Drawing.Drawing2D.DashStyle.Dash
            };

            foreach (var bend in part.BaseDrawing.Bends)
            {
                var start = bend.StartPoint;
                var end = bend.EndPoint;

                if (part.Rotation != 0)
                {
                    start = start.Rotate(part.Rotation);
                    end = end.Rotate(part.Rotation);
                }

                start = start + part.Location;
                end = end + part.Location;

                var pt1 = view.PointWorldToGraph(start);
                var pt2 = view.PointWorldToGraph(end);

                g.DrawLine(bendPen, pt1, pt2);
            }
        }

        private void DrawEtchMarks(Graphics g, Part part)
        {
            if (!view.ShowBendLines || part.BaseDrawing.Bends == null || part.BaseDrawing.Bends.Count == 0)
                return;

            using var etchPen = new Pen(Color.Green, 1.5f);
            var etchLength = 1.0;

            foreach (var bend in part.BaseDrawing.Bends)
            {
                if (bend.Direction != BendDirection.Up)
                    continue;

                var start = bend.StartPoint;
                var end = bend.EndPoint;

                if (part.Rotation != 0)
                {
                    start = start.Rotate(part.Rotation);
                    end = end.Rotate(part.Rotation);
                }

                start = start + part.Location;
                end = end + part.Location;

                var length = bend.Length;
                var angle = bend.StartPoint.AngleTo(bend.EndPoint) + part.Rotation;

                if (length < etchLength * 3.0)
                {
                    var pt1 = view.PointWorldToGraph(start);
                    var pt2 = view.PointWorldToGraph(end);
                    g.DrawLine(etchPen, pt1, pt2);
                }
                else
                {
                    var dx = System.Math.Cos(angle) * etchLength;
                    var dy = System.Math.Sin(angle) * etchLength;

                    var s1 = view.PointWorldToGraph(start);
                    var e1 = view.PointWorldToGraph(new Vector(start.X + dx, start.Y + dy));
                    g.DrawLine(etchPen, s1, e1);

                    var s2 = view.PointWorldToGraph(end);
                    var e2 = view.PointWorldToGraph(new Vector(end.X - dx, end.Y - dy));
                    g.DrawLine(etchPen, s2, e2);
                }
            }
        }

        private void DrawGrainWarning(Graphics g, Part part)
        {
            var plate = view.Plate;
            if (!view.ShowBendLines || plate == null || part.BaseDrawing.Bends == null || part.BaseDrawing.Bends.Count == 0)
                return;

            var grainAngle = plate.GrainAngle;
            var tolerance = Angle.ToRadians(5);

            foreach (var bend in part.BaseDrawing.Bends)
            {
                var bendAngle = bend.LineAngle + part.Rotation;
                bendAngle = bendAngle % System.Math.PI;
                if (bendAngle < 0) bendAngle += System.Math.PI;

                var grainNormalized = grainAngle % System.Math.PI;
                if (grainNormalized < 0) grainNormalized += System.Math.PI;

                var diff = System.Math.Abs(bendAngle - grainNormalized);
                diff = System.Math.Min(diff, System.Math.PI - diff);

                if (diff > tolerance)
                {
                    var box = part.BaseDrawing.Program.BoundingBox();
                    var location = part.Location;
                    var pt1 = view.PointWorldToGraph(location);
                    var pt2 = view.PointWorldToGraph(new Vector(
                        location.X + box.Length, location.Y + box.Width));
                    using var warnPen = new Pen(Color.FromArgb(180, 255, 140, 0), 2f);
                    g.DrawRectangle(warnPen, pt1.X, pt2.Y,
                        System.Math.Abs(pt2.X - pt1.X), System.Math.Abs(pt2.Y - pt1.Y));
                    return;
                }
            }
        }

        private void DrawOffsetGeometry(Graphics g)
        {
            var layoutParts = view.LayoutParts;
            using var offsetPen = new Pen(Color.FromArgb(120, 255, 100, 100));

            for (var i = 0; i < layoutParts.Count; i++)
            {
                var layoutPart = layoutParts[i];

                if (layoutPart.IsDirty)
                    layoutPart.Update(view);

                layoutPart.UpdateOffset(view.Plate.PartSpacing, view.OffsetTolerance, view.Matrix);

                if (layoutPart.OffsetPath != null)
                    g.DrawPath(offsetPen, layoutPart.OffsetPath);
            }
        }

        private void DrawRapids(Graphics g)
        {
            var pos = new Vector(0, 0);

            for (var i = 0; i < view.Plate.Parts.Count; ++i)
            {
                var part = view.Plate.Parts[i];
                var pgm = part.Program;

                var piercePoint = GetFirstPiercePoint(pgm, part.Location);
                DrawLine(g, pos, piercePoint, view.ColorScheme.RapidPen);

                pos = piercePoint;
                DrawRapids(g, pgm, part.Location, ref pos, skipFirstRapid: true);
            }
        }

        private static Vector GetFirstPiercePoint(Program pgm, Vector partLocation)
        {
            for (var i = 0; i < pgm.Length; i++)
            {
                if (pgm[i] is SubProgramCall call && call.Program != null)
                    return GetFirstPiercePoint(call.Program, partLocation + call.Offset);

                if (pgm[i] is Motion motion)
                {
                    return motion.EndPoint + partLocation;
                }
            }
            return partLocation;
        }

        private void DrawRapids(Graphics g, Program pgm, Vector basePos, ref Vector pos, bool skipFirstRapid = false)
        {
            var firstRapidSkipped = false;

            for (var i = 0; i < pgm.Length; ++i)
            {
                var code = pgm[i];

                if (code is SubProgramCall { Program: { } program } call)
                {
                    // A SubProgramCall is a coordinate-frame shift, not a physical
                    // rapid to the hole center. The Cincinnati post emits it as a
                    // G52 bracket, so the physical rapid is the sub-program's first
                    // motion, which goes straight from here to the lead-in pierce.
                    // Look ahead for that pierce point and draw the direct rapid,
                    // then recurse with skipFirstRapid so the sub doesn't also draw
                    // its first rapid on top. See docs/cincinnati-post-output.md.
                    var holeBase = basePos + call.Offset;
                    var firstPierce = GetFirstPiercePoint(program, holeBase);

                    if (ShouldDrawRapid(skipFirstRapid, ref firstRapidSkipped))
                        DrawLine(g, pos, firstPierce, view.ColorScheme.RapidPen);

                    var subPos = holeBase;
                    DrawRapids(g, program, holeBase, ref subPos, skipFirstRapid: true);
                    pos = subPos;
                }
                else if (code is Motion motion)
                {
                    var endpt = pgm.Mode == Mode.Incremental
                        ? motion.EndPoint + pos
                        : motion.EndPoint;

                    if (code.Type == CodeType.RapidMove && ShouldDrawRapid(skipFirstRapid, ref firstRapidSkipped))
                        DrawLine(g, pos, endpt, view.ColorScheme.RapidPen);

                    pos = endpt;
                }
            }
        }

        private static bool ShouldDrawRapid(bool skipFirstRapid, ref bool firstRapidSkipped)
        {
            if (skipFirstRapid && !firstRapidSkipped)
            {
                firstRapidSkipped = true;
                return false;
            }
            return true;
        }

        private void DrawAllPiercePoints(Graphics g)
        {
            using var brush = new SolidBrush(Color.Red);
            using var pen = new Pen(Color.DarkRed, 1f);

            for (var i = 0; i < view.Plate.Parts.Count; ++i)
            {
                var part = view.Plate.Parts[i];
                var pgm = part.Program;
                var pos = part.Location;
                DrawProgramPiercePoints(g, pgm, part.Location, ref pos, brush, pen);
            }
        }

        private void DrawProgramPiercePoints(Graphics g, Program pgm, Vector basePos, ref Vector pos, Brush brush, Pen pen)
        {
            for (var i = 0; i < pgm.Length; ++i)
            {
                var code = pgm[i];

                if (code.Type == CodeType.SubProgramCall)
                {
                    var subpgm = (SubProgramCall)code;
                    if (subpgm.Program != null)
                    {
                        var holeBase = basePos + subpgm.Offset;
                        pos = holeBase;
                        DrawProgramPiercePoints(g, subpgm.Program, holeBase, ref pos, brush, pen);
                    }
                }
                else
                {
                    var motion = code as Motion;
                    if (motion == null) continue;

                    var endpt = pgm.Mode == Mode.Incremental
                        ? motion.EndPoint + pos
                        : motion.EndPoint + basePos;

                    if (code.Type == CodeType.RapidMove)
                    {
                        var pt = view.PointWorldToGraph(endpt);
                        var radius = 2f;
                        g.FillEllipse(brush, pt.X - radius, pt.Y - radius, radius * 2, radius * 2);
                        g.DrawEllipse(pen, pt.X - radius, pt.Y - radius, radius * 2, radius * 2);
                    }

                    pos = endpt;
                }
            }
        }

        private void DrawAllCutDirectionArrows(Graphics g)
        {
            using var pen = new Pen(Color.FromArgb(60, 60, 60), 1.5f);

            var arrowSpacingWorld = view.LengthGuiToWorld(60f);
            var arrowSize = 6f;

            for (var i = 0; i < view.Plate.Parts.Count; ++i)
            {
                var part = view.Plate.Parts[i];
                var pgm = part.Program;
                var pos = part.Location;
                CutDirectionArrows.DrawProgram(g, view, pgm, ref pos, pen, arrowSpacingWorld, arrowSize);
            }
        }

        private void DrawLine(Graphics g, Vector pt1, Vector pt2, Pen pen)
        {
            var point1 = view.PointWorldToGraph(pt1);
            var point2 = view.PointWorldToGraph(pt2);

            g.DrawLine(pen, point1, point2);
        }

        private void DrawBox(Graphics g, Box box)
        {
            var rect = new RectangleF
            {
                Location = view.PointWorldToGraph(box.Location),
                Width = view.LengthWorldToGui(box.Length),
                Height = view.LengthWorldToGui(box.Width)
            };

            g.DrawRectangle(view.ColorScheme.BoundingBoxPen, rect.X, rect.Y - rect.Height, rect.Width, rect.Height);
        }
    }
}
