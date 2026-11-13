using System;
using OpenNest.Data;
using OpenNest.IO;

namespace OpenNest.Undo
{
    /// <summary>
    /// Records a document's nest edits into its undo history and steps through it, for any
    /// desktop front end. Edits go through <see cref="BeginChange"/> or
    /// <see cref="RecordChange"/>; an edit made outside a scope is found by fingerprint before
    /// the next scope, Undo, Redo or save and committed as "Change". Undo and Redo read a
    /// whole-nest copy back and hand it to <see cref="SwapIn"/>, which the view sets to rebind
    /// everything that held the old nest.
    /// </summary>
    public sealed class NestUndoRecorder
    {
        /// <summary>The label of an edit no scope recorded, found by fingerprint.</summary>
        public const string UnrecordedChangeLabel = "Change";

        private readonly Document document;
        private ChangeScope openChange;
        private int recordedFingerprint;
        private int warnedFingerprint;

        public NestUndoRecorder(Document document)
        {
            ArgumentNullException.ThrowIfNull(document);
            this.document = document;
            document.History.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Raised after the history or the recorded state changed: a commit, an Undo or Redo,
        /// a save point or a restore. Unsaved-change marks and Edit menu labels follow it.
        /// </summary>
        public event EventHandler Changed;

        /// <summary>Raised after <see cref="RestoreNest"/> swapped a nest in.</summary>
        public event EventHandler Restored;

        public Document Document => document;

        public UndoHistory<NestUndoState> History => document.History;

        /// <summary>
        /// Puts a nest read back from the history in the document and rebinds the view to it.
        /// It must set <see cref="OpenNest.Document.Nest"/>; the default does only that. A view
        /// that cannot take the nest now throws and the live nest stays.
        /// </summary>
        public Action<Nest> SwapIn { get; set; }

        /// <summary>True while <see cref="RestoreNest"/> swaps a nest in.</summary>
        public bool IsRestoring { get; private set; }

        /// <summary>True while a change scope is open or a nest is being swapped in.</summary>
        public bool IsChangeInProgress => openChange != null || IsRestoring;

        /// <summary>Whether <see cref="Undo"/> would step back: there is a step and no edit is open.</summary>
        public bool CanUndo => History.CanUndo && !IsChangeInProgress;

        public bool CanRedo => History.CanRedo && !IsChangeInProgress;

        /// <summary>"Undo Rotate", or "Undo" when there is nothing to undo.</summary>
        public string UndoText => UndoMenuText(History);

        /// <summary>"Redo Fill", or "Redo" when there is nothing to redo.</summary>
        public string RedoText => RedoMenuText(History);

        public static string UndoMenuText<TState>(UndoHistory<TState> history)
            where TState : class, IUndoState =>
            history?.UndoLabel is { } label ? $"Undo {label}" : "Undo";

        public static string RedoMenuText<TState>(UndoHistory<TState> history)
            where TState : class, IUndoState =>
            history?.RedoLabel is { } label ? $"Redo {label}" : "Redo";

        /// <summary>
        /// True when the nest differs from the state last saved (or opened): the history has
        /// moved off its save point, or an edit no scope recorded is pending.
        /// </summary>
        public bool HasUnsavedChanges =>
            History.Current != null
            && (!History.IsAtSavePoint || NestFingerprint.Compute(document.Nest) != recordedFingerprint);

        /// <summary>True when the nest changed since the last recorded state without a scope.</summary>
        public bool HasUnrecordedChange =>
            History.Current != null && NestFingerprint.Compute(document.Nest) != recordedFingerprint;

        /// <summary>
        /// Starts the history over from the nest as it stands, as opening it does. The state is
        /// treated as saved.
        /// </summary>
        public void Reset()
        {
            History.Reset(new NestUndoState(NestSnapshot.Capture(document.Nest)));
            recordedFingerprint = NestFingerprint.Compute(document.Nest);
            Changed?.Invoke(this, EventArgs.Empty);
        }

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
            if (IsRestoring)
                return ChangeScope.Detached();

            RecordUnrecordedChange();
            openChange = ChangeScope.Root(label, coalesceKey, EndChange);
            return openChange;
        }

