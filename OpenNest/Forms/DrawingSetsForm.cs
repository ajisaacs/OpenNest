using System;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// File &gt; Drawing Sets (Database mode): browses, edits and releases the server's drawing
    /// sets with <see cref="Controls.DrawingSetBrowser"/>. It needs no open nest, since a
    /// release creates new nests or merges into draft ones and opens each in its own tab.
    /// </summary>
    public partial class DrawingSetsForm : Form
    {
        internal OpenNest.Controls.DrawingSetBrowser Browser => browser;

        /// <param name="library">Used by the set editor to pick drawings.</param>
        public DrawingSetsForm(IDrawingSetStore sets, IDrawingLibraryStore library, string serverUrl)
        {
            InitializeComponent();
            browser.Initialize(sets, library, serverUrl);
            Text = $"Drawing Sets — {serverUrl}";
        }

        /// <summary>
        /// Shows Release: nests are saved through <paramref name="nests"/> and each is opened
        /// with <paramref name="openNest"/>; a nest <paramref name="isNestOpen"/> reports as open
        /// in a tab is never merged into.
        /// </summary>
        public void EnableRelease(INestRepository nests, Action<Nest, NestRecord> openNest, Func<Guid, bool> isNestOpen = null) =>
            browser.EnableRelease(nests, openNest, isNestOpen);

        private async void DrawingSetsForm_Shown(object sender, EventArgs e) => await browser.RefreshAsync();
    }
}
