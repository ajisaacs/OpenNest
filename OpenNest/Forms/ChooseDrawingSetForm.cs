using System;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// Drawing Library &gt; Add to Set: chooses an active drawing set, or a new one, with the
    /// set browser in pick mode. Nothing is changed here; the caller opens the set editor.
    /// </summary>
    public partial class ChooseDrawingSetForm : Form
    {
        /// <summary>The listed set chosen; null when New Set was chosen or nothing was.</summary>
        public DrawingSetRecord PickedSet { get; private set; }

        /// <summary>True when New Set was chosen instead of a listed set.</summary>
        public bool PickedNew { get; private set; }

        internal OpenNest.Controls.DrawingSetBrowser Browser => browser;

        public ChooseDrawingSetForm(IDrawingSetStore sets, IDrawingLibraryStore library, string serverUrl)
        {
            InitializeComponent();
            browser.Initialize(sets, library, serverUrl, pick: true);
            Text = $"Choose a Drawing Set — {serverUrl}";
        }

        private async void ChooseDrawingSetForm_Shown(object sender, EventArgs e) => await browser.RefreshAsync();

        private void Browser_SelectedSetChanged(object sender, EventArgs e) =>
            chooseButton.Enabled = browser.SelectedSet is { Archived: false };

        private void Browser_SetActivated(object sender, EventArgs e) => Choose();

        private void ChooseButton_Click(object sender, EventArgs e) => Choose();

        private void Browser_NewSetChosen(object sender, EventArgs e)
        {
            PickedNew = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Choose()
        {
            if (browser.SelectedSet is not { Archived: false } set)
                return;
            PickedSet = set;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
