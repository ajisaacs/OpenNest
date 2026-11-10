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

        /// <summary>The full path of the file it was opened from, else null.</summary>
        public string Path { get; }

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

        // Windows paths ignore case; Linux and macOS paths do not.
        private static bool SamePath(string a, string b) => string.Equals(a, b,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }
}
