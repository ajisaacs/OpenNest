using OpenNest.CNC;
using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Controls
{
    internal class CutOffHandler
    {
        private readonly PlateView view;
        private Dictionary<Part, Geometry.Entity> dragPerimeterCache;

        public CutOffHandler(PlateView view)
        {
            this.view = view;
        }

        public bool IsDragging { get; private set; }

        public CutOff TryStartDrag(Vector point, double tolerance)
        {
            var hitCutOff = GetCutOffAtPoint(point, tolerance);
            if (hitCutOff == null)
                return null;

            IsDragging = true;
            dragPerimeterCache = Plate.BuildPerimeterCache(view.Plate);
            return hitCutOff;
        }

        public void UpdateDrag(Vector currentPoint, CutOff cutOff)
        {
            if (!IsDragging || cutOff == null)
                return;

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
                    if (program.Codes[i] is RapidMove rapid &&
                        program.Codes[i + 1] is LinearMove linear)
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
