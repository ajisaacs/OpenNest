using OpenNest.Data;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public class MachineConfigForm : Form
    {
        private readonly IDataProvider _provider;
        private readonly TreeView _tree;
        private readonly Panel _detailPanel;

        private MachineConfig _currentMachine;

        public MachineConfigForm(IDataProvider provider)
        {
            _provider = provider;

            Text = "Machine Configuration";
            Size = new Size(900, 600);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(700, 400);

            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 250,
                FixedPanel = FixedPanel.Panel1
            };

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false
            };
            _tree.AfterSelect += Tree_AfterSelect;

            var treeButtonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(2)
            };

            var addMachineButton = new Button { Text = "+ Machine", AutoSize = true };
            addMachineButton.Click += AddMachine_Click;
            var removeMachineButton = new Button { Text = "- Machine", AutoSize = true };
            removeMachineButton.Click += RemoveMachine_Click;
            var addMaterialButton = new Button { Text = "+ Material", AutoSize = true };
            addMaterialButton.Click += AddMaterial_Click;
            var removeMaterialButton = new Button { Text = "- Material", AutoSize = true };
            removeMaterialButton.Click += RemoveMaterial_Click;
            var addThicknessButton = new Button { Text = "+ Thickness", AutoSize = true };
            addThicknessButton.Click += AddThickness_Click;
            var removeThicknessButton = new Button { Text = "- Thickness", AutoSize = true };
            removeThicknessButton.Click += RemoveThickness_Click;

            treeButtonPanel.Controls.AddRange(new Control[]
            {
                addMachineButton, removeMachineButton,
                addMaterialButton, removeMaterialButton,
                addThicknessButton, removeThicknessButton
            });

            splitContainer.Panel1.Controls.Add(_tree);
            splitContainer.Panel1.Controls.Add(treeButtonPanel);

            _detailPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            splitContainer.Panel2.Controls.Add(_detailPanel);

            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(4)
            };

            var saveButton = new Button { Text = "Save", AutoSize = true };
            saveButton.Click += Save_Click;
            var importButton = new Button { Text = "Import...", AutoSize = true };
            importButton.Click += Import_Click;
            var exportButton = new Button { Text = "Export...", AutoSize = true };
            exportButton.Click += Export_Click;

            bottomPanel.Controls.AddRange(new Control[] { saveButton, exportButton, importButton });

            Controls.Add(splitContainer);
            Controls.Add(bottomPanel);

            LoadTree();
        }

        private void LoadTree()
        {
            _tree.Nodes.Clear();
            foreach (var summary in _provider.GetMachines())
            {
                var machine = _provider.GetMachine(summary.Id);
                if (machine is null) continue;

                var machineNode = new TreeNode(machine.Name) { Tag = machine };
                foreach (var material in machine.Materials)
                {
                    var matNode = new TreeNode(material.Name) { Tag = material };
                    foreach (var thickness in material.Thicknesses)
                    {
                        var thickNode = new TreeNode(thickness.Value.ToString("0.####")) { Tag = thickness };
                        matNode.Nodes.Add(thickNode);
                    }
                    machineNode.Nodes.Add(matNode);
                }
                _tree.Nodes.Add(machineNode);
            }

            if (_tree.Nodes.Count > 0)
                _tree.SelectedNode = _tree.Nodes[0];
        }

        private void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            _detailPanel.Controls.Clear();
            if (e.Node?.Tag is null) return;

            switch (e.Node.Tag)
            {
                case MachineConfig machine:
                    _currentMachine = machine;
                    ShowMachineDetails(machine);
                    break;
                case MaterialConfig material:
                    _currentMachine = e.Node.Parent?.Tag as MachineConfig;
                    ShowMaterialDetails(material);
                    break;
                case ThicknessConfig thickness:
                    _currentMachine = e.Node.Parent?.Parent?.Tag as MachineConfig;
                    ShowThicknessDetails(thickness);
                    break;
            }
        }

        private void ShowMachineDetails(MachineConfig machine)
        {
            var layout = CreateDetailLayout();
            var row = 0;

            AddField(layout, ref row, "Name:", CreateTextBox(machine.Name, v => machine.Name = v));
            AddField(layout, ref row, "Type:", CreateEnumCombo(machine.Type, v => machine.Type = v));
            AddField(layout, ref row, "Units:", CreateEnumCombo(machine.Units, v => machine.Units = v));

            _detailPanel.Controls.Add(layout);
        }

        private void ShowMaterialDetails(MaterialConfig material)
        {
            var layout = CreateDetailLayout();
            var row = 0;

            AddField(layout, ref row, "Name:", CreateTextBox(material.Name, v => material.Name = v));
            AddField(layout, ref row, "Grade:", CreateTextBox(material.Grade, v => material.Grade = v));
            AddField(layout, ref row, "Density:", CreateNumericBox(material.Density, v => material.Density = v, 4));

            _detailPanel.Controls.Add(layout);
        }

        private void ShowThicknessDetails(ThicknessConfig thickness)
        {
            var layout = CreateDetailLayout();
            var row = 0;

            AddField(layout, ref row, "Thickness:", CreateNumericBox(thickness.Value, v => thickness.Value = v, 4));
            AddField(layout, ref row, "Kerf:", CreateNumericBox(thickness.Kerf, v => thickness.Kerf = v, 4));
            AddField(layout, ref row, "Assist Gas:", CreateTextBox(thickness.AssistGas, v => thickness.AssistGas = v));

            AddSectionHeader(layout, ref row, "Lead In");
            AddField(layout, ref row, "Type:", CreateTextBox(thickness.LeadIn.Type, v => thickness.LeadIn.Type = v));
            AddField(layout, ref row, "Length:", CreateNumericBox(thickness.LeadIn.Length, v => thickness.LeadIn.Length = v, 4));
            AddField(layout, ref row, "Angle:", CreateNumericBox(thickness.LeadIn.Angle, v => thickness.LeadIn.Angle = v, 1));
            AddField(layout, ref row, "Radius:", CreateNumericBox(thickness.LeadIn.Radius, v => thickness.LeadIn.Radius = v, 4));

            AddSectionHeader(layout, ref row, "Lead Out");
            AddField(layout, ref row, "Type:", CreateTextBox(thickness.LeadOut.Type, v => thickness.LeadOut.Type = v));
            AddField(layout, ref row, "Length:", CreateNumericBox(thickness.LeadOut.Length, v => thickness.LeadOut.Length = v, 4));
            AddField(layout, ref row, "Angle:", CreateNumericBox(thickness.LeadOut.Angle, v => thickness.LeadOut.Angle = v, 1));
            AddField(layout, ref row, "Radius:", CreateNumericBox(thickness.LeadOut.Radius, v => thickness.LeadOut.Radius = v, 4));

            AddSectionHeader(layout, ref row, "Cut Off");
            AddField(layout, ref row, "Part Clearance:", CreateNumericBox(thickness.CutOff.PartClearance, v => thickness.CutOff.PartClearance = v, 4));
            AddField(layout, ref row, "Overtravel:", CreateNumericBox(thickness.CutOff.Overtravel, v => thickness.CutOff.Overtravel = v, 4));
            AddField(layout, ref row, "Min Segment:", CreateNumericBox(thickness.CutOff.MinSegmentLength, v => thickness.CutOff.MinSegmentLength = v, 4));
            AddField(layout, ref row, "Direction:", CreateTextBox(thickness.CutOff.Direction, v => thickness.CutOff.Direction = v));

            AddSectionHeader(layout, ref row, "Plate Sizes");
            var sizesText = string.Join(", ", thickness.PlateSizes);
            AddField(layout, ref row, "Sizes:", CreateTextBox(sizesText, v =>
            {
                thickness.PlateSizes = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }));

            _detailPanel.Controls.Add(layout);
        }

        private static TableLayoutPanel CreateDetailLayout()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        private static void AddField(TableLayoutPanel layout, ref int row, string label, Control control)
        {
            layout.RowCount = row + 1;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, row);
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(control, 1, row);
            row++;
        }

        private static void AddSectionHeader(TableLayoutPanel layout, ref int row, string text)
        {
            layout.RowCount = row + 1;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(Control.DefaultFont, FontStyle.Bold),
                Margin = new Padding(0, 12, 0, 4)
            };
            layout.SetColumnSpan(label, 2);
            layout.Controls.Add(label, 0, row);
            row++;
        }

        private static TextBox CreateTextBox(string value, Action<string> setter)
        {
            var textBox = new TextBox { Text = value };
            textBox.TextChanged += (s, e) => setter(textBox.Text);
            return textBox;
        }

        private static NumericUpDown CreateNumericBox(double value, Action<double> setter, int decimals)
        {
            var numeric = new NumericUpDown
            {
                DecimalPlaces = decimals,
                Minimum = 0,
                Maximum = 10000,
                Increment = (decimal)System.Math.Pow(10, -decimals),
                Value = (decimal)value
            };
            numeric.ValueChanged += (s, e) => setter((double)numeric.Value);
            return numeric;
        }

        private static ComboBox CreateEnumCombo<T>(T currentValue, Action<T> setter) where T : struct, Enum
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            combo.Items.AddRange(Enum.GetNames<T>().Cast<object>().ToArray());
            combo.SelectedItem = currentValue.ToString();
            combo.SelectedIndexChanged += (s, e) =>
            {
                if (Enum.TryParse<T>(combo.SelectedItem?.ToString(), out var val))
                    setter(val);
            };
            return combo;
        }

        private void Save_Click(object sender, EventArgs e)
        {
            foreach (TreeNode machineNode in _tree.Nodes)
            {
                if (machineNode.Tag is MachineConfig machine)
                    _provider.SaveMachine(machine);
            }
            MessageBox.Show("Machine configurations saved.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void AddMachine_Click(object sender, EventArgs e)
        {
            var machine = new MachineConfig { Name = "New Machine" };
            _provider.SaveMachine(machine);
            LoadTree();
        }

        private void RemoveMachine_Click(object sender, EventArgs e)
        {
            if (_tree.SelectedNode?.Tag is not MachineConfig machine) return;
            if (MessageBox.Show($"Delete machine '{machine.Name}'?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            _provider.DeleteMachine(machine.Id);
            LoadTree();
        }

        private void AddMaterial_Click(object sender, EventArgs e)
        {
            if (_currentMachine is null) return;

            _currentMachine.Materials.Add(new MaterialConfig { Name = "New Material" });
            _provider.SaveMachine(_currentMachine);
            LoadTree();
        }

        private void RemoveMaterial_Click(object sender, EventArgs e)
        {
            if (_tree.SelectedNode?.Tag is not MaterialConfig material) return;
            if (_currentMachine is null) return;

            _currentMachine.Materials.Remove(material);
            _provider.SaveMachine(_currentMachine);
            LoadTree();
        }

        private void AddThickness_Click(object sender, EventArgs e)
        {
            var material = _tree.SelectedNode?.Tag as MaterialConfig;
            if (material is null && _tree.SelectedNode?.Tag is ThicknessConfig)
                material = _tree.SelectedNode.Parent?.Tag as MaterialConfig;
            if (material is null || _currentMachine is null) return;

            material.Thicknesses.Add(new ThicknessConfig { Value = 0.250 });
            _provider.SaveMachine(_currentMachine);
            LoadTree();
        }

        private void RemoveThickness_Click(object sender, EventArgs e)
        {
            if (_tree.SelectedNode?.Tag is not ThicknessConfig thickness) return;
            var material = _tree.SelectedNode.Parent?.Tag as MaterialConfig;
            if (material is null || _currentMachine is null) return;

            material.Thicknesses.Remove(thickness);
            _provider.SaveMachine(_currentMachine);
            LoadTree();
        }

        private void Import_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                Title = "Import Machine Configuration"
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;

                try
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                    };
                    var machine = JsonSerializer.Deserialize<MachineConfig>(json, options);
                    if (machine is null) return;

                    machine.Id = Guid.NewGuid();
                    _provider.SaveMachine(machine);
                    LoadTree();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to import: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void Export_Click(object sender, EventArgs e)
        {
            if (_currentMachine is null) return;

            using (var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                FileName = $"{_currentMachine.Name}.json",
                Title = "Export Machine Configuration"
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;

                try
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                    };
                    var json = JsonSerializer.Serialize(_currentMachine, options);
                    File.WriteAllText(dialog.FileName, json);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
