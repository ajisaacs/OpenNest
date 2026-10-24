using System;
using System.Collections.Generic;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// Nest &gt; Add from Drawing Library: <see cref="DrawingLibraryView"/> in a dialog, filtered
    /// to the nest's material and thickness, with an In This Nest tab. Without a nest the same
    /// view is the Library tab of <see cref="DrawingSetsForm"/> (File &gt; Drawing Library).
    /// The dialog stays open after each add, so callers check <see cref="AddedDrawings"/> and
    /// <see cref="QuantitiesChanged"/> rather than the dialog result.
    /// </summary>
    public partial class DrawingLibraryForm : Form
    {
        internal DrawingLibraryView View => view;

        /// <summary>The library drawings the dialog added or raised the quantity of, in order.</summary>
        public List<Drawing> AddedDrawings => view.AddedDrawings;

        /// <summary>True once the In This Nest tab changed a drawing's required quantity.</summary>
        public bool QuantitiesChanged => view.QuantitiesChanged;

        /// <param name="nest">The nest to add drawings to.</param>
        /// <param name="sets">Enables Used in Sets and Add to Set; null hides them.</param>
        public DrawingLibraryForm(IDrawingLibraryStore library, string serverUrl, Nest nest, IDrawingSetStore sets = null)
        {
            InitializeComponent();
            view.Initialize(library, serverUrl, nest, sets);
            Text = $"Add from Drawing Library — {serverUrl}";
            Load += (_, _) =>
            {
                DialogBounds.FitToOwnerScreen(this);
                view.FitDetailsPane();
            };
        }

        /// <summary>Lets the drawing menu release a drawing on its own; see <see cref="DrawingLibraryView.EnableRelease"/>.</summary>
        public void EnableRelease(INestRepository nests, Action<Nest, NestRecord> openNest, Func<Guid, bool> isNestOpen = null) =>
            view.EnableRelease(nests, openNest, isNestOpen);

        private async void DrawingLibraryForm_Shown(object sender, EventArgs e) => await view.RefreshAsync();
    }
}
