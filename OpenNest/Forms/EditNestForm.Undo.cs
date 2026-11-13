using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.CutOffs;
using OpenNest.Data;
using OpenNest.Undo;

namespace OpenNest.Forms
{
    // Undo and Redo: the history holds whole-nest copies and a restore swaps one in. Recording,
    // the fingerprint check and stepping are the shared NestUndoRecorder; this window rebinds.
    public partial class EditNestForm
    {
        private NestUndoRecorder undo;

        /// <summary>Raised after <see cref="RestoreNest"/> swapped a nest in, for windows that show it.</summary>
        public event EventHandler NestRestored;

        public UndoHistory<NestUndoState> History => Document.History;

        /// <summary>The shared recorder of this window's nest edits.</summary>
        internal NestUndoRecorder UndoRecorder => undo ??= CreateRecorder();

        /// <summary>True while a change scope is open or a nest is being swapped in.</summary>
        public bool IsChangeInProgress => UndoRecorder.IsChangeInProgress;

        /// <summary>
        /// Opens a scope that records one undoable edit named <paramref name="label"/>. Mark it
        /// changed and complete it; a scope opened inside another merges into the outermost one.
        /// Repeated edits with an equal <paramref name="coalesceKey"/> within a second undo as one.
        /// </summary>
        public ChangeScope BeginChange(string label, object coalesceKey = null) =>
            UndoRecorder.BeginChange(label, coalesceKey);

        /// <summary>Runs <paramref name="edit"/>, which changes the nest, as one undoable step.</summary>
        public void RecordChange(string label, System.Action edit) => UndoRecorder.RecordChange(label, edit);

        /// <summary>Whether <see cref="Undo"/> would step back: no edit or inspection is open.</summary>
        public bool CanUndo => UndoRecorder.CanUndo && !IsInspecting;

        public bool CanRedo => UndoRecorder.CanRedo && !IsInspecting;

        public void Undo()
        {
            if (!IsInspecting)
                UndoRecorder.Undo();
        }

        public void Redo()
        {
            if (!IsInspecting)
                UndoRecorder.Redo();
        }

        /// <summary>
        /// True when the nest differs from the state last saved (or opened): the history has
        /// moved off its save point, or an edit no scope recorded is pending.
        /// </summary>
        public bool HasUnsavedChanges => UndoRecorder.HasUnsavedChanges;

        /// <summary>
        /// Marks the nest as it stands saved, for a nest something else wrote to the server
        /// (Make Nests saved it from this tab).
        /// </summary>
        internal void MarkSaved()
        {
            UndoRecorder.MarkSaved();
            UpdateTitle();
        }

        /// <summary>Shows the nest's name, marked with an asterisk while it has unsaved changes.</summary>
        internal void UpdateTitle() => Text = HasUnsavedChanges ? $"{Nest.Name} *" : Nest.Name;

        private NestUndoRecorder CreateRecorder()
        {
            var recorder = new NestUndoRecorder(Document) { SwapIn = SwapInRestoredNest };
            recorder.Changed += (_, _) => UpdateTitle();
            recorder.Restored += (_, _) => NestRestored?.Invoke(this, EventArgs.Empty);
            return recorder;
        }

        private void InitializeHistory()
        {
            UndoRecorder.Reset();
#if DEBUG
            Application.Idle += CheckForUnrecordedChange;
            FormClosed += (_, _) => Application.Idle -= CheckForUnrecordedChange;
#endif
        }

        /// <summary>
        /// Commits an edit no scope recorded as its own entry, so a rollback, Undo or Redo
        /// does not silently discard it, and a save marks it saved. Detected by fingerprint, so
        /// best effort.
        /// </summary>
        internal void RecordUnrecordedChange() => UndoRecorder.RecordUnrecordedChange();

        /// <summary>
        /// Replaces the document's nest with <paramref name="restored"/>, a copy read back from
        /// the history, and rebinds everything that held the old one. The document, the nest's
        /// identity (name, status, author, creation date), the plate shown, the viewport and the
        /// selection (by part index) are kept.
        /// </summary>
        public void RestoreNest(Nest restored) => UndoRecorder.RestoreNest(restored);

        // The recorder's swap-in, run with the identity already carried over and nothing recorded.
        private void SwapInRestoredNest(Nest restored)
        {
            // Apply after a restore would commit to plates no longer in the nest.
            CuttingInspection?.RequestClose();
            if (CuttingInspection != null)
                throw new InvalidOperationException("Close Cutting Inspection before undoing.");

            var plateIndex = PlateManager.CurrentIndex;
            var partCount = PlateView.Plate?.Parts.Count ?? 0;
            var selected = PlateView.SelectedParts
                .Select(layout => PlateView.Plate.Parts.IndexOf(layout.BasePart))
                .Where(index => index >= 0)
                .ToArray();
            var viewport = PlateView.CaptureViewport();

            // Actions, previews and side panels hold parts and plates of the old nest.
            PlateView.EndAction();
            PlateView.ClearPreviewParts();
            PlateView.CancelOverlapCheck();
            drawingListBox1.CancelPickup();
            addPart = false;
            if (IsSidePanelVisible)
                HideSidePanel();

            UnbindNest();
            Document.Nest = restored;
            BindNest(restored);
            // Reading regenerates cut-offs with default settings; the view's apply here.
            // Regenerating empties a cut-off-only plate for a moment, so the batch keeps
            // the new-plate sentinel from adding or dropping plates until all are done.
            PlateManager.BeginBatch();
            try
            {
                foreach (var plate in restored.Plates.Where(plate => plate.CutOffs.Count > 0).ToArray())
                    plate.RegenerateCutOffs(PlateView.CutOffSettings);
            }
            finally
            {
                PlateManager.EndBatch();
            }
            ColorSchemeRegistry.RecolorDrawings(restored);

            // Loading a plate zooms to fit; an undo keeps the view where it was.
            PlateManager.LoadAt(System.Math.Clamp(plateIndex, 0, System.Math.Max(0, PlateManager.Count - 1)));
            PlateView.RestoreViewport(viewport);
            if (PlateView.Plate.Parts.Count == partCount && selected.Length > 0)
                Reselect(selected);

            UpdatePlateList();
            UpdatePlateHeader();
            UpdateRemovePlateButton();
            PlateView.Invalidate();
        }

        private void Reselect(int[] indices)
        {
            var layouts = PlateView.LayoutParts;
            foreach (var index in indices)
            {
                var layout = layouts[index];
                layout.IsSelected = true;
                PlateView.SelectedParts.Add(layout);
            }
            PlateView.NotifySelectionChanged();
        }

#if DEBUG
        private DateTime nextUnrecordedCheck;

        // Every edit should open a change scope; one that does not folds into the next entry.
        private void CheckForUnrecordedChange(object sender, EventArgs e)
        {
            if (IsDisposed || IsChangeInProgress || PlateView.IsFillInProgress || DateTime.UtcNow < nextUnrecordedCheck)
                return;
            nextUnrecordedCheck = DateTime.UtcNow.AddMilliseconds(500);
            if (UndoRecorder.TakeUnrecordedChangeWarning())
                Debug.WriteLine($"Undo: '{Nest.Name}' changed outside a change scope; the edit is not recorded yet.");
        }
#endif
    }
}