        /// <summary>Runs <paramref name="edit"/>, which changes the nest, as one undoable step.</summary>
        public void RecordChange(string label, Action edit)
        {
            ArgumentNullException.ThrowIfNull(edit);
            using var change = BeginChange(label);
            change.MarkChanged();
            edit();
            change.Complete();
        }

        /// <summary>Steps back one edit; does nothing while an edit or restore is in progress.</summary>
        public void Undo()
        {
            if (IsChangeInProgress)
                return;
            RecordUnrecordedChange();
            if (History.CanUndo)
                Restore(History.Undo(), History.Redo);
        }

        /// <summary>Repeats the next undone edit; does nothing while an edit or restore is in progress.</summary>
        public void Redo()
        {
            if (IsChangeInProgress)
                return;
            // An edit made since the undo drops what could be redone.
            RecordUnrecordedChange();
            if (History.CanRedo)
                Restore(History.Redo(), History.Undo);
        }

        /// <summary>
        /// Marks the nest as it stands saved, committing any edit no scope recorded first so
        /// the save point covers it.
        /// </summary>
        public void MarkSaved()
        {
            RecordUnrecordedChange();
            if (History.Current != null)
                History.MarkSaved();
        }

        /// <summary>
        /// Commits an edit no scope recorded as its own entry, so a rollback, Undo or Redo
        /// does not silently discard it, and a save marks it saved. Detected by fingerprint, so
        /// best effort.
        /// </summary>
        public void RecordUnrecordedChange()
        {
            if (HasUnrecordedChange)
                Commit(UnrecordedChangeLabel, null);
        }

        /// <summary>
        /// True the first time an unrecorded edit is seen, for a debug warning that an entry
        /// point forgot its scope; false while a scope is open or for the edit already reported.
        /// </summary>
        public bool TakeUnrecordedChangeWarning()
        {
            if (IsChangeInProgress || History.Current == null)
                return false;
            var fingerprint = NestFingerprint.Compute(document.Nest);
            if (fingerprint == recordedFingerprint || fingerprint == warnedFingerprint)
                return false;
            warnedFingerprint = fingerprint;
            return true;
        }

        /// <summary>
        /// Replaces the document's nest with <paramref name="restored"/>, a copy read back from
        /// the history, through <see cref="SwapIn"/>. The nest's identity (name, status, author,
        /// creation date) is carried over from the live nest; nothing it does is recorded.
        /// </summary>
        public void RestoreNest(Nest restored)
        {
            ArgumentNullException.ThrowIfNull(restored);
            var live = document.Nest;
            restored.Name = live.Name;
            restored.Status = live.Status;
            restored.MadeBy = live.MadeBy;
            restored.DateCreated = live.DateCreated;

            IsRestoring = true;
            try
            {
                if (SwapIn != null)
                    SwapIn(restored);
                else
                    document.Nest = restored;
            }
            finally
            {
                IsRestoring = false;
            }

            recordedFingerprint = NestFingerprint.Compute(document.Nest);
            Changed?.Invoke(this, EventArgs.Empty);
            Restored?.Invoke(this, EventArgs.Empty);
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
                restored = state.Snapshot.Restore(document.Nest);
            }
            catch
            {
                moveBack();
                throw;
            }
            RestoreNest(restored);
        }

        private void EndChange(ChangeScope scope)
        {
            openChange = null;
            if (!scope.IsChanged)
                return;
            if (scope.Succeeded)
                Commit(scope.Label, scope.CoalesceKey);
            else
                RestoreNest(History.Current.Snapshot.Restore(document.Nest));
        }

        private void Commit(string label, object coalesceKey)
        {
            History.Commit(label, new NestUndoState(NestSnapshot.Capture(document.Nest)), coalesceKey);
            recordedFingerprint = NestFingerprint.Compute(document.Nest);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
