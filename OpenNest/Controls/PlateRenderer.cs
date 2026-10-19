using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;

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
            var plate = view.DisplayPlate;
            var plateRect = new RectangleF
            {
                Width = view.LengthWorldToGui(plate.Size.Length),
                Height = view.LengthWorldToGui(plate.Size.Width),
            };

            var edgeSpacingRect = new RectangleF
            {
                Width = view.LengthWorldToGui(
                    plate.Size.Length - plate.EdgeSpacing.Left - plate.EdgeSpacing.Right
                ),
                Height = view.LengthWorldToGui(
                    plate.Size.Width - plate.EdgeSpacing.Top - plate.EdgeSpacing.Bottom
                ),
            };

            switch (plate.Quadrant)
            {
                case 1:
                    plateRect.Location = view.PointWorldToGraph(0, 0);
                    edgeSpacingRect.Location = view.PointWorldToGraph(
                        plate.EdgeSpacing.Left,
                        plate.EdgeSpacing.Bottom
                    );
                    break;

                case 2:
                    plateRect.Location = view.PointWorldToGraph(-plate.Size.Length, 0);
                    edgeSpacingRect.Location = view.PointWorldToGraph(
                        plate.EdgeSpacing.Left - plate.Size.Length,
                        plate.EdgeSpacing.Bottom
                    );
                    break;

                case 3:
                    plateRect.Location = view.PointWorldToGraph(
                        -plate.Size.Length,
                        -plate.Size.Width
                    );
                    edgeSpacingRect.Location = view.PointWorldToGraph(
                        plate.EdgeSpacing.Left - plate.Size.Length,
                        plate.EdgeSpacing.Bottom - plate.Size.Width
                    );
                    break;

                case 4:
                    plateRect.Location = view.PointWorldToGraph(0, -plate.Size.Width);
                    edgeSpacingRect.Location = view.PointWorldToGraph(
                        plate.EdgeSpacing.Left,
                        plate.EdgeSpacing.Bottom - plate.Size.Width
                    );
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
                g.DrawRectangle(
                    view.ColorScheme.EdgeSpacingPen,
                    edgeSpacingRect.X,
                    edgeSpacingRect.Y,
                    edgeSpacingRect.Width,
                    edgeSpacingRect.Height
                );
            }

            g.DrawRectangle(
                view.ColorScheme.LayoutOutlinePen,
                plateRect.X,
                plateRect.Y,
                plateRect.Width,
                plateRect.Height
            );
        }

        public void DrawParts(Graphics g)
        {
            var viewBounds = view.GetViewBounds();
            var layoutParts = view.DisplayLayoutParts;

            for (var i = 0; i < layoutParts.Count; ++i)
            {
                var part = layoutParts[i];

                if (part.IsDirty)
                    part.Update(view);

                if (!part.DisplayBounds.IntersectsWith(viewBounds))
                    continue;

                var finding = view.ValidationOverlay.Finding;
                var muted = finding != null && finding.PartNumber != i + 1 && finding.OtherPartNumber != i + 1
                    || !view.HasProposalDisplay && view.CutSimulation.IsPartPending(i);
                part.Draw(g, (i + 1).ToString(), muted);
                DrawBendLines(g, part.BasePart, muted);
                DrawEtchMarks(g, part.BasePart, muted);
                DrawGrainWarning(g, part.BasePart, muted);
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
                if (!part.DisplayBounds.IntersectsWith(viewBounds))
                    continue;

                g.FillPath(previewBrush, path);
                part.DrawEtch(g);
                g.DrawPath(previewPen, path);
            }

            if (view.DrawOffset && view.DisplayPlate.PartSpacing > 0)
                DrawOffsetGeometry(g);

            if (view.DrawBounds && !view.HasProposalDisplay)
            {
                var bounds = view.SelectedParts.Select(p => p.BasePart).ToList().GetBoundingBox();
                DrawBox(g, bounds);
            }

            if (view.DrawRapid || view.HasProposalDisplay)
                DrawRapids(g);

            if (view.DrawPiercePoints)
                DrawAllPiercePoints(g);

            if (view.DrawCutDirection)
                DrawAllCutDirectionArrows(g);
        }

        // Machine clamps are fixtures, not material: a hatched amber keep-out drawn over the
        // sheet and parts (translucent, so a part placed on a clamp stays visible). They take
        // part in no hit test, selection or sequencing.
        private static readonly Color ClampHatchColor = Color.FromArgb(150, 70, 80, 90);
        private static readonly Color ClampBackColor = Color.FromArgb(70, 230, 170, 40);
        private static readonly Color ClampOutlineColor = Color.FromArgb(220, 150, 100, 0);

        public void DrawClampZones(Graphics g)
        {
            var plate = view.DisplayPlate;
            if (plate == null || plate.ClampZones.Count == 0)
                return;

            // Zones are in plate coordinates (lower-left sheet corner); move them to the
            // sheet's place around the origin, as DrawPlate places the sheet itself.
            var left = plate.Quadrant is 2 or 3 ? -plate.Size.Length : 0;
            var bottom = plate.Quadrant is 3 or 4 ? -plate.Size.Width : 0;

            using var fill = new HatchBrush(HatchStyle.WideUpwardDiagonal, ClampHatchColor, ClampBackColor);
            using var pen = new Pen(ClampOutlineColor, 1.5f);

            foreach (var zone in plate.ClampZones)
            {
                var box = zone.Bounds.Translate(left, bottom);
                var rect = new RectangleF
                {
                    Location = view.PointWorldToGraph(box.Location),
                    Width = view.LengthWorldToGui(box.Length),
                    Height = view.LengthWorldToGui(box.Width),
                };
                rect.Y -= rect.Height;

                g.FillRectangle(fill, rect);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }

        // D8: a clamp taken off this sheet, by Auto Nest as a last resort or by hand (right-click).
        // Distinct from an active clamp - a
        // dashed grey outline and a small "remove" marker, still display + exclusion only
        // (no hit test, selection or sequencing).
        private static readonly Color RemovedClampOutlineColor = Color.FromArgb(200, 110, 110, 110);

        public void DrawRemovedClamps(Graphics g)
        {
            var plate = view.DisplayPlate;
            if (plate?.ClampRail == null || plate.RemovedClamps.Count == 0)
                return;

            var left = plate.Quadrant is 2 or 3 ? -plate.Size.Length : 0;
            var bottom = plate.Quadrant is 3 or 4 ? -plate.Size.Width : 0;

            using var pen = new Pen(RemovedClampOutlineColor, 1.5f) { DashStyle = DashStyle.Dash };
            using var font = new Font(FontFamily.GenericSansSerif, 7f);
            using var textBrush = new SolidBrush(RemovedClampOutlineColor);

            foreach (var zone in plate.ClampRail.ResolveFor(plate.Size, plate.Quadrant))
            {
                if (!plate.RemovedClamps.Contains(zone.Id))
                    continue;

                var box = zone.Bounds.Translate(left, bottom);
                var rect = new RectangleF
                {
                    Location = view.PointWorldToGraph(box.Location),
                    Width = view.LengthWorldToGui(box.Length),
                    Height = view.LengthWorldToGui(box.Width),
                };
                rect.Y -= rect.Height;

                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                g.DrawString("remove", font, textBrush, rect.X, rect.Y - font.GetHeight(g));
            }
        }

        // Scrap cut-off lines must stand out from the dark part outlines and the
        // sheet: a saturated orange-red dash, not another solid near-black line.
        private static readonly Color CutOffLineColor = Color.OrangeRed;

        public void DrawCutOffs(Graphics g)
        {
            var plate = view.DisplayPlate;
            if (view.HasProposalDisplay)
            {
                DrawProposedCutOffs(g, plate);
                return;
            }
            if (plate?.CutOffs == null || plate.CutOffs.Count == 0)
                return;

            using var pen = new Pen(CutOffLineColor, 2.0f) { DashStyle = DashStyle.Dash };
            using var selectedPen = new Pen(Color.FromArgb(0, 120, 255), 3.5f);

            foreach (var cutoff in plate.CutOffs)
            {
                var program = cutoff.Drawing?.Program;
                if (program == null || program.Codes.Count == 0)
                    continue;

                var activePen = view.Selection.SelectedCutOffs.Contains(cutoff) ? selectedPen : pen;

                for (var i = 0; i < program.Codes.Count - 1; i += 2)
                {
                    if (
                        program.Codes[i] is RapidMove rapid
                        && program.Codes[i + 1] is LinearMove linear
                    )
                    {
                        DrawLine(g, rapid.EndPoint, linear.EndPoint, activePen);
                    }
                }
            }
        }

        /// <summary>
        /// A proposal preview carries cut-off parts but no cut-off definitions, so draw each
        /// cut-off part's own (absolute) cutting moves in the same dashed style.
        /// </summary>
        private void DrawProposedCutOffs(Graphics g, Plate plate)
        {
            if (plate == null)
                return;

            using var pen = new Pen(CutOffLineColor, 2.0f) { DashStyle = DashStyle.Dash };

            foreach (var part in plate.Parts)
            {
                var program = part.BaseDrawing.IsCutOff ? part.Program : null;
                if (program == null || program.Mode != Mode.Absolute)
                    continue;

                Vector? position = null;
                foreach (var code in program.Codes)
                {
                    if (code is not Motion motion)
                        continue;
                    var end = motion.EndPoint + part.Location;
                    if (motion is LinearMove && position is { } start)
                        DrawLine(g, start, end, pen);
                    position = end;
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
                Height = view.LengthWorldToGui(workArea.Width),
            };
            rect.Y -= rect.Height;

            using var pen = new Pen(Color.Red, 1.5f) { DashStyle = DashStyle.Dash };
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

                var priority =
                    view.DebugRemnantPriorities != null && i < view.DebugRemnantPriorities.Count
                        ? System.Math.Min(view.DebugRemnantPriorities[i], 2)
                        : 0;

                using var brush = new SolidBrush(PriorityFills[priority]);
                g.FillRectangle(brush, rect);

                using var pen = new Pen(PriorityBorders[priority], 1.5f);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

                var label = $"P{priority} {box.Width:F1}x{box.Length:F1}";
                using var font = new Font("Segoe UI", 8f);
                using var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                };
                g.DrawString(label, font, Brushes.Black, rect, sf);
            }
        }

        private void DrawBendLines(Graphics g, Part part, bool muted)
        {
            if (
                !view.ShowBendLines
                || part.BaseDrawing.Bends == null
                || part.BaseDrawing.Bends.Count == 0
            )
                return;

            using var bendPen = new Pen(muted ? Color.Gray : Color.Yellow, 1.5f)
            {
                DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
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

        private void DrawEtchMarks(Graphics g, Part part, bool muted)
        {
            if (
                !view.ShowBendLines
                || part.BaseDrawing.Bends == null
                || part.BaseDrawing.Bends.Count == 0
            )
                return;

            using var etchPen = new Pen(muted ? Color.Gray : Color.Green, 1.5f);
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

        private void DrawGrainWarning(Graphics g, Part part, bool muted)
        {
            var plate = view.DisplayPlate;
            if (
                !view.ShowBendLines
                || plate == null
                || part.BaseDrawing.Bends == null
                || part.BaseDrawing.Bends.Count == 0
            )
                return;

            var grainAngle = plate.GrainAngle;
            var tolerance = Angle.ToRadians(5);

            foreach (var bend in part.BaseDrawing.Bends)
            {
                var bendAngle = bend.LineAngle + part.Rotation;
                bendAngle = bendAngle % System.Math.PI;
                if (bendAngle < 0)
                    bendAngle += System.Math.PI;

                var grainNormalized = grainAngle % System.Math.PI;
                if (grainNormalized < 0)
                    grainNormalized += System.Math.PI;

                var diff = System.Math.Abs(bendAngle - grainNormalized);
                diff = System.Math.Min(diff, System.Math.PI - diff);

                if (diff > tolerance)
                {
                    var box = part.BaseDrawing.Program.BoundingBox();
                    var location = part.Location;
                    var pt1 = view.PointWorldToGraph(location);
                    var pt2 = view.PointWorldToGraph(
                        new Vector(location.X + box.Length, location.Y + box.Width)
                    );
                    using var warnPen = new Pen(muted ? Color.Gray : Color.FromArgb(180, 255, 140, 0), 2f);
                    g.DrawRectangle(
                        warnPen,
                        pt1.X,
                        pt2.Y,
                        System.Math.Abs(pt2.X - pt1.X),
                        System.Math.Abs(pt2.Y - pt1.Y)
                    );
                    return;
                }
            }
        }

        private void DrawOffsetGeometry(Graphics g)
        {
            var layoutParts = view.DisplayLayoutParts;
            using var offsetPen = new Pen(Color.FromArgb(120, 255, 100, 100));

            for (var i = 0; i < layoutParts.Count; i++)
            {
                var layoutPart = layoutParts[i];

                if (layoutPart.IsDirty)
                    layoutPart.Update(view);

                layoutPart.UpdateOffset(view.DisplayPlate.PartSpacing, view.OffsetTolerance, view.Matrix);

                if (layoutPart.OffsetPath != null)
                    g.DrawPath(offsetPen, layoutPart.OffsetPath);
            }
        }

        private void DrawRapids(Graphics g)
        {
            var pen = view.ColorScheme.RapidPen;
            foreach (var segment in RapidEnumerator.Enumerate(view.DisplayPlate.Parts))
                DrawLine(g, segment.From, segment.To, pen);
        }

        private void DrawAllPiercePoints(Graphics g)
        {
            using var brush = new SolidBrush(Color.Red);
            using var pen = new Pen(Color.DarkRed, 1f);

            for (var i = 0; i < view.DisplayPlate.Parts.Count; ++i)
            {
                var part = view.DisplayPlate.Parts[i];
                var pgm = part.Program;
                var pos = part.Location;
                DrawProgramPiercePoints(g, pgm, part.Location, ref pos, brush, pen);
            }
        }

        private void DrawProgramPiercePoints(
            Graphics g,
            Program pgm,
            Vector basePos,
            ref Vector pos,
            Brush brush,
            Pen pen
        )
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
                    if (motion == null)
                        continue;

                    var endpt =
                        pgm.Mode == Mode.Incremental
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

            for (var i = 0; i < view.DisplayPlate.Parts.Count; ++i)
            {
                var part = view.DisplayPlate.Parts[i];
                var pgm = part.Program;
                var pos = part.Location;
                CutDirectionArrows.DrawProgram(
                    g,
                    view,
                    pgm,
                    ref pos,
                    pen,
                    arrowSpacingWorld,
                    arrowSize
                );
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
                Height = view.LengthWorldToGui(box.Width),
            };

            g.DrawRectangle(
                view.ColorScheme.BoundingBoxPen,
                rect.X,
                rect.Y - rect.Height,
                rect.Width,
                rect.Height
            );
        }
    }
}
