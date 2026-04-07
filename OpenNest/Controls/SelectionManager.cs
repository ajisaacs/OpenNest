using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;

namespace OpenNest.Controls
{
    internal class SelectionManager
    {
        private readonly PlateView view;
        private readonly List<LayoutPart> selectedParts = new List<LayoutPart>();
        private readonly List<CutOff> selectedCutOffs = new List<CutOff>();

        public SelectionManager(PlateView view)
        {
            this.view = view;
        }

        public List<LayoutPart> SelectedParts => selectedParts;
        public List<CutOff> SelectedCutOffs => selectedCutOffs;

        public event EventHandler SelectionChanged;

        public void DeselectAll()
        {
            selectedParts.ForEach(p => p.IsSelected = false);
            selectedParts.Clear();
            selectedCutOffs.Clear();
            SelectionChanged?.Invoke(view, EventArgs.Empty);
        }

        public void DeselectParts()
        {
            selectedParts.ForEach(p => p.IsSelected = false);
            selectedParts.Clear();
            SelectionChanged?.Invoke(view, EventArgs.Empty);
        }

        public void DeselectCutOffs()
        {
            selectedCutOffs.Clear();
            view.Invalidate();
        }

        public void SelectAll()
        {
            var parts = view.LayoutParts;
            parts.ForEach(p => p.IsSelected = true);
            selectedParts.AddRange(parts);
            SelectionChanged?.Invoke(view, EventArgs.Empty);
        }

        public void NotifySelectionChanged()
        {
            SelectionChanged?.Invoke(view, EventArgs.Empty);
        }

        public void DeleteSelected()
        {
            if (selectedCutOffs.Count > 0)
            {
                foreach (var cutOff in selectedCutOffs)
                    view.Plate.CutOffs.Remove(cutOff);

                selectedCutOffs.Clear();
                view.Plate.RegenerateCutOffs(view.CutOffSettings);
                view.Invalidate();
            }
            else
            {
                RemoveSelectedParts();
            }
        }

        public void RemoveSelectedParts()
        {
            foreach (var part in selectedParts)
                view.Plate.Parts.Remove(part.BasePart);

            DeselectAll();
            view.Invalidate();
        }

        public void AlignSelected(AlignType alignType)
        {
            if (selectedParts.Count == 0)
                return;

            AlignSelected(alignType, selectedParts[0]);
        }

        public void AlignSelected(AlignType alignType, LayoutPart fixedPart)
        {
            switch (alignType)
            {
                case AlignType.Bottom:
                    Align.Bottom(fixedPart.BasePart, selectedParts.Select(p => p.BasePart).ToList());
                    break;
                case AlignType.Horizontally:
                    Align.Horizontally(fixedPart.BasePart, selectedParts.Select(p => p.BasePart).ToList());
                    break;
                case AlignType.Left:
                    Align.Left(fixedPart.BasePart, selectedParts.Select(p => p.BasePart).ToList());
                    break;
                case AlignType.Right:
                    Align.Right(fixedPart.BasePart, selectedParts.Select(p => p.BasePart).ToList());
                    break;
                case AlignType.Top:
                    Align.Top(fixedPart.BasePart, selectedParts.Select(p => p.BasePart).ToList());
                    break;
                case AlignType.Vertically:
                    Align.Vertically(fixedPart.BasePart, selectedParts.Select(p => p.BasePart).ToList());
                    break;
                case AlignType.EvenlySpaceHorizontally:
                    Align.EvenlyDistributeHorizontally(selectedParts.Select(p => p.BasePart).ToList());
                    break;
                case AlignType.EvenlySpaceVertically:
                    Align.EvenlyDistributeVertically(selectedParts.Select(p => p.BasePart).ToList());
                    break;
                default:
                    return;
            }

            selectedParts.ForEach(p => p.IsDirty = true);
            view.Invalidate();
        }

        public void RotateSelectedParts(double angle)
        {
            var parts = selectedParts.Select(p => p.BasePart).ToList();
            var bounds = parts.GetBoundingBox();
            var center = bounds.Center;
            var anchor = bounds.Location;

            for (var i = 0; i < selectedParts.Count; ++i)
                selectedParts[i].BasePart.Rotate(angle, center);

            var diff = anchor - parts.GetBoundingBox().Location;

            for (var i = 0; i < selectedParts.Count; ++i)
                selectedParts[i].Offset(diff);

            if (view.Plate.CutOffs.Count > 0)
                view.Plate.RegenerateCutOffs(view.CutOffSettings);
        }

        public void PushSelected(PushDirection direction)
        {
            var movingParts = selectedParts.Select(p => p.BasePart).ToList();
            Compactor.Push(movingParts, view.Plate, direction);
            selectedParts.ForEach(p => p.IsDirty = true);
            view.Invalidate();
        }

        public LayoutPart GetPartAtControlPoint(Point pt)
        {
            var pt2 = view.PointControlToGraph(pt);
            return GetPartAtGraphPoint(pt2);
        }

        public LayoutPart GetPartAtGraphPoint(PointF pt)
        {
            var parts = view.LayoutParts;
            for (var i = parts.Count - 1; i >= 0; --i)
            {
                if (parts[i].Path.IsVisible(pt))
                    return parts[i];
            }
            return null;
        }

        public LayoutPart GetPartAtPoint(Vector pt)
        {
            var pt2 = view.PointWorldToGraph(pt);
            return GetPartAtGraphPoint(pt2);
        }

        public IList<LayoutPart> GetPartsFromWindow(RectangleF rect, SelectionType selectionType)
        {
            var list = new List<LayoutPart>();
            var parts = view.LayoutParts;

            if (selectionType == SelectionType.Intersect)
            {
                for (var i = 0; i < parts.Count; ++i)
                {
                    var part = parts[i];
                    var region = new Region(part.Path);
                    if (region.IsVisible(rect))
                        list.Add(part);
                    region.Dispose();
                }
            }
            else
            {
                for (var i = 0; i < parts.Count; ++i)
                {
                    var part = parts[i];
                    var bounds = part.Path.GetBounds();
                    if (rect.Contains(bounds))
                        list.Add(part);
                }
            }

            return list;
        }

        public void Clear()
        {
            selectedParts.Clear();
            selectedCutOffs.Clear();
        }
    }
}
