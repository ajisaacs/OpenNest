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
    /// tab filtered to it. It needs no open nest, since a release creates new nests or merges
    /// into draft ones and opens each in its own tab. Each tab reads its list when first shown;
    /// Releases re-reads its page whenever it is shown again, so a release made on Sets appears.
    /// </summary>
    public partial class DrawingSetsForm : Form
    {
        private readonly IDrawingLibraryStore library;
        private readonly string serverUrl;
        private bool shown;

        internal DrawingSetBrowser Browser => browser;

        internal DrawingLibraryView LibraryView => libraryView;

        internal SetReleasesView Releases => releasesView;

        /// <summary>True while the Releases tab is showing.</summary>
        public bool ShowsReleases => tabs.SelectedTab == releasesTab;

        /// <param name="library">Used by the set editor to pick drawings.</param>
        public DrawingSetsForm(IDrawingSetStore sets, IDrawingLibraryStore library, string serverUrl)
        {
            InitializeComponent();
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.serverUrl = serverUrl ?? "";
            browser.Initialize(sets, library, serverUrl);
            browser.ShowReleases = set => _ = ShowReleasesAsync(set);
            libraryView.Initialize(library, serverUrl, null, sets);
            libraryView.ShowReleases = drawing => _ = ShowReleasesAsync(drawing: drawing);
            releasesView.Initialize(sets);
            Text = $"Drawing Library — {serverUrl}";
            // Shown as an MDI child, where a DialogResult button no longer closes the window.
            closeButton.Click += (_, _) => Close();
        }

        /// <summary>
        /// Shows Release, and New Release on the Releases tab: nests are saved through
        /// <paramref name="nests"/> and each is opened with <paramref name="openNest"/>; a nest
        /// <paramref name="isNestOpen"/> reports as open in a tab is never merged into.
        /// </summary>
        public void EnableRelease(INestRepository nests, Action<Nest, NestRecord> openNest, Func<Guid, bool> isNestOpen = null)
        {
            libraryView.EnableRelease(nests, openNest, isNestOpen);
            browser.EnableRelease(nests, openNest, isNestOpen);
            releasesView.EnableRelease(library, nests, serverUrl, openNest, isNestOpen);
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
