using System;
using System.ComponentModel;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Controls
{
    /// <summary>
    /// A thickness box that lists the standard gauges and plate fractions ("16 ga - 0.0598")
    /// for the current units while still accepting any typed decimal. See
    /// <see cref="StandardThicknesses"/> for the accepted text.
    /// </summary>
    public class ThicknessComboBox : ComboBox
    {
        private Units units = Units.Inches;

        public ThicknessComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDown;
            MaxDropDownItems = 16;
            FillItems();
        }

        /// <summary>The units the listed values and typed numbers are in.</summary>
        [DefaultValue(Units.Inches)]
        public Units Units
        {
            get { return units; }
            set
            {
                if (units == value)
                    return;

                // The thickness keeps its number; only the listed values change units.
                var valid = TryGetValue(out var current);
                units = value;
                FillItems();
                if (valid)
                    Value = current;
            }
        }

        /// <summary>True when the text reads as a thickness.</summary>
        [Browsable(false)]
        public bool IsValid => TryGetValue(out _);

        /// <summary>The thickness in <see cref="Units"/>; 0 when blank or unreadable.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double Value
        {
            get { return TryGetValue(out var value) ? value : 0; }
            set { Text = StandardThicknesses.Format(value, units); }
        }

        public bool TryGetValue(out double value) => StandardThicknesses.TryParse(Text, units, out value);

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            SelectAll();
        }

        protected override void OnLeave(EventArgs e)
        {
            // Show a typed gauge or matching decimal as its listed label.
            if (TryGetValue(out var value) && Text.Trim().Length > 0)
                Value = value;
            base.OnLeave(e);
        }

        private void FillItems()
        {
            var text = Text;
            Items.Clear();
            foreach (var entry in StandardThicknesses.Entries)
                Items.Add(StandardThicknesses.Label(entry, units));
            Text = text;
        }
    }
}
