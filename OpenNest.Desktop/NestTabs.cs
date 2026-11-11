using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenNest.Desktop
{
    /// <summary>An open nest: its document, its file and whether it has unsaved changes.</summary>
    public sealed class NestTab
    {
        internal NestTab(Document document, string path)
        {
            Document = document;
            Path = path;
        }

        public Document Document { get; }

        /// <summary>The full path of the file it was opened from or saved to, else null.</summary>
        public string Path { get; internal set; }

        /// <summary>
        /// The file Save writes without asking: the one saved to this session, if it still
        /// exists. Null for a nest only opened, as in the WinForms app, so the first Save asks.
        /// </summary>
        public string SavePath => Document.HasSavePath ? Document.LastSavePath : null;

        /// <summary>True after a change this session that is not saved.</summary>
        public bool Modified { get; private set; }

        /// <summary>The nest's name, else the file name.</summary>
        public string Name => !string.IsNullOrWhiteSpace(Document.Name)
            ? Document.Name
            : System.IO.Path.GetFileNameWithoutExtension(Path ?? string.Empty);

        public void MarkModified() => Modified = true;

        public void MarkSaved() => Modified = false;
    }

    /// <summary>
    /// A save or export refused before anything was written; the message says why and what to
    /// do instead.
    /// </summary>
    public sealed class SaveTargetRefusedException : InvalidOperationException
    {
        public SaveTargetRefusedException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// The nests open in the desktop app, one tab each, and which one is active. Opening a file
    /// that is already open activates its tab; closing the active tab activates the one to its
    /// right, else the one to its left.
    /// </summary>
    public sealed class NestTabs
    {
        private readonly List<NestTab> tabs = new();

        /// <summary>Raised after a tab is added, closed or activated.</summary>
        public event EventHandler Changed;

        public IReadOnlyList<NestTab> Tabs => tabs;

        /// <summary>The active tab's index, or -1 when no nest is open.</summary>
        public int ActiveIndex { get; private set; } = -1;

        public NestTab Active => ActiveIndex >= 0 ? tabs[ActiveIndex] : null;

        /// <summary>True when any open nest has unsaved changes.</summary>
        public bool AnyModified => tabs.Any(t => t.Modified);

        /// <summary>
        /// Activates the tab already showing <paramref name="path"/>, or loads it with
        /// <paramref name="load"/> into a new tab after the others. A load that throws adds no tab.
        /// </summary>
        public NestTab Open(string path, Func<string, Document> load)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(load);
            var fullPath = System.IO.Path.GetFullPath(path);
            var existing = tabs.FindIndex(t => t.Path != null && SamePath(t.Path, fullPath));
            if (existing >= 0)
            {
                Activate(existing);
                return tabs[existing];
            }

            var tab = new NestTab(load(fullPath), fullPath);
            tabs.Add(tab);
            ActiveIndex = tabs.Count - 1;
            Changed?.Invoke(this, EventArgs.Empty);
            return tab;
        }

        public void Activate(int index)
        {
            if (index < 0 || index >= tabs.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (index == ActiveIndex)
                return;
            ActiveIndex = index;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Removes the tab; the host asks about unsaved changes first.</summary>
        public void Close(NestTab tab)
        {
            var index = tabs.IndexOf(tab);
            if (index < 0)
                return;

            tabs.RemoveAt(index);
            if (tabs.Count == 0)
                ActiveIndex = -1;
            else if (index < ActiveIndex)
                ActiveIndex--;
            else if (index == ActiveIndex)
                ActiveIndex = System.Math.Min(index, tabs.Count - 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Saves the tab's nest to <paramref name="path"/>, which becomes its file and name, and
        /// clears its unsaved mark. Refuses a file another open tab shows, so one file never has
        /// two tabs. A failed write throws and changes nothing.
        /// </summary>
        public void SaveAs(NestTab tab, string path)
        {
            var fullPath = CheckTarget(tab, path, export: false);
            tab.Document.SaveAs(fullPath);
            tab.Path = fullPath;
            tab.MarkSaved();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Writes a copy of the tab's nest to <paramref name="path"/> without changing its file,
        /// name or unsaved mark. Refuses the tab's own file, which only Save may write, and a
        /// file another open tab shows.
        /// </summary>
        public void ExportCopy(NestTab tab, string path)
        {
            var fullPath = CheckTarget(tab, path, export: true);
            tab.Document.ExportCopy(fullPath);
        }

        private string CheckTarget(NestTab tab, string path, bool export)
        {
            ArgumentNullException.ThrowIfNull(tab);
            ArgumentNullException.ThrowIfNull(path);
            if (!tabs.Contains(tab))
                throw new SaveTargetRefusedException(
                    $"{tab.Name} was closed before it could be written. Open it again to save it.");
            var fullPath = System.IO.Path.GetFullPath(path);
            if (export && tab.Path != null && SamePath(tab.Path, fullPath))
                throw new SaveTargetRefusedException(
                    $"{System.IO.Path.GetFileName(fullPath)} is this nest's own file. Use "
                    + "File > Save to save it there, or export the copy under another name.");
            var other = tabs.FirstOrDefault(
                t => t != tab && t.Path != null && SamePath(t.Path, fullPath));
            if (other != null)
                throw new SaveTargetRefusedException(
                    $"{System.IO.Path.GetFileName(fullPath)} is open in the {other.Name} tab. "
                    + "Close that tab first, or choose another file name.");
            return fullPath;
        }

        // Windows paths ignore case; Linux and macOS paths do not.
        private static bool SamePath(string a, string b) => string.Equals(a, b,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }
}
