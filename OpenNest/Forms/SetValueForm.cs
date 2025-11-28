using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class SetValueForm : Form
    {
        public SetValueForm()
        {
            InitializeComponent();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    Close();
                    break;
            }

            return base.ProcessDialogKey(keyData);
        }

        public double Minimum
        {
            get { return (double)numericUpDownValue.Minimum; }
            set { numericUpDownValue.Minimum = (decimal)value; }
        }

        public double Maximum
        {
            get { return (double)numericUpDownValue.Maximum; }
            set { numericUpDownValue.Maximum = (decimal)value; }
        }

        public double Increment
        {
            get { return (double)numericUpDownValue.Increment; }
            set { numericUpDownValue.Increment = (decimal)value; }
        }

        public double Value
        {
            get { return (double)numericUpDownValue.Value; }
            set { numericUpDownValue.Value = (decimal)value; }
        }
    }
}
