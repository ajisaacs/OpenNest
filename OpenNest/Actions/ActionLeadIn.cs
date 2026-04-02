using OpenNest.CNC.CuttingStrategy;
using OpenNest.Controls;
using OpenNest.Converters;
using OpenNest.Forms;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace OpenNest.Actions
{
    [DisplayName("Place Lead-in")]
    public class ActionLeadIn : Action
    {
        private enum SnapType { None, Endpoint, Midpoint }

        private const double SnapCapturePixels = 10.0;

        private LayoutPart selectedLayoutPart;
        private Part selectedPart;
        private ShapeProfile profile;
        private List<ShapeInfo> contours;
        private Vector snapPoint;
        private Entity snapEntity;
        private ContourType snapContourType;
        private double snapNormal;
        private bool hasSnap;
        private SnapType activeSnapType;
        private ShapeInfo hoveredContour;
        private ContextMenuStrip contextMenu;
        private LeadInToolWindow toolWindow;
        private static readonly Brush grayOverlay = new SolidBrush(Color.FromArgb(160, 180, 180, 180));
        private static readonly Pen highlightPen = new Pen(Color.Cyan, 2.5f);

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
            ShowToolWindow();
        }

        public override void DisconnectEvents()
        {
            plateView.MouseMove -= OnMouseMove;
            plateView.MouseDown -= OnMouseDown;
            plateView.KeyDown -= OnKeyDown;
            plateView.Paint -= OnPaint;

            HideToolWindow();

            contextMenu?.Dispose();
            contextMenu = null;

            selectedLayoutPart = null;
            selectedPart = null;
            profile = null;
            contours = null;
            hasSnap = false;
            activeSnapType = SnapType.None;
            hoveredContour = null;
            plateView.Invalidate();
        }

        public override void CancelAction() { }

        public override bool IsBusy() => selectedPart != null;

        private void ShowToolWindow()
        {
            if (toolWindow == null)
            {
                toolWindow = new LeadInToolWindow();
                toolWindow.AutoAssignClicked += OnAutoAssignClicked;
            }

            // Load current parameters or defaults
            var plate = plateView.Plate;
            if (plate?.CuttingParameters != null)
                toolWindow.LoadFromParameters(plate.CuttingParameters);
            else
            {
                var json = Properties.Settings.Default.CuttingParametersJson;
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var saved = CuttingParametersSerializer.Deserialize(json);
                        toolWindow.LoadFromParameters(saved);
                    }
                    catch { /* use defaults */ }
                }
            }

            toolWindow.ParametersChanged += OnToolParametersChanged;

            var mainForm = plateView.FindForm();
            if (mainForm != null)
                toolWindow.Owner = mainForm;

            toolWindow.Show();
        }

        private void HideToolWindow()
        {
            if (toolWindow == null)
                return;

            SaveParameters();

            toolWindow.ParametersChanged -= OnToolParametersChanged;
            toolWindow.AutoAssignClicked -= OnAutoAssignClicked;
            toolWindow.Close();
            toolWindow.Dispose();
            toolWindow = null;
        }

        private CuttingParameters GetCurrentParameters()
        {
            return toolWindow?.BuildParameters() ?? plateView.Plate?.CuttingParameters ?? new CuttingParameters();
        }

        private void SaveParameters()
        {
            if (toolWindow == null)
                return;

            var parameters = toolWindow.BuildParameters();
            var json = CuttingParametersSerializer.Serialize(parameters);
            Properties.Settings.Default.CuttingParametersJson = json;
            Properties.Settings.Default.Save();

            if (plateView.Plate != null)
                plateView.Plate.CuttingParameters = parameters;
        }

        private void OnToolParametersChanged(object sender, System.EventArgs e)
        {
            plateView.Invalidate();
        }

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
            activeSnapType = SnapType.None;
            hoveredContour = null;

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
                    snapNormal = ContourCuttingStrategy.ComputeNormal(closest, entity, info.ContourType, info.Winding);
                    hasSnap = true;
                    hoveredContour = info;
                }
            }

            // Check endpoint/midpoint snaps on the hovered contour
            if (hoveredContour != null)
            {
                TrySnapToEntityPoints(localPt);

                // Auto-switch tool window tab
                if (toolWindow != null)
                    toolWindow.ActiveContourType = snapContourType;
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
            var g = e.Graphics;

            DrawOverlay(g);
            DrawHoveredContour(g);
            DrawLeadInPreview(g);
        }

        private void DrawOverlay(Graphics g)
        {
            foreach (var lp in plateView.LayoutParts)
            {
                if (lp != selectedLayoutPart && lp.Path != null)
                    g.FillPath(grayOverlay, lp.Path);
            }
        }

        private void DrawHoveredContour(Graphics g)
        {
            if (hoveredContour == null || selectedPart == null)
                return;

            using var contourPath = hoveredContour.Shape.GetGraphicsPath();
            using var contourMatrix = new Matrix();
            contourMatrix.Translate((float)selectedPart.Location.X, (float)selectedPart.Location.Y);
            contourMatrix.Multiply(plateView.Matrix, MatrixOrder.Append);
            contourPath.Transform(contourMatrix);

            var prevSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawPath(highlightPen, contourPath);
            g.SmoothingMode = prevSmooth;
        }

        private void DrawLeadInPreview(Graphics g)
        {
            if (!hasSnap || selectedPart == null)
                return;

            var parameters = GetCurrentParameters();

            var leadIn = SelectLeadIn(parameters, snapContourType);
            if (leadIn == null)
                return;

            leadIn = ClampLeadInForCircle(leadIn, parameters);

            var piercePoint = leadIn.GetPiercePoint(snapPoint, snapNormal);
            var pt1 = plateView.PointWorldToGraph(TransformToWorld(piercePoint));
            var pt2 = plateView.PointWorldToGraph(TransformToWorld(snapPoint));

            var oldSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var previewPen = new Pen(Color.Magenta, 2.0f / plateView.ViewScale);
            g.DrawLine(previewPen, pt1, pt2);

            var radius = 3.0f / plateView.ViewScale;
            g.FillEllipse(Brushes.Magenta, pt1.X - radius, pt1.Y - radius, radius * 2, radius * 2);

            if (activeSnapType != SnapType.None)
                DrawSnapMarker(g, pt2, activeSnapType);
            else
                g.FillEllipse(Brushes.Lime, pt2.X - radius, pt2.Y - radius, radius * 2, radius * 2);

            g.SmoothingMode = oldSmooth;
        }

        private LeadIn ClampLeadInForCircle(LeadIn leadIn, CuttingParameters parameters)
        {
            if (snapContourType != ContourType.ArcCircle
                || !(snapEntity is Circle snapCircle)
                || parameters.PierceClearance <= 0)
                return leadIn;

            var pierceCheck = leadIn.GetPiercePoint(snapPoint, snapNormal);
            var maxRadius = snapCircle.Radius - parameters.PierceClearance;

            if (maxRadius <= 0 || pierceCheck.DistanceTo(snapCircle.Center) <= maxRadius)
                return leadIn;

            var currentDist = snapPoint.DistanceTo(pierceCheck);
            if (currentDist <= Tolerance.Epsilon)
                return leadIn;

            var dx = (pierceCheck.X - snapPoint.X) / currentDist;
            var dy = (pierceCheck.Y - snapPoint.Y) / currentDist;
            var vx = snapPoint.X - snapCircle.Center.X;
            var vy = snapPoint.Y - snapCircle.Center.Y;
            var b = 2.0 * (vx * dx + vy * dy);
            var c = vx * vx + vy * vy - maxRadius * maxRadius;
            var disc = b * b - 4.0 * c;

            if (disc < 0)
                return leadIn;

            var t = (-b + System.Math.Sqrt(disc)) / 2.0;
            return (t > 0 && t < currentDist) ? leadIn.Scale(t / currentDist) : leadIn;
        }

        private void TrySnapToEntityPoints(Vector localPt)
        {
            var captureRadius = SnapCapturePixels / plateView.ViewScale;
            var bestDist = captureRadius;
            var bestPoint = default(Vector);
            var bestEntity = default(Entity);
            var bestType = SnapType.None;

            foreach (var entity in hoveredContour.Shape.Entities)
            {
                switch (entity)
                {
                    case Line line:
                        TryCandidate(line.StartPoint, line, SnapType.Endpoint);
                        TryCandidate(line.EndPoint, line, SnapType.Endpoint);
                        TryCandidate(line.MidPoint, line, SnapType.Midpoint);
                        break;
                    case Arc arc:
                        TryCandidate(arc.StartPoint(), arc, SnapType.Endpoint);
                        TryCandidate(arc.EndPoint(), arc, SnapType.Endpoint);
                        TryCandidate(arc.MidPoint(), arc, SnapType.Midpoint);
                        break;
                }
            }

            if (bestType != SnapType.None)
            {
                snapPoint = bestPoint;
                snapEntity = bestEntity;
                snapNormal = ContourCuttingStrategy.ComputeNormal(bestPoint, bestEntity, snapContourType, hoveredContour.Winding);
                activeSnapType = bestType;
            }

            return;

            void TryCandidate(Vector pt, Entity ent, SnapType type)
            {
                var dist = pt.DistanceTo(localPt);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPoint = pt;
                    bestEntity = ent;
                    bestType = type;
                }
            }
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

            var entities = ConvertProgram.ToGeometry(cleanProgram)
                .Where(e => e.Layer == SpecialLayers.Cut)
                .ToList();

            profile = new ShapeProfile(entities);

            contours = new List<ShapeInfo>();

            // Perimeter is always External
            if (profile.Perimeter != null)
            {
                contours.Add(new ShapeInfo
                {
                    Shape = profile.Perimeter,
                    ContourType = ContourType.External,
                    Winding = ContourCuttingStrategy.DetermineWinding(profile.Perimeter)
                });
            }

            // Cutouts
            foreach (var cutout in profile.Cutouts)
            {
                contours.Add(new ShapeInfo
                {
                    Shape = cutout,
                    ContourType = ContourCuttingStrategy.DetectContourType(cutout),
                    Winding = ContourCuttingStrategy.DetermineWinding(cutout)
                });
            }
        }

        private void CommitLeadIn()
        {
            var parameters = GetCurrentParameters();

            if (selectedPart.HasManualLeadIns)
                selectedPart.RemoveLeadIns();

            ApplyAutoTab(parameters);

            selectedPart.ApplySingleLeadIn(parameters, snapPoint, snapEntity, snapContourType);
            selectedPart.LeadInsLocked = true;

            selectedLayoutPart.IsDirty = true;
            DeselectPart();
            plateView.Invalidate();
        }

        private void OnAutoAssignClicked(object sender, System.EventArgs e)
        {
            if (selectedPart == null)
                return;

            var parameters = GetCurrentParameters();

            if (selectedPart.HasManualLeadIns)
                selectedPart.RemoveLeadIns();

            ApplyAutoTab(parameters);

            selectedPart.ApplyLeadIns(parameters, Vector.Zero);
            selectedPart.LeadInsLocked = true;

            selectedLayoutPart.IsDirty = true;
            DeselectPart();
            plateView.Invalidate();
        }

        private void ApplyAutoTab(CuttingParameters parameters)
        {
            if (parameters.AutoTabMinSize <= 0 && parameters.AutoTabMaxSize <= 0)
                return;

            var bbox = selectedPart.Program.BoundingBox();
            var minDim = System.Math.Min(bbox.Width, bbox.Length);

            if (minDim >= parameters.AutoTabMinSize && minDim <= parameters.AutoTabMaxSize)
                parameters.TabsEnabled = true;
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
            activeSnapType = SnapType.None;
            hoveredContour = null;
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

        private void DrawSnapMarker(Graphics g, PointF pt, SnapType type)
        {
            var size = 5f;

            if (type == SnapType.Endpoint)
            {
                // Diamond
                var points = new[]
                {
                    new PointF(pt.X, pt.Y - size),
                    new PointF(pt.X + size, pt.Y),
                    new PointF(pt.X, pt.Y + size),
                    new PointF(pt.X - size, pt.Y)
                };
                g.FillPolygon(Brushes.Red, points);
            }
            else if (type == SnapType.Midpoint)
            {
                // Triangle
                var points = new[]
                {
                    new PointF(pt.X, pt.Y - size),
                    new PointF(pt.X + size, pt.Y + size),
                    new PointF(pt.X - size, pt.Y + size)
                };
                g.FillPolygon(Brushes.Red, points);
            }
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
            public RotationType Winding { get; set; }
        }
    }
}
