using System;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// Chooses which machine's acceleration limits time the cut, or none for full speed
    /// everywhere. Remembers the choice; until one is made, the first machine is used.
    /// </summary>
    internal sealed class CutTimeMachineBox : ComboBox
    {
        private const string ConstantSpeed = "none";

        private sealed record Choice(MachineConfig Machine)
        {
            public override string ToString() =>
                Machine == null ? "None (full speed)"
                : Machine.Motion == null ? $"{Machine.Name} (placeholder)"
                : Machine.Name;
        }

        public CutTimeMachineBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            Width = 200;

            Items.Add(new Choice(null));
            foreach (var machine in MachineSettings.LoadMachines().OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
                Items.Add(new Choice(machine));

            var saved = Properties.Settings.Default.CutTimeMachine;
            var choices = Items.Cast<Choice>().ToList();
            SelectedItem = saved == ConstantSpeed
                ? choices[0]
                : choices.FirstOrDefault(c => c.Machine?.Id.ToString() == saved) ?? choices.ElementAtOrDefault(1) ?? choices[0];
        }

        public MachineConfig Machine => (SelectedItem as Choice)?.Machine;

        /// <summary>The chosen machine's limits in <paramref name="units"/>, or null for full speed.</summary>
        public MotionProfile GetMotion(Units units) =>
            Machine?.GetMotionOrPlaceholder().ToProfile(Machine.Units, units);

        public void SaveChoice()
        {
            Properties.Settings.Default.CutTimeMachine = Machine?.Id.ToString() ?? ConstantSpeed;
            Properties.Settings.Default.Save();
        }
    }
}
