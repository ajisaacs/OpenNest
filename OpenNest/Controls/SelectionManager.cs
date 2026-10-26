using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

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
            using var change = view.BeginChange("Delete");
            var regenerateCutOffs = view.Plate.CutOffs.Count > 0
                && (selectedCutOffs.Count > 0 || selectedParts.Count > 0);
            if (selectedCutOffs.Count > 0)
                change.MarkChanged();
            foreach (var cutOff in selectedCutOffs)
                view.Plate.CutOffs.Remove(cutOff);

            // Finish removing both kinds before rebuilding surviving cut strokes;
            // otherwise they retain gaps around parts deleted in the same window.
            RemoveSelectedParts();
            if (regenerateCutOffs)
                view.Plate.RegenerateCutOffs(view.CutOffSettings);
            change.Complete();
        }

        public void RemoveSelectedParts()
        {
            using var change = view.BeginChange("Delete");
            if (selectedParts.Count > 0)
                change.MarkChanged();
            foreach (var part in selectedParts)
                view.Plate.Parts.Remove(part.BasePart);

            DeselectAll();
            view.Invalidate();
            change.Complete();
        }

        /// <summary>
        /// Moves the selection by (<paramref name="dx"/>, <paramref name="dy"/>); repeated
        /// nudges of the same selection undo as one move.
        /// </summary>
        public void NudgeSelected(double dx, double dy)
        {
            if (selectedParts.Count == 0)
                return;

            using var change = view.BeginChange("Nudge", new SelectionKey("Nudge", view.Plate, selectedParts));
            change.MarkChanged();
            selectedParts.ForEach(part => part.Offset(dx, dy));
            view.Invalidate();
            change.Complete();
        }

        public void AlignSelected(AlignType alignType)
        {
            if (selectedParts.Count == 0)
                return;

            AlignSelected(alignType, selectedParts[0]);
        }

        public void AlignSelected(AlignType alignType, LayoutPart fixedPart)
        {
            if (selectedParts.Count == 0)
                return;

            using var change = view.BeginChange("Align");
            change.MarkChanged();
            switch (alignType)
            {
                case AlignType.Bottom:
                    Align.Bottom(
                        fixedPart.BasePart,
                        selectedParts.Select(p => p.BasePart).ToList()
                    );
                    break;
                case AlignType.Horizontally:
                    Align.Horizontally(
                        fixedPart.BasePart,
                        selectedParts.Select(p => p.BasePart).ToList()
                    );
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
                    Align.Vertically(
                        fixedPart.BasePart,
                        selectedParts.Select(p => p.BasePart).ToList()
                    );
                    break;
                case AlignType.EvenlySpaceHorizontally:
                    Align.EvenlyDistributeHorizontally(
                        selectedParts.Select(p => p.BasePart).ToList()
                    );
                    break;
                case AlignType.EvenlySpaceVertically:
                    Align.EvenlyDistributeVertically(
                        selectedParts.Select(p => p.BasePart).ToList()
                    );
                    break;
                default:
                    // An unknown alignment moves nothing; completing records nothing either.
                    change.Complete();
                    return;
            }

            selectedParts.ForEach(p => p.IsDirty = true);
            view.Invalidate();
            change.Complete();
        }

        /// <summary>
        /// Rotates the selection by <paramref name="angle"/> about its centre, keeping its
        /// lower-left corner; with <paramref name="coalesce"/>, quick repeats undo as one.
        /// </summary>
        public void RotateSelectedParts(double angle, bool coalesce = false)
        {
            if (selectedParts.Count == 0)
                return;

            using var change = view.BeginChange("Rotate",
                coalesce ? new SelectionKey("Rotate", view.Plate, selectedParts) : null);
            change.MarkChanged();
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
            change.Complete();
        }

        public void PushSelected(PushDirection direction)
        {
            if (selectedParts.Count == 0)
                return;

            using var change = view.BeginChange("Push");
            change.MarkChanged();
            var movingParts = selectedParts.Select(p => p.BasePart).ToList();
            Compactor.Push(movingParts, view.Plate, direction);
            selectedParts.ForEach(p => p.IsDirty = true);
            view.Invalidate();
            change.Complete();
        }

        /// <summary>
        /// Grows the part-to-part spacing of the selection as far as the plate
        /// allows (Expander). <paramref name="run"/> executes the search and may
        /// cancel it through its token (the desktop shows a Cancel window); by
        /// default it runs synchronously. A cancelled run moves no part. Returns
        /// false only when fewer than two parts are selected.
        /// </summary>
        public bool ExpandSelected(
            Func<Func<CancellationToken, Expander.Result>, Expander.Result> run = null
        )
        {
            var movingParts = selectedParts.Select(p => p.BasePart).ToList();

            if (movingParts.Count < 2)
                return false;

            var plate = view.Plate;
            Expander.Result Work(CancellationToken token) =>
                Expander.Expand(movingParts, plate, null, token);

            using var change = view.BeginChange("Expand Spacing");
            var result = run != null ? run(Work) : Work(CancellationToken.None);

            if (result.Cancelled)
            {
                view.Status = "Expand Spacing cancelled - no parts were moved";
                change.Complete();
                return true;
            }

            change.MarkChanged();
            selectedParts.ForEach(p => p.IsDirty = true);

            if (view.Plate.CutOffs.Count > 0)
                view.Plate.RegenerateCutOffs(view.CutOffSettings);

            view.Invalidate();

            view.Status =
                $"Expanded spacing to {result.AchievedSpacing:0.###}"
                + (result.Violations.Count > 0 ? $" - {result.Violations.Count} blocked pairs" : "");

            change.Complete();
            return true;
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
                if (parts[i].SelectionPath?.IsVisible(pt) == true)
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
                    var path = part.SelectionPath;
                    if (path == null)
                        continue;
                    using var region = new Region(path);
                    if (region.IsVisible(rect))
                        list.Add(part);
                }
            }
            else
            {
                for (var i = 0; i < parts.Count; ++i)
                {
                    var part = parts[i];
                    var path = part.SelectionPath;
                    if (path != null && rect.Contains(path.GetBounds()))
                        list.Add(part);
                }
            }

            return list;
        }

        public IList<CutOff> GetCutOffsFromWindow(RectangleF rect, SelectionType selectionType)
        {
            var result = new List<CutOff>();
            foreach (var cutOff in view.Plate.CutOffs)
            {
                var program = cutOff.Drawing?.Program;
                if (program == null)
                    continue;

                var hasSegment = false;
                var matches = selectionType == SelectionType.Contains;
                // Match the actual cut strokes drawn by PlateRenderer, not the nominal
                // full-length line: a gap around a part is not selectable material.
                for (var i = 0; i < program.Codes.Count - 1; i += 2)
                {
                    if (program.Codes[i] is not RapidMove rapid
                        || program.Codes[i + 1] is not LinearMove linear)
                        continue;

                    hasSegment = true;
                    var start = view.PointWorldToGraph(rapid.EndPoint);
                    var end = view.PointWorldToGraph(linear.EndPoint);
                    var hit = selectionType == SelectionType.Contains
                        ? ContainsInclusive(rect, start) && ContainsInclusive(rect, end)
                        : SegmentIntersects(rect, start, end);
                    if (selectionType == SelectionType.Intersect && hit)
                    {
                        matches = true;
                        break;
                    }
                    if (selectionType == SelectionType.Contains && !hit)
                    {
                        matches = false;
                        break;
                    }
                }

                if (hasSegment && matches)
                    result.Add(cutOff);
            }
            return result;
        }

        private static bool ContainsInclusive(RectangleF rect, PointF point) =>
            point.X >= rect.Left && point.X <= rect.Right
            && point.Y >= rect.Top && point.Y <= rect.Bottom;

        private static bool SegmentIntersects(RectangleF rect, PointF start, PointF end)
        {
            if (ContainsInclusive(rect, start) || ContainsInclusive(rect, end))
                return true;

            // Clip the segment against all four window edges. Endpoints alone miss
            // a stroke that enters and exits the window without ending inside it.
            var min = 0.0;
            var max = 1.0;
            var dx = (double)end.X - start.X;
            var dy = (double)end.Y - start.Y;
            return Clip(-dx, start.X - rect.Left)
                && Clip(dx, rect.Right - start.X)
                && Clip(-dy, start.Y - rect.Top)
                && Clip(dy, rect.Bottom - start.Y);

            bool Clip(double p, double q)
            {
                if (p == 0)
                    return q >= 0;
                var t = q / p;
                if (p < 0)
                {
                    if (t > max)
                        return false;
                    min = System.Math.Max(min, t);
                }
                else
                {
                    if (t < min)
                        return false;
                    max = System.Math.Min(max, t);
                }
                return min <= max;
            }
        }

        public void Clear()
        {
            selectedParts.Clear();
            selectedCutOffs.Clear();
        }

        /// <summary>
        /// Equal for the same edit of the same parts on the same plate, so repeated nudges or
        /// wheel turns coalesce into one undo entry.
        /// </summary>
        private sealed class SelectionKey : IEquatable<SelectionKey>
        {
            private readonly string operation;
            private readonly Plate plate;
            private readonly Part[] parts;

            public SelectionKey(string operation, Plate plate, IEnumerable<LayoutPart> selection)
            {
                this.operation = operation;
                this.plate = plate;
                parts = selection.Select(p => p.BasePart).ToArray();
            }

            public bool Equals(SelectionKey other) =>
                other != null
                && operation == other.operation
                && ReferenceEquals(plate, other.plate)
                && parts.Length == other.parts.Length
                && parts.Zip(other.parts).All(pair => ReferenceEquals(pair.First, pair.Second));

            public override bool Equals(object obj) => Equals(obj as SelectionKey);

            public override int GetHashCode() => HashCode.Combine(operation, RuntimeHelpers.GetHashCode(plate), parts.Length);
        }
    }
}
