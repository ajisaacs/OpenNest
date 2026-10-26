using System;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
        private const Keys UndoKeys = Keys.Control | Keys.Z;
        private const Keys RedoKeys = Keys.Control | Keys.Y;
        private const Keys RedoAlternateKeys = Keys.Control | Keys.Shift | Keys.Z;

        // The window whose history the Edit menu follows.
        private EditNestForm undoSource;

        /// <summary>
        /// Handles Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z for the active nest. A focused text editor
        /// keeps them, and an action with its own step-back (Sequence by Path) gets Ctrl+Z first.
        /// </summary>
        private bool ProcessUndoKeys(Keys keyData)
        {
            if (keyData is not (UndoKeys or RedoKeys or RedoAlternateKeys) || activeForm == null)
                return false;
            if (KeepsOwnUndo(FocusedControl()))
                return false;

            if (keyData == UndoKeys)
            {
                if (activeForm.PlateView.Actions.CurrentAction?.TryUndoStep() != true)
                    TryUndo(activeForm);
            }
            else
                TryRedo(activeForm);
            return true;
        }

        private static Control FocusedControl()
        {
            var handle = Win32.GetFocus();
            return handle == IntPtr.Zero ? null : Control.FromChildHandle(handle);
        }

        /// <summary>
        /// True for controls that undo their own typing: text boxes (the editing control of a
        /// grid or numeric box is one), editable combo boxes, numeric boxes, a grid in edit
        /// mode and the program editor.
        /// </summary>
        internal static bool KeepsOwnUndo(Control focused)
        {
            for (var control = focused; control != null; control = control.Parent)
            {
                switch (control)
                {
                    case TextBoxBase:
                    case System.Windows.Forms.NumericUpDown:
                    case ComboBox { DropDownStyle: not ComboBoxStyle.DropDownList }:
                    case DataGridView { IsCurrentCellInEditMode: true }:
                    case ProgramEditorControl:
                        return true;
                }
            }
            return false;
        }

        internal bool TryUndo(EditNestForm form)
        {
            if (!CanUndoOrRedo(form) || !form.CanUndo)
                return false;
            form.Undo();
            return true;
        }

        internal bool TryRedo(EditNestForm form)
        {
            if (!CanUndoOrRedo(form) || !form.CanRedo)
                return false;
            form.Redo();
            return true;
        }

        private bool CanUndoOrRedo(EditNestForm form) => form is { IsDisposed: false } && !IsUndoBusy(form);

        private void EditUndo_Click(object sender, EventArgs e) => WithActive(form => TryUndo(form));

        private void EditRedo_Click(object sender, EventArgs e) => WithActive(form => TryRedo(form));

        // Busy states have no change event of their own; check them whenever the menu opens.
        private void EditMenu_DropDownOpening(object sender, EventArgs e) => UpdateUndoMenu();

        /// <summary>Follows <paramref name="form"/>'s history in the Edit menu.</summary>
        private void BindUndoMenu(EditNestForm form)
        {
            if (undoSource != null)
            {
                undoSource.History.Changed -= History_Changed;
                undoSource.NestRestored -= EditForm_NestRestored;
            }
            undoSource = form;
            if (undoSource != null)
            {
                undoSource.History.Changed += History_Changed;
                undoSource.NestRestored += EditForm_NestRestored;
            }
            UpdateUndoMenu();
        }

        private void History_Changed(object sender, EventArgs e) => UpdateUndoMenu();

        internal void UpdateUndoMenu()
        {
            var form = activeForm;
            var available = form != null && CanUndoOrRedo(form);
            var history = form?.History;
            mnuEditUndo.Enabled = available && form.CanUndo;
            mnuEditRedo.Enabled = available && form.CanRedo;
            mnuEditUndo.Text = history?.UndoLabel is { } undo ? $"Undo {undo}" : "Undo";
            mnuEditRedo.Text = history?.RedoLabel is { } redo ? $"Redo {redo}" : "Redo";
        }

        private void EditForm_NestRestored(object sender, EventArgs e)
        {
            var form = (EditNestForm)sender;
            // The remnant list was found on a plate no longer in the nest.
            if (remnantViewer is { IsDisposed: false } viewer && ReferenceEquals(viewer.PlateView, form.PlateView))
                viewer.Close();
            if (!ReferenceEquals(form, activeForm))
                return;
            EnableCheck();
            UpdateOverlapMenu();
            statusBar.UpdateAll();
            UpdateUndoMenu();
        }
    }
}
