using System.Collections.Generic;
using System.Drawing;

namespace OpenNest.Controls
{
    internal class PreviewManager
    {
        private readonly PlateView view;
        private readonly List<LayoutPart> activeParts = new List<LayoutPart>();

        public PreviewManager(PlateView view)
        {
            this.view = view;
        }

        public IReadOnlyList<LayoutPart> PreviewParts => activeParts;

        public Brush PreviewBrush => view.ColorScheme.ActivePreviewPartBrush;

        public Pen PreviewPen => view.ColorScheme.ActivePreviewPartPen;

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

            activeParts.Clear();
        }

        public void Update()
        {
            activeParts.ForEach(p => p.Update(view));
        }

        public void Clear()
        {
            activeParts.Clear();
        }
    }
}
