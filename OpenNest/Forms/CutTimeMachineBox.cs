using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Desktop;

namespace OpenNest.Forms
{
    /// <summary>
    /// Chooses which machine's acceleration limits time the cut, or none for full speed
    /// everywhere. Remembers the choice; until one is made, the first machine is used.
    /// </summary>
    internal sealed class CutTimeMachineBox : ComboBox
    {
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

            var machines = SavedCutTimeSettings.SortedByName(MachineSettings.LoadMachines());
            Items.Add(new Choice(null));
            foreach (var machine in machines)
                Items.Add(new Choice(machine));

            var saved = SavedMachine(machines);
            SelectedItem = Items.Cast<Choice>().First(c => c.Machine == saved);
        }

        /// <summary>The remembered machine, the first one until a choice is saved, or null for full speed.</summary>
        public static MachineConfig SavedMachine(IEnumerable<MachineConfig> machines) =>
            SavedCutTimeSettings.ChooseMachine(machines, Properties.Settings.Default.CutTimeMachine);

        public MachineConfig Machine => (SelectedItem as Choice)?.Machine;

        /// <summary>The chosen machine's limits in <paramref name="units"/>, or null for full speed.</summary>
        public MotionProfile GetMotion(Units units) =>
            Machine?.GetMotionOrPlaceholder().ToProfile(Machine.Units, units);

        public void SaveChoice()
        {
            Properties.Settings.Default.CutTimeMachine = SavedCutTimeSettings.MachineChoice(Machine);
            Properties.Settings.Default.Save();
        }
    }
}
