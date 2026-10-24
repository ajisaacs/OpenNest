using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// <see cref="ReleasePanel"/> in a dialog, for a release started where the Drawing Sets
    /// window's panel is not at hand: Make Nests in Nest &gt; Add from Drawing Library. Add
    /// Drawings lists more library drawings on their own (<see cref="AddReleaseDrawingsForm"/>);
    /// sets are released from the Drawing Sets window. Closing waits for a running release to
    /// stop between uploads.
    /// </summary>
    public partial class ReleaseSetForm : Form
    {
        internal ReleasePanel Panel => releasePanel;

        /// <summary>The finished release, once one has run; otherwise null.</summary>
        public SetReleaseResult Result => releasePanel.Result;

        /// <summary>True when a server nest is open in a tab of this desktop; such a draft is not merged into.</summary>
        public Func<Guid, bool> IsNestOpen
        {
            get => releasePanel.IsNestOpen;
            set => releasePanel.IsNestOpen = value ?? (_ => false);
        }

        /// <summary>Shows the library browser that adds drawings on their own; replaced by tests.</summary>
        internal Action ShowAddDrawings { get; set; }

        /// <param name="setsToRelease">Sets read with <see cref="IDrawingSetStore.GetAsync"/>, members joined, each at one kit.</param>
        /// <param name="drawings">Library drawings to release on their own, each with its quantity.</param>
        /// <param name="openNest">Opens a created or merged nest in its own tab, bound to its server record.</param>
        public ReleaseSetForm(IDrawingSetStore sets, IDrawingLibrary library, INestRepository nests, string serverUrl,
            IReadOnlyList<DrawingSetRecord> setsToRelease, IReadOnlyList<SetReleaseDrawingLine> drawings,
            Action<Nest, NestRecord> openNest)
        {
            InitializeComponent();
            ArgumentNullException.ThrowIfNull(setsToRelease);
            ArgumentNullException.ThrowIfNull(drawings);
            releasePanel.Initialize(sets, library, nests, serverUrl, openNest);
            foreach (var set in setsToRelease)
                releasePanel.AddSet(set);
            foreach (var line in drawings)
                releasePanel.AddDrawings(new[] { line.Drawing }, line.Quantity);
            ShowAddDrawings = () =>
            {
                using var form = new AddReleaseDrawingsForm(library, serverUrl ?? "", releasePanel.AddDrawings,
                    id => releasePanel.DrawingLines().Any(l => l.Drawing.Id == id));
                form.ShowDialog(this);
            };
            Load += (_, _) => DialogBounds.FitToOwnerScreen(this);
        }

        private void AddDrawingsButton_Click(object sender, EventArgs e)
        {
            if (!releasePanel.IsRunning)
                ShowAddDrawings();
        }

        private void ReleaseSetForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Nests may be on their way to the server; stop between uploads, never mid-way.
            if (releasePanel.IsRunning || !releasePanel.ConfirmDiscard())
                e.Cancel = true;
        }
    }
}
