using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// File &gt; Drawing Library, File &gt; Drawing Sets and File &gt; Releases (Database mode), one
    /// window with three tabs. Library browses and edits the server's drawing library with
    /// <see cref="DrawingLibraryView"/>; Sets browses, edits and releases the server's drawing sets with
    /// <see cref="DrawingSetBrowser"/>; Releases lists the releases with
    /// <see cref="SetReleasesView"/>, and a set's Releases (or a library drawing's) opens that
    /// tab filtered to it. Once release is enabled, a <see cref="ReleasePanel"/> beside the
    /// Library and Sets tabs builds one release from both: a double-click adds a set or a
    /// drawing to it. It needs no open nest, since a release creates new nests or merges into
    /// draft ones and opens each in its own tab. Each tab reads its list when first shown;
    /// Releases re-reads its page whenever it is shown again, so a release made meanwhile appears.
    /// </summary>
    public partial class DrawingSetsForm : Form
    {
        private const int ReleasePanelWidth = 620;

        private readonly IDrawingSetStore sets;
        private readonly IDrawingLibraryStore library;
        private readonly string serverUrl;
        private bool shown;

        internal DrawingSetBrowser Browser => browser;

        internal DrawingLibraryView LibraryView => libraryView;

        internal SetReleasesView Releases => releasesView;

        /// <summary>The release being built; shown beside the Library and Sets tabs once release is enabled.</summary>
        internal ReleasePanel ReleasePanel => releasePanel;

        /// <summary>True while the Releases tab is showing.</summary>
        public bool ShowsReleases => tabs.SelectedTab == releasesTab;

        /// <param name="library">Used by the set editor to pick drawings.</param>
        public DrawingSetsForm(IDrawingSetStore sets, IDrawingLibraryStore library, string serverUrl)
        {
            InitializeComponent();
            this.sets = sets ?? throw new ArgumentNullException(nameof(sets));
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.serverUrl = serverUrl ?? "";
            browser.Initialize(sets, library, serverUrl);
            browser.ShowReleases = set => _ = ShowReleasesAsync(set);
            libraryView.Initialize(library, serverUrl, null, sets);
            libraryView.ShowReleases = drawing => _ = ShowReleasesAsync(drawing: drawing);
            releasesView.Initialize(sets);
            Text = $"Drawing Library — {serverUrl}";
            // Shown modeless, where a DialogResult button does not close the window.
            closeButton.Click += (_, _) => Close();
            Load += (_, _) => FitReleasePanel();
        }

        /// <summary>
        /// Shows the release panel beside the Library and Sets tabs, and New Release on the
        /// Releases tab: nests are saved through <paramref name="nests"/> and each is opened with
        /// <paramref name="openNest"/>; a nest <paramref name="isNestOpen"/> reports as open in a
        /// tab is never merged into.
        /// </summary>
        public void EnableRelease(INestRepository nests, Action<Nest, NestRecord> openNest, Func<Guid, bool> isNestOpen = null)
        {
            releasePanel.Initialize(sets, library, nests, serverUrl, openNest);
            releasePanel.IsNestOpen = isNestOpen ?? (_ => false);
            browser.EnableRelease(set => releasePanel.AddSet(set));
            libraryView.EnableRelease((drawing, quantity) => releasePanel.AddDrawings(new[] { drawing }, quantity));
            releasesView.EnableRelease(StartRelease);
            UpdateReleasePanel();
        }

        /// <summary>Raised once Make Nests has opened the nests it made.</summary>
        public event EventHandler NestsOpened
        {
            add => releasePanel.NestsOpened += value;
            remove => releasePanel.NestsOpened -= value;
        }

        /// <summary>The release being built, to keep while the window is closed; null when there is none.</summary>
        public ReleaseDraft TakeRelease() => releasePanel.TakeDraft();

        /// <summary>Lists a release kept by <see cref="TakeRelease"/> again.</summary>
        public void RestoreRelease(ReleaseDraft draft) => releasePanel.Restore(draft);

        // New Release: the release is built on the Sets tab, starting afresh once one has run.
        private void StartRelease()
        {
            ShowSets();
            if (releasePanel.Result != null && releasePanel.ConfirmDiscard())
                releasePanel.Clear();
            releasePanel.Focus();
        }

        // The panel shows beside the lists it adds from, never over the releases.
        private void UpdateReleasePanel()
        {
            var show = releasePanel.IsInitialized && !ShowsReleases;
            if (splitContainer.Panel2Collapsed == !show)
                return;
            splitContainer.Panel2Collapsed = !show;
            if (show)
                FitReleasePanel();
        }

        private void FitReleasePanel()
        {
            if (splitContainer.Panel2Collapsed)
                return;
            splitContainer.Panel1MinSize = 300;
            splitContainer.Panel2MinSize = releasePanel.MinimumSize.Width;
            var available = splitContainer.Width - splitContainer.SplitterWidth;
            if (available >= splitContainer.Panel1MinSize + splitContainer.Panel2MinSize)
                splitContainer.SplitterDistance = System.Math.Clamp(available - ReleasePanelWidth,
                    splitContainer.Panel1MinSize, available - splitContainer.Panel2MinSize);
        }

        // A draft closed in a tab meanwhile can be merged into now.
        private void DrawingSetsForm_Activated(object sender, EventArgs e) => releasePanel.RefreshOpenNests();

        private void DrawingSetsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Nests may be on their way to the server; Make Nests stops between uploads, never mid-way.
            if (releasePanel.IsRunning)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Make Nests is still running. Stop it, or wait for it to finish, before closing.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (e.CloseReason == CloseReason.UserClosing && !releasePanel.ConfirmDiscard())
                e.Cancel = true;
        }

        /// <summary>
        /// Switches to the Releases tab, listing the releases that include <paramref name="set"/>,
        /// that had <paramref name="drawing"/> on its own, or (both null) every release. Callable
        /// before the window is shown, to open it on that tab; the page is read once it shows.
        /// </summary>
        public Task ShowReleasesAsync(DrawingSetRecord set = null, DrawingLibraryRecord drawing = null)
        {
            releasesView.SetFilter(set, drawing);
            if (ShowsReleases)
                return shown ? releasesView.RefreshAsync() : Task.CompletedTask;
            // Selecting the tab reads the page once the window is shown.
            tabs.SelectedTab = releasesTab;
            return Task.CompletedTask;
        }

        /// <summary>Switches to the Library tab.</summary>
        public void ShowLibrary() => tabs.SelectedTab = libraryTab;

        /// <summary>Switches to the Sets tab.</summary>
        public void ShowSets() => tabs.SelectedTab = setsTab;

        /// <summary>
        /// Opens Import BOM on the Sets tab once the window is shown (File &gt; Import BOM in
        /// Database mode).
        /// </summary>
        public bool ImportBomWhenShown { get; set; }

        /// <summary>Switches to the Sets tab and opens Import BOM on it.</summary>
        public Task ImportBomAsync()
        {
            ShowSets();
            return browser.ImportBomAsync();
        }

        private async void DrawingSetsForm_Shown(object sender, EventArgs e)
        {
            shown = true;
            await RefreshSelectedTabAsync();
            if (ImportBomWhenShown && !IsDisposed)
            {
                ImportBomWhenShown = false;
                await ImportBomAsync();
            }
        }

        private async void Tabs_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateReleasePanel();
            if (shown)
                await RefreshSelectedTabAsync();
        }

        private Task RefreshSelectedTabAsync()
        {
            if (ShowsReleases)
                return releasesView.RefreshAsync();
            if (tabs.SelectedTab == libraryTab)
                return libraryView.HasLoaded ? Task.CompletedTask : libraryView.RefreshAsync();
            return browser.HasLoaded ? Task.CompletedTask : browser.RefreshAsync();
        }
    }
}
