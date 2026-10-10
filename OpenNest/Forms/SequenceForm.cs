using System;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class SequenceForm : Form
    {
        public SequenceForm()
        {
            InitializeComponent();
        }

        public event EventHandler DoneClicked;

        private void doneButton_Click(object sender, EventArgs e)
        {
            DoneClicked?.Invoke(this, EventArgs.Empty);
        }

        private void numericUpDown1_Leave(object sender, EventArgs e)
        {
            numericUpDown1.Validate();
        }
    }
}
