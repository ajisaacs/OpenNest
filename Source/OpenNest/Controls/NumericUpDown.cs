using System;

namespace OpenNest.Controls
{
    public class NumericUpDown : System.Windows.Forms.NumericUpDown
    {
        private string suffix;

        

        public NumericUpDown()
        {
            suffix = string.Empty;
        }

        public string Suffix
        {
            get { return suffix; }
            set 
            { 
                suffix = value;
                UpdateEditText();
            }
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            this.Select(0, Text.Length);
        }

        protected override void UpdateEditText()
        {
            if (Suffix != null)
                Text = Value.ToString("N" + DecimalPlaces) + Suffix;
            else
                base.UpdateEditText();
        }
    }
}
