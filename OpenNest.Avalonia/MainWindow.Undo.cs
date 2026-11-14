using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenNest.Desktop;
using OpenNest.Undo;
using Visual = global::Avalonia.Visual;

namespace OpenNest.Avalonia
{
    /// <summary>
    /// Edit &gt; Undo and Redo of the active nest, through its tab's shared
    /// <see cref="NestUndoRecorder"/>: Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z unless a text box has
    /// focus, the menu naming the next step, and nothing while Auto Nest works on the nest.
    /// Switching tabs rebinds the menu to the active nest's history.
    /// </summary>
    public partial class MainWindow
    {
        // Each open tab's history handler, so closing the tab unhooks it.
        private readonly System.Collections.Generic.Dictionary<NestTab, EventHandler> undoHandlers = new();

        /// <summary>Follows <paramref name="tab"/>'s history: its unsaved dot and, while active, the Edit menu.</summary>
        private void WatchHistory(NestTab tab)
        {
            EventHandler handler = (_, _) => OnHistoryChanged(tab);
            undoHandlers[tab] = handler;
            tab.Undo.Changed += handler;
        }

        private void UnwatchHistory(NestTab tab)
        {
            if (undoHandlers.Remove(tab, out var handler))
                tab.Undo.Changed -= handler;
        }

        private void OnHistoryChanged(NestTab tab)
        {
            RebuildTabStrip();
            if (tab == tabs.Active)
                UpdateUndoMenu();
        }

        /// <summary>
        /// Handles Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z for the active nest, as the WinForms main
        /// window does. A focused text editor keeps them for its own typing.
        /// </summary>
        private bool ProcessUndoKeys(KeyEventArgs e)
        {
            var modifiers = e.KeyModifiers & ~KeyModifiers.Shift;
            if (modifiers != KeyModifiers.Control || e.Key is not (Key.Z or Key.Y))
                return false;
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (e.Key == Key.Y && shift)
                return false;
            if (tabs.Active == null || KeepsOwnUndo(FocusManager?.GetFocusedElement() as Visual))
                return false;

            if (e.Key == Key.Z && !shift)
                TryUndo(tabs.Active);
            else
                TryRedo(tabs.Active);
            return true;
        }

        /// <summary>
        /// True for controls that undo their own typing: a text box, including the one inside a
        /// numeric box, an editable combo box or a grid cell being edited.
        /// </summary>
        internal static bool KeepsOwnUndo(Visual focused)
        {
            for (var visual = focused; visual != null; visual = visual.GetVisualParent())
            {
                if (visual is TextBox)
                    return true;
            }
            return false;
        }

        // Not while Auto Nest works on the nest: its result is applied to the nest it started on.
        private bool CanUndoOrRedo(NestTab tab) => tab != null && tab != nestingTab;

        private bool TryUndo(NestTab tab)
        {
            if (!CanUndoOrRedo(tab) || !tab.Undo.CanUndo)
                return false;
            return Step(tab, "undo", tab.Undo.Undo);
        }

        private bool TryRedo(NestTab tab)
        {
            if (!CanUndoOrRedo(tab) || !tab.Undo.CanRedo)
                return false;
            return Step(tab, "redo", tab.Undo.Redo);
        }

        private bool Step(NestTab tab, string what, Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                // A copy that cannot be read back leaves the nest and the history as they were.
                ShowNotice($"Could not {what} in {tab.Name}: {ex.Message}");
                UpdateUndoMenu();
                return false;
            }
            UpdateUndoMenu();
            return true;
        }

        private void OnUndo(object sender, RoutedEventArgs e)
        {
            if (tabs.Active != null)
                TryUndo(tabs.Active);
        }

        private void OnRedo(object sender, RoutedEventArgs e)
        {
            if (tabs.Active != null)
                TryRedo(tabs.Active);
        }

        // Busy states have no change event of their own; check them whenever the menu opens.
        private void OnEditMenuOpened(object sender, RoutedEventArgs e) => UpdateUndoMenu();

        /// <summary>Shows the active nest's next Undo and Redo steps, enabled when they can run.</summary>
        private void UpdateUndoMenu()
        {
            var tab = tabs.Active;
            var available = CanUndoOrRedo(tab);
            UndoMenuItem.IsEnabled = available && tab.Undo.CanUndo;
            RedoMenuItem.IsEnabled = available && tab.Undo.CanRedo;
            UndoMenuItem.Header = AccessKeyed(tab?.Undo.UndoText ?? "Undo");
            RedoMenuItem.Header = AccessKeyed(tab?.Undo.RedoText ?? "Redo");
        }

        // "Undo Rotate" as "_Undo Rotate": U and R stay the access keys, a label's own
        // underscores are shown as typed.
        private static string AccessKeyed(string text) => "_" + text.Replace("_", "__");

#if DEBUG
        private DispatcherTimer unrecordedCheck;

        // Every edit should open a change scope; one that does not folds into the next entry.
        private void StartUnrecordedChangeCheck()
        {
            unrecordedCheck = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
                (_, _) =>
                {
                    var tab = tabs.Active;
                    if (tab != null && tab != nestingTab && tab.Undo.TakeUnrecordedChangeWarning())
                        Debug.WriteLine($"Undo: '{tab.Name}' changed outside a change scope; the edit is not recorded yet.");
                });
            unrecordedCheck.Start();
            Closed += (_, _) => unrecordedCheck.Stop();
        }
#endif
    }
}
