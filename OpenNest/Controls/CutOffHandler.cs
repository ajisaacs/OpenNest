using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using OpenNest.Undo;

namespace OpenNest.Controls
{
    internal class CutOffHandler
    {
        private readonly PlateView view;
        private Dictionary<Part, Geometry.Entity> dragPerimeterCache;

        // Open from the press on a cut-off to the release: a drag undoes as one move.
        private ChangeScope drag;

        public CutOffHandler(PlateView view)
        {
            this.view = view;
        }

        public bool IsDragging { get; private set; }

        /// <summary>Whether Sheet Cut-Off draws full-length lines instead of stopping at the first crossing.</summary>
        public bool FullLengthNewLines { get; set; }

        public CutOff TryStartDrag(Vector point, double tolerance)
        {
            var hitCutOff = GetCutOffAtPoint(point, tolerance);
            if (hitCutOff == null)
                return null;

            IsDragging = true;
            dragPerimeterCache = Plate.BuildPerimeterCache(view.Plate);
            drag = view.BeginChange("Move Cut-Off");
            return hitCutOff;
        }

        public void UpdateDrag(Vector currentPoint, CutOff cutOff)
        {
            if (!IsDragging || cutOff == null)
                return;

            drag?.MarkChanged();
            if (cutOff.Axis == CutOffAxis.Vertical)
                cutOff.Position = new Vector(currentPoint.X, cutOff.Position.Y);
            else
                cutOff.Position = new Vector(cutOff.Position.X, currentPoint.Y);

            cutOff.Regenerate(view.Plate, view.CutOffSettings, dragPerimeterCache);
            view.Invalidate();
        }

        public void EndDrag()
        {
            if (!IsDragging)
                return;

            IsDragging = false;
            dragPerimeterCache = null;
            view.Plate.RegenerateCutOffs(view.CutOffSettings);
            view.Invalidate();

            var scope = drag;
            drag = null;
            scope?.Complete();
            scope?.Dispose();
        }

        /// <summary>
        /// Applies a trim, extend or swap as one undoable change: the new limits, regenerated
        /// cut-offs, and the changed cut-offs' segments placed where the cut-off rules need them.
        /// False, changing nothing, when the edit was refused.
        /// </summary>
        public bool Apply(CutOffEdit edit, string label)
        {
            if (edit == null || !edit.Succeeded)
                return false;

            using var change = view.BeginChange(label);
            change.MarkChanged();
            edit.Apply();
            view.Plate.RegenerateCutOffs(view.CutOffSettings);
            PlateSequencing.PlaceCutOffs(view.Plate, edit.Changes.Select(c => c.CutOff));
            view.Invalidate();
            change.Complete();
            return true;
        }

        public CutOff GetCutOffAtPoint(Vector point, double tolerance)
        {
            if (view.Plate?.CutOffs == null)
                return null;

            foreach (var cutoff in view.Plate.CutOffs)
            {
                var program = cutoff.Drawing?.Program;
                if (program == null)
                    continue;

                for (var i = 0; i < program.Codes.Count - 1; i += 2)
                {
                    if (
                        program.Codes[i] is RapidMove rapid
                        && program.Codes[i + 1] is LinearMove linear
                    )
                    {
                        var line = new Line(rapid.EndPoint, linear.EndPoint);
                        if (line.ClosestPointTo(point).DistanceTo(point) <= tolerance)
                            return cutoff;
                    }
                }
            }

            return null;
        }
    }
}
