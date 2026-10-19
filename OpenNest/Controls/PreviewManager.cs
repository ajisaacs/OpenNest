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

        /// <summary>The sheet the preview parts lie on when it is not the view's plate; null otherwise.</summary>
        public Plate Sheet { get; private set; }

        public Brush PreviewBrush => view.ColorScheme.ActivePreviewPartBrush;

        public Pen PreviewPen => view.ColorScheme.ActivePreviewPartPen;

        public void SetActiveParts(List<Part> parts, Plate sheet = null)
        {
            activeParts.Clear();
            Sheet = sheet;

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
            Sheet = null;
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
            Sheet = null;
        }

        public void Update()
        {
            activeParts.ForEach(p => p.Update(view));
        }

        public void Clear()
        {
            activeParts.Clear();
            Sheet = null;
        }
    }
}
