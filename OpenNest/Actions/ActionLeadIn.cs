using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenNest.Actions
{
    [DisplayName("Place Lead-in")]
    public class ActionLeadIn : Action
    {
        private LayoutPart selectedLayoutPart;
        private Part selectedPart;
        private ShapeProfile profile;
        private List<ShapeInfo> contours;
        private Vector snapPoint;
        private Entity snapEntity;
        private ContourType snapContourType;
        private double snapNormal;
        private bool hasSnap;
        private ContextMenuStrip contextMenu;

        public ActionLeadIn(PlateView plateView)
            : base(plateView)
        {
            ConnectEvents();
        }

        public override void ConnectEvents()
        {
            plateView.MouseMove += OnMouseMove;
            plateView.MouseDown += OnMouseDown;
            plateView.KeyDown += OnKeyDown;
            plateView.Paint += OnPaint;
        }

        public override void DisconnectEvents()
        {
            plateView.MouseMove -= OnMouseMove;
            plateView.MouseDown -= OnMouseDown;
            plateView.KeyDown -= OnKeyDown;
            plateView.Paint -= OnPaint;

            contextMenu?.Dispose();
            contextMenu = null;

            selectedLayoutPart = null;
            selectedPart = null;
            profile = null;
            contours = null;
            hasSnap = false;
            plateView.Invalidate();
        }

        public override void CancelAction() { }

        public override bool IsBusy() => selectedPart != null;

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (selectedPart == null || contours == null)
                return;

            var worldPt = plateView.CurrentPoint;

            // Transform world point into program-local space by subtracting the
            // part's location. The contour shapes are already in the program's
            // rotated coordinate system, so no additional un-rotation is needed.
            var localPt = new Vector(worldPt.X - selectedPart.Location.X,
                                     worldPt.Y - selectedPart.Location.Y);

            // Find closest contour and point
            var bestDist = double.MaxValue;
            hasSnap = false;

            foreach (var info in contours)
            {
                var closest = info.Shape.ClosestPointTo(localPt, out var entity);
                var dist = closest.DistanceTo(localPt);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    snapPoint = closest;
                    snapEntity = entity;
                    snapContourType = info.ContourType;
                    snapNormal = ContourCuttingStrategy.ComputeNormal(closest, entity, info.ContourType);
                    hasSnap = true;
                }
            }

            plateView.Invalidate();
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (selectedPart == null)
                {
                    // First click: select a part
                    SelectPartAtCursor();
                }
                else if (hasSnap)
                {
                    // Second click: commit lead-in at snap point
                    CommitLeadIn();
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (selectedPart != null && selectedPart.HasManualLeadIns)
                    ShowContextMenu(e.Location);
                else
                    DeselectPart();
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (selectedPart != null)
                    DeselectPart();
                else
                    plateView.SetAction(typeof(ActionSelect));
            }
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            if (!hasSnap || selectedPart == null)
                return;

            var parameters = plateView.Plate?.CuttingParameters;
            if (parameters == null)
                return;

            // Transform snap point from local part space to world space
            var worldSnap = TransformToWorld(snapPoint);

            // Get the appropriate lead-in for this contour type
            var leadIn = SelectLeadIn(parameters, snapContourType);
            if (leadIn == null)
                return;

            // Clamp lead-in for circle contours so it stays inside the hole
            if (snapContourType == ContourType.ArcCircle && snapEntity is Circle snapCircle
                && parameters.PierceClearance > 0)
            {
                var pierceCheck = leadIn.GetPiercePoint(snapPoint, snapNormal);
                var distFromCenter = pierceCheck.DistanceTo(snapCircle.Center);
                var maxRadius = snapCircle.Radius - parameters.PierceClearance;
                if (maxRadius > 0 && distFromCenter > maxRadius)
                {
                    var currentDist = snapPoint.DistanceTo(pierceCheck);
                    if (currentDist > Tolerance.Epsilon)
                    {
                        var dx = (pierceCheck.X - snapPoint.X) / currentDist;
                        var dy = (pierceCheck.Y - snapPoint.Y) / currentDist;
                        var vx = snapPoint.X - snapCircle.Center.X;
                        var vy = snapPoint.Y - snapCircle.Center.Y;
                        var b = 2.0 * (vx * dx + vy * dy);
                        var c = vx * vx + vy * vy - maxRadius * maxRadius;
                        var disc = b * b - 4.0 * c;
                        if (disc >= 0)
                        {
                            var t = (-b + System.Math.Sqrt(disc)) / 2.0;
                            if (t > 0 && t < currentDist)
                                leadIn = leadIn.Scale(t / currentDist);
                        }
                    }
                }
            }

            // Get the pierce point (in local space)
            var piercePoint = leadIn.GetPiercePoint(snapPoint, snapNormal);
            var worldPierce = TransformToWorld(piercePoint);

            var g = e.Graphics;
            var oldSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw the lead-in preview as a line from pierce point to contour point
            var pt1 = plateView.PointWorldToGraph(worldPierce);
            var pt2 = plateView.PointWorldToGraph(worldSnap);

            using var pen = new Pen(Color.Yellow, 2.0f / plateView.ViewScale);
            g.DrawLine(pen, pt1, pt2);

            // Draw a small circle at the pierce point
            var radius = 3.0f / plateView.ViewScale;
            g.FillEllipse(Brushes.Yellow, pt1.X - radius, pt1.Y - radius, radius * 2, radius * 2);

            // Draw a small circle at the contour start point
            g.FillEllipse(Brushes.Lime, pt2.X - radius, pt2.Y - radius, radius * 2, radius * 2);

            g.SmoothingMode = oldSmooth;
        }

        private void SelectPartAtCursor()
        {
            var layoutPart = plateView.GetPartAtPoint(plateView.CurrentPoint);
            if (layoutPart == null)
                return;

            var part = layoutPart.BasePart;

            // Don't allow lead-in placement on cut-off parts
            if (part.BaseDrawing.IsCutOff)
                return;

            // If part already has locked lead-ins, don't allow re-placement
            if (part.LeadInsLocked)
                return;

            selectedLayoutPart = layoutPart;
            selectedPart = part;

            // Build contour info from the part's program geometry
            BuildContourInfo();

            // Highlight the selected part
            layoutPart.IsSelected = true;
            plateView.Invalidate();
        }

        private void BuildContourInfo()
        {
            // Get a clean program (no lead-ins) in the part's current rotated space.
            // If the part has manual lead-ins, rebuild from base drawing + rotation.
            // Otherwise the current Program is already clean and rotated.
            CNC.Program cleanProgram;

            if (selectedPart.HasManualLeadIns)
            {
                cleanProgram = selectedPart.BaseDrawing.Program.Clone() as CNC.Program;
                if (!OpenNest.Math.Tolerance.IsEqualTo(selectedPart.Rotation, 0))
                    cleanProgram.Rotate(selectedPart.Rotation);
            }
            else
            {
                cleanProgram = selectedPart.Program;
            }

            var entities = ConvertProgram.ToGeometry(cleanProgram);
            entities.RemoveAll(e => e.Layer == SpecialLayers.Scribe);
            profile = new ShapeProfile(entities);

            contours = new List<ShapeInfo>();

            // Perimeter is always External
            if (profile.Perimeter != null)
            {
                contours.Add(new ShapeInfo
                {
                    Shape = profile.Perimeter,
                    ContourType = ContourType.External
                });
            }

            // Cutouts
            foreach (var cutout in profile.Cutouts)
            {
                contours.Add(new ShapeInfo
                {
                    Shape = cutout,
                    ContourType = ContourCuttingStrategy.DetectContourType(cutout)
                });
            }
        }

        private void CommitLeadIn()
        {
            var parameters = plateView.Plate?.CuttingParameters;
            if (parameters == null)
                return;

            // Remove any existing lead-ins first
            if (selectedPart.HasManualLeadIns)
                selectedPart.RemoveLeadIns();

            // Apply lead-ins using the snap point as the approach point.
            // snapPoint is in the program's local coordinate space (rotated, not offset),
            // which is what Part.ApplyLeadIns expects.
            selectedPart.ApplyLeadIns(parameters, snapPoint);
            selectedPart.LeadInsLocked = true;

            // Rebuild the layout part's graphics
            selectedLayoutPart.IsDirty = true;
            selectedLayoutPart.Update();

            // Deselect and reset
            DeselectPart();
            plateView.Invalidate();
        }

        private void DeselectPart()
        {
            if (selectedLayoutPart != null)
            {
                selectedLayoutPart.IsSelected = false;
                selectedLayoutPart = null;
            }

            selectedPart = null;
            profile = null;
            contours = null;
            hasSnap = false;
            plateView.Invalidate();
        }

        private void ShowContextMenu(Point location)
        {
            contextMenu?.Dispose();
            contextMenu = new ContextMenuStrip();

            var removeItem = new ToolStripMenuItem("Remove All Lead-ins");
            removeItem.Click += (s, e) =>
            {
                selectedPart.RemoveLeadIns();
                selectedLayoutPart.IsDirty = true;
                selectedLayoutPart.Update();
                DeselectPart();
                plateView.Invalidate();
            };

            contextMenu.Items.Add(removeItem);
            contextMenu.Show(plateView, location);
        }

        private Vector TransformToWorld(Vector localPt)
        {
            // The contours are already in rotated local space (we rotated the program
            // before building the profile), so just add the part location offset
            return new Vector(localPt.X + selectedPart.Location.X,
                              localPt.Y + selectedPart.Location.Y);
        }

        private static LeadIn SelectLeadIn(CuttingParameters parameters, ContourType contourType)
        {
            return contourType switch
            {
                ContourType.ArcCircle => parameters.ArcCircleLeadIn ?? parameters.InternalLeadIn,
                ContourType.Internal => parameters.InternalLeadIn,
                _ => parameters.ExternalLeadIn
            };
        }

        private class ShapeInfo
        {
            public Shape Shape { get; set; }
            public ContourType ContourType { get; set; }
        }
    }
}
