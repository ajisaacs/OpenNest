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

        private void numericUpDown1_Leave(object sender, EventArgs e)
        {
            numericUpDown1.Validate();
        }
    }
}
