using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Data;
using OpenNest.IO;
using OpenNest.Undo;

namespace OpenNest.Forms
{
    // Undo and Redo: the history holds whole-nest copies and a restore swaps one in.
    public partial class EditNestForm
    {
        private ChangeScope openChange;
        private int recordedFingerprint;
        private bool restoringNest;

        /// <summary>Raised after <see cref="RestoreNest"/> swapped a nest in, for windows that show it.</summary>
        public event EventHandler NestRestored;

        public UndoHistory<NestUndoState> History => Document.History;

        /// <summary>True while a change scope is open or a nest is being swapped in.</summary>
        public bool IsChangeInProgress => openChange != null || restoringNest;

        /// <summary>
        /// Opens a scope that records one undoable edit named <paramref name="label"/>. Mark it
        /// changed and complete it; a scope opened inside another merges into the outermost one.
        /// Repeated edits with an equal <paramref name="coalesceKey"/> within a second undo as one.
        /// </summary>
        public ChangeScope BeginChange(string label, object coalesceKey = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(label);
            if (openChange != null)
                return ChangeScope.Nested(openChange);
            // Swapping a nest in rebinds the view; nothing it does is an edit.
            if (restoringNest)
                return ChangeScope.Detached();

            RecordUnrecordedChange();
            openChange = ChangeScope.Root(label, coalesceKey, EndChange);
            return openChange;
        }

        /// <summary>Runs <paramref name="edit"/>, which changes the nest, as one undoable step.</summary>
        public void RecordChange(string label, System.Action edit)
        {
            ArgumentNullException.ThrowIfNull(edit);
            using var change = BeginChange(label);
            change.MarkChanged();
            edit();
            change.Complete();
        }

        /// <summary>Whether <see cref="Undo"/> would step back: no edit or inspection is open.</summary>
        public bool CanUndo => History.CanUndo && CanRestore;

        public bool CanRedo => History.CanRedo && CanRestore;

        private bool CanRestore => !IsChangeInProgress && CuttingInspection == null;

        public void Undo()
        {
            if (!CanRestore)
                return;
            RecordUnrecordedChange();
            if (History.CanUndo)
                Restore(History.Undo(), History.Redo);
        }

        public void Redo()
        {
            if (!CanRestore)
                return;
            // An edit made since the undo drops what could be redone.
            RecordUnrecordedChange();
            if (History.CanRedo)
                Restore(History.Redo(), History.Undo);
        }

        /// <summary>
        /// Reads <paramref name="state"/> back and swaps it in; when the copy cannot be read the
        /// live nest is untouched, so <paramref name="moveBack"/> points the history back at it.
        /// </summary>
        private void Restore(NestUndoState state, Func<NestUndoState> moveBack)
        {
            Nest restored;
            try
            {
                restored = state.Snapshot.Restore(Nest);
            }
            catch
            {
                moveBack();
                throw;
            }
            RestoreNest(restored);
        }

        private void InitializeHistory()
        {
            History.Reset(new NestUndoState(NestSnapshot.Capture(Nest)));
            recordedFingerprint = NestFingerprint.Compute(Nest);
#if DEBUG
            Application.Idle += CheckForUnrecordedChange;
            FormClosed += (_, _) => Application.Idle -= CheckForUnrecordedChange;
#endif
        }

        private void EndChange(ChangeScope scope)
        {
            openChange = null;
            if (!scope.IsChanged)
                return;
            if (scope.Succeeded)
                Commit(scope.Label, scope.CoalesceKey);
            else
                RestoreNest(History.Current.Snapshot.Restore(Nest));
        }

        private void Commit(string label, object coalesceKey)
        {
            History.Commit(label, new NestUndoState(NestSnapshot.Capture(Nest)), coalesceKey);
            recordedFingerprint = NestFingerprint.Compute(Nest);
        }

        /// <summary>
        /// Commits an edit no scope recorded as its own entry, so a rollback, Undo or Redo
        /// does not silently discard it. Detected by fingerprint, so best effort.
        /// </summary>
        private void RecordUnrecordedChange()
        {
            if (History.Current != null && NestFingerprint.Compute(Nest) != recordedFingerprint)
                Commit("Change", null);
        }

        /// <summary>
        /// Replaces the document's nest with <paramref name="restored"/>, a copy read back from
        /// the history, and rebinds everything that held the old one. The document, the nest's
        /// identity (name, status, author, creation date), the plate shown, the viewport and the
        /// selection (by part index) are kept.
        /// </summary>
        public void RestoreNest(Nest restored)
        {
            ArgumentNullException.ThrowIfNull(restored);
            // Apply after a restore would commit to plates no longer in the nest.
            CuttingInspection?.RequestClose();
            if (CuttingInspection != null)
                throw new InvalidOperationException("Close Cutting Inspection before undoing.");

            var live = Nest;
            restored.Name = live.Name;
            restored.Status = live.Status;
            restored.MadeBy = live.MadeBy;
            restored.DateCreated = live.DateCreated;

            var plateIndex = PlateManager.CurrentIndex;
            var partCount = PlateView.Plate?.Parts.Count ?? 0;
            var selected = PlateView.SelectedParts
                .Select(layout => PlateView.Plate.Parts.IndexOf(layout.BasePart))
                .Where(index => index >= 0)
                .ToArray();
            var viewport = PlateView.CaptureViewport();

            restoringNest = true;
            try
            {
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
                foreach (var plate in restored.Plates.Where(plate => plate.CutOffs.Count > 0))
                    plate.RegenerateCutOffs(PlateView.CutOffSettings);
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
            finally
            {
                restoringNest = false;
            }

            recordedFingerprint = NestFingerprint.Compute(Nest);
            NestRestored?.Invoke(this, EventArgs.Empty);
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
        private int warnedFingerprint;

        // Every edit should open a change scope; one that does not folds into the next entry.
        private void CheckForUnrecordedChange(object sender, EventArgs e)
        {
            if (IsDisposed || IsChangeInProgress || PlateView.IsFillInProgress || DateTime.UtcNow < nextUnrecordedCheck)
                return;
            nextUnrecordedCheck = DateTime.UtcNow.AddMilliseconds(500);
            var fingerprint = NestFingerprint.Compute(Nest);
            if (fingerprint == recordedFingerprint || fingerprint == warnedFingerprint)
                return;
            warnedFingerprint = fingerprint;
            Debug.WriteLine($"Undo: '{Nest.Name}' changed outside a change scope; the edit is not recorded yet.");
        }
#endif
    }
}
