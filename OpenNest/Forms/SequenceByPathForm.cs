using System;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class SequenceByPathForm : Form
    {
        public SequenceByPathForm()
        {
            InitializeComponent();
            SetLineCount(0);
        }

        public event EventHandler UndoClicked;
        public event EventHandler ClearClicked;
        public event EventHandler DoneClicked;

        public void SetLineCount(int count)
        {
            statusLabel.Text = count == 0
                ? "Drag a line through the parts, or click them, in cut order."
                : $"{count} line{(count == 1 ? "" : "s")}. Drag or click to add the next.";
            undoButton.Enabled = count > 0;
            clearButton.Enabled = count > 0;
        }

        private void undoButton_Click(object sender, EventArgs e) => UndoClicked?.Invoke(this, EventArgs.Empty);

        private void clearButton_Click(object sender, EventArgs e) => ClearClicked?.Invoke(this, EventArgs.Empty);

        private void doneButton_Click(object sender, EventArgs e) => DoneClicked?.Invoke(this, EventArgs.Empty);
    }
}
