using System.Collections.Generic;
using System.Drawing;

namespace OpenNest.Controls
{
    internal class PreviewManager
    {
        private readonly PlateView view;
        private readonly List<LayoutPart> stationaryParts = new List<LayoutPart>();
        private readonly List<LayoutPart> activeParts = new List<LayoutPart>();

        public PreviewManager(PlateView view)
        {
            this.view = view;
        }

        public IReadOnlyList<LayoutPart> PreviewParts =>
            activeParts.Count > 0 ? activeParts : stationaryParts;

        public Brush PreviewBrush =>
            activeParts.Count > 0 ? view.ColorScheme.ActivePreviewPartBrush : view.ColorScheme.PreviewPartBrush;

        public Pen PreviewPen =>
            activeParts.Count > 0 ? view.ColorScheme.ActivePreviewPartPen : view.ColorScheme.PreviewPartPen;

        public void SetStationaryParts(List<Part> parts)
        {
            stationaryParts.Clear();
            activeParts.Clear();

            if (parts != null)
            {
                foreach (var part in parts)
                    stationaryParts.Add(LayoutPart.Create(part, view));
            }

            view.Invalidate();
        }

        public void SetActiveParts(List<Part> parts)
        {
            activeParts.Clear();

            if (parts != null)
            {
                foreach (var part in parts)
                    activeParts.Add(LayoutPart.Create(part, view));
            }

            view.Invalidate();
        }

        public void ClearPreviewParts()
        {
            stationaryParts.Clear();
            activeParts.Clear();
            view.Invalidate();
        }

        public void AcceptPreviewParts(List<Part> parts)
        {
            if (parts != null)
            {
                foreach (var part in parts)
                    view.Plate.Parts.Add(part);
            }

            stationaryParts.Clear();
            activeParts.Clear();
        }

        public void Update()
        {
            stationaryParts.ForEach(p => p.Update(view));
            activeParts.ForEach(p => p.Update(view));
        }

        public void Clear()
        {
            stationaryParts.Clear();
            activeParts.Clear();
        }
    }
}
