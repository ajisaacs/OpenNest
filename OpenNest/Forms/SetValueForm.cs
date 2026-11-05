using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class SetValueForm : Form
    {
        private int optionRowHeight;

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

        /// <summary>
        /// Shows a check box under the value with this text; checking it disables the
        /// value. Hidden while empty.
        /// </summary>
        public string OptionText
        {
            get { return checkBoxOption.Text; }
            set
            {
                var show = !string.IsNullOrEmpty(value);
                checkBoxOption.Text = value;
                checkBoxOption.Visible = show;
                // Visible reads false until the form is shown, so track the extra row here.
                var rowHeight = show
                    ? checkBoxOption.PreferredSize.Height + checkBoxOption.Margin.Vertical
                    : 0;
                var width = show
                    ? System.Math.Max(ClientSize.Width,
                        checkBoxOption.PreferredSize.Width + checkBoxOption.Margin.Horizontal + Padding.Horizontal)
                    : ClientSize.Width;
                ClientSize = new Size(width, ClientSize.Height - optionRowHeight + rowHeight);
                optionRowHeight = rowHeight;
            }
        }

        public bool OptionChecked
        {
            get { return checkBoxOption.Checked; }
            set { checkBoxOption.Checked = value; }
        }

        private void checkBoxOption_CheckedChanged(object sender, System.EventArgs e) =>
            numericUpDownValue.Enabled = !checkBoxOption.Checked;
    }
}
