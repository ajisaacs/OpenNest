using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using OpenNest.Data;
using OpenNest.Shapes;

namespace OpenNest.Forms
{
    public class MachineConfigForm : Form
    {
        private static readonly string[] LeadInTypes =
        {
            "None",
            "Line",
            "Arc",
            "Line + Arc",
            "Clean Hole",
            "Line + Line",
        };

        private static readonly string[] LeadOutTypes = { "None", "Line", "Arc" };

        private static readonly string[] AssistGasOptions = { "O2", "N2", "Air" };

        private static readonly string[] CutOffDirectionOptions = Enum.GetNames<CutDirection>();

        private readonly IDataProvider _provider;
        private readonly TreeView _tree;
        private readonly Panel _detailPanel;
        private readonly HashSet<Guid> _pendingDeletes = new();
        private readonly Button _saveButton;

        private MachineConfig _currentMachine;
        private TreeNode _currentMachineNode;
        private bool _dirty;

        public MachineConfigForm(IDataProvider provider)
        {
            _provider = provider;

            Text = "Machine Configuration";
            Size = new Size(1210, 640);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(1010, 440);

            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 500,
                FixedPanel = FixedPanel.Panel1,
            };

            _tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
            _tree.AfterSelect += Tree_AfterSelect;
            _tree.MouseDown += Tree_MouseDown;

            splitContainer.Panel1.Controls.Add(_tree);

            _detailPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            splitContainer.Panel2.Controls.Add(_detailPanel);

            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(4),
            };

            _saveButton = new Button { Text = "Save", AutoSize = true };
            _saveButton.Click += Save_Click;
            var importButton = new Button { Text = "Import...", AutoSize = true };
            importButton.Click += Import_Click;
            var exportButton = new Button { Text = "Export...", AutoSize = true };
            exportButton.Click += Export_Click;

            bottomPanel.Controls.AddRange(new Control[] { _saveButton, exportButton, importButton });

            Controls.Add(splitContainer);
            Controls.Add(bottomPanel);

            LoadTree();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_dirty)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Save before closing?",
                    "Unsaved Changes",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning
                );
                if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (result == DialogResult.Yes)
                    SaveAll();
            }

            base.OnFormClosing(e);
        }

        private void MarkDirty() => _dirty = true;

        private void LoadTree()
        {
            _tree.Nodes.Clear();
            _pendingDeletes.Clear();
            _dirty = false;

            foreach (var summary in _provider.GetMachines())
            {
                var machine = _provider.GetMachine(summary.Id);
                if (machine is null)
                    continue;

                _tree.Nodes.Add(BuildMachineNode(machine));
            }

            if (_tree.Nodes.Count > 0)
                _tree.SelectedNode = _tree.Nodes[0];
        }

        private static TreeNode BuildMachineNode(MachineConfig machine)
        {
            var machineNode = new TreeNode(machine.Name) { Tag = machine };
            foreach (var material in machine.Materials)
                machineNode.Nodes.Add(BuildMaterialNode(material));
            return machineNode;
        }

        private static TreeNode BuildMaterialNode(MaterialConfig material)
        {
            var matNode = new TreeNode(material.Name) { Tag = material };
            foreach (var thickness in material.Thicknesses)
                matNode.Nodes.Add(BuildThicknessNode(thickness));
            return matNode;
        }

        private static TreeNode BuildThicknessNode(ThicknessConfig thickness) =>
            new(ThicknessLabel(thickness)) { Tag = thickness };

        private static string ThicknessLabel(ThicknessConfig thickness) =>
            thickness.Value.ToString("0.####");

        private void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            _detailPanel.Controls.Clear();
            if (e.Node?.Tag is null)
                return;

            switch (e.Node.Tag)
            {
                case MachineConfig machine:
                    _currentMachine = machine;
                    _currentMachineNode = e.Node;
                    ShowMachineDetails(machine, e.Node);
                    break;
                case MaterialConfig material:
                    _currentMachineNode = e.Node.Parent;
                    _currentMachine = _currentMachineNode?.Tag as MachineConfig;
                    ShowMaterialDetails(material, e.Node);
                    break;
                case ThicknessConfig thickness:
                    _currentMachineNode = e.Node.Parent?.Parent;
                    _currentMachine = _currentMachineNode?.Tag as MachineConfig;
                    ShowThicknessDetails(thickness, e.Node);
                    break;
            }
        }

        private void Tree_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;

            var node = _tree.GetNodeAt(e.Location);
            if (node is not null)
                _tree.SelectedNode = node;

            BuildContextMenu(node).Show(_tree, e.Location);
        }

        private ContextMenuStrip BuildContextMenu(TreeNode node)
        {
            var menu = new ContextMenuStrip();

            switch (node?.Tag)
            {
                case MachineConfig:
                    menu.Items.Add("Add Material", null, AddMaterial_Click);
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Delete Machine", null, RemoveMachine_Click);
                    break;
                case MaterialConfig:
                    menu.Items.Add("Add Thickness", null, AddThickness_Click);
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Delete Material", null, RemoveMaterial_Click);
                    break;
                case ThicknessConfig:
                    menu.Items.Add("Delete Thickness", null, RemoveThickness_Click);
                    break;
                default:
                    menu.Items.Add("Add Machine", null, AddMachine_Click);
                    break;
            }

            return menu;
        }

        private void ShowMachineDetails(MachineConfig machine, TreeNode node)
        {
            var group = CreateGroup("General");
            var layout = CreateDetailLayout();
            var row = 0;

            AddField(
                layout,
                ref row,
                "Name:",
                CreateTextBox(
                    machine.Name,
                    v =>
                    {
                        machine.Name = v;
                        node.Text = v;
                        MarkDirty();
                    }
                )
            );
            AddField(
                layout,
                ref row,
                "Type:",
                CreateEnumCombo(machine.Type, v =>
                {
                    machine.Type = v;
                    MarkDirty();
                })
            );

            var unitsCombo = CreateEnumCombo(machine.Units, v =>
            {
                machine.Units = v;
                MarkDirty();
            });
            var previousUnits = machine.Units;
            unitsCombo.Tag = previousUnits;
            unitsCombo.SelectedIndexChanged += (s, e2) =>
            {
                var before = (UnitSystem)unitsCombo.Tag;
                if (!Enum.TryParse<UnitSystem>(unitsCombo.SelectedItem?.ToString(), out var after)
                    || after == before)
                    return;

                var confirm = MessageBox.Show(
                    "Changing units does not convert existing thickness, kerf and lead "
                        + "values — they will keep their current numbers. Continue?",
                    "Change Units",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );
                if (confirm != DialogResult.Yes)
                {
                    unitsCombo.SelectedItem = before.ToString();
                    return;
                }

                unitsCombo.Tag = after;
            };
            AddField(layout, ref row, "Units:", unitsCombo);

            group.Controls.Add(layout);
            _detailPanel.Controls.Add(group);
        }

        private void ShowMaterialDetails(MaterialConfig material, TreeNode node)
        {
            var group = CreateGroup("General");
            var layout = CreateDetailLayout();
            var row = 0;

            AddField(
                layout,
                ref row,
                "Name:",
                CreateTextBox(
                    material.Name,
                    v =>
                    {
                        material.Name = v;
                        node.Text = v;
                        MarkDirty();
                    }
                )
            );
            AddField(
                layout,
                ref row,
                "Grade:",
                CreateTextBox(material.Grade, v =>
                {
                    material.Grade = v;
                    MarkDirty();
                })
            );
            AddField(
                layout,
                ref row,
                "Density:",
                CreateNumericBox(material.Density, v =>
                {
                    material.Density = v;
                    MarkDirty();
                }, 4, 1000, 0.01m)
            );

            group.Controls.Add(layout);
            _detailPanel.Controls.Add(group);
        }

        private void ShowThicknessDetails(ThicknessConfig thickness, TreeNode node)
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(CreateTab("General", BuildThicknessGeneralTab(thickness, node)));
            tabs.TabPages.Add(
                CreateTab("Lead In", BuildLeadTab(thickness.LeadIn, LeadInTypes))
            );
            tabs.TabPages.Add(
                CreateTab("Lead Out", BuildLeadTab(thickness.LeadOut, LeadOutTypes))
            );
            tabs.TabPages.Add(CreateTab("Cut Off", BuildCutOffTab(thickness.CutOff)));
            tabs.TabPages.Add(CreateTab("Plate Sizes", BuildPlateSizesTab(thickness)));

            var host = new Panel { Dock = DockStyle.Fill, Height = 420 };
            host.Controls.Add(tabs);
            _detailPanel.Controls.Add(host);
        }

        private Control BuildThicknessGeneralTab(ThicknessConfig thickness, TreeNode node)
        {
            var layout = CreateDetailLayout();
            var row = 0;

            AddField(
                layout,
                ref row,
                "Thickness:",
                CreateLabeledNumeric(
                    thickness.Value,
                    v =>
                    {
                        thickness.Value = v;
                        node.Text = ThicknessLabel(thickness);
                        MarkDirty();
                    },
                    4,
                    LengthUnitSuffix,
                    100,
                    0.001m
                )
            );
            AddField(
                layout,
                ref row,
                "Kerf:",
                CreateLabeledNumeric(
                    thickness.Kerf,
                    v =>
                    {
                        thickness.Kerf = v;
                        MarkDirty();
                    },
                    4,
                    LengthUnitSuffix,
                    10,
                    0.001m
                )
            );
            AddField(
                layout,
                ref row,
                "Assist Gas:",
                CreateDropdown(
                    thickness.AssistGas,
                    AssistGasOptions,
                    v =>
                    {
                        thickness.AssistGas = v;
                        MarkDirty();
                    },
                    allowCustom: true
                )
            );

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            panel.Controls.Add(layout);
            return panel;
        }

        private Control BuildLeadTab(LeadConfig lead, string[] typeOptions)
        {
            var layout = CreateDetailLayout();
            var row = 0;

            var typeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = InputWidth,
            };
            typeCombo.Items.AddRange(typeOptions);
            typeCombo.SelectedItem = typeOptions.Contains(lead.Type) ? lead.Type : typeOptions[0];
            typeCombo.SelectedIndexChanged += (s, e) =>
            {
                lead.Type = typeCombo.SelectedItem?.ToString() ?? typeOptions[0];
                MarkDirty();
            };
            AddField(layout, ref row, "Type:", typeCombo);

            AddField(
                layout,
                ref row,
                "Length:",
                CreateLabeledNumeric(
                    lead.Length,
                    v =>
                    {
                        lead.Length = v;
                        MarkDirty();
                    },
                    4,
                    LengthUnitSuffix,
                    50,
                    0.001m
                )
            );
            AddField(
                layout,
                ref row,
                "Angle:",
                CreateLabeledNumeric(
                    lead.Angle,
                    v =>
                    {
                        lead.Angle = v;
                        MarkDirty();
                    },
                    1,
                    _ => "°",
                    360,
                    0.1m
                )
            );
            AddField(
                layout,
                ref row,
                "Radius:",
                CreateLabeledNumeric(
                    lead.Radius,
                    v =>
                    {
                        lead.Radius = v;
                        MarkDirty();
                    },
                    4,
                    LengthUnitSuffix,
                    50,
                    0.001m
                )
            );

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            panel.Controls.Add(layout);
            return panel;
        }

        private Control BuildCutOffTab(CutOffConfig cutOff)
        {
            var layout = CreateDetailLayout();
            var row = 0;

            AddField(
                layout,
                ref row,
                "Part Clearance:",
                CreateLabeledNumeric(
                    cutOff.PartClearance,
                    v =>
                    {
                        cutOff.PartClearance = v;
                        MarkDirty();
                    },
                    4,
                    LengthUnitSuffix,
                    10,
                    0.001m
                )
            );
            AddField(
                layout,
                ref row,
                "Overtravel:",
                CreateLabeledNumeric(
                    cutOff.Overtravel,
                    v =>
                    {
                        cutOff.Overtravel = v;
                        MarkDirty();
                    },
                    4,
                    LengthUnitSuffix,
                    10,
                    0.001m
                )
            );
            AddField(
                layout,
                ref row,
                "Min Segment:",
                CreateLabeledNumeric(
                    cutOff.MinSegmentLength,
                    v =>
                    {
                        cutOff.MinSegmentLength = v;
                        MarkDirty();
                    },
                    4,
                    LengthUnitSuffix,
                    50,
                    0.001m
                )
            );
            AddField(
                layout,
                ref row,
                "Direction:",
                CreateDropdown(
                    cutOff.Direction,
                    CutOffDirectionOptions,
                    v =>
                    {
                        cutOff.Direction = v;
                        MarkDirty();
                    }
                )
            );

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            panel.Controls.Add(layout);
            return panel;
        }

        private Control BuildPlateSizesTab(ThicknessConfig thickness)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };

            void RefreshList()
            {
                list.Items.Clear();
                foreach (var entry in PlateSizes.All)
                    list.Items.Add(entry.Label, thickness.PlateSizes.Contains(entry.Label));
                foreach (var custom in thickness.PlateSizes.Where(s =>
                    !PlateSizes.All.Any(e => string.Equals(e.Label, s, StringComparison.OrdinalIgnoreCase))
                ))
                    list.Items.Add(custom, true);
            }

            RefreshList();

            list.ItemCheck += (s, e) =>
            {
                BeginInvoke(() =>
                {
                    thickness.PlateSizes = list
                        .CheckedItems.Cast<object>()
                        .Select(i => i.ToString())
                        .ToList();
                    MarkDirty();
                });
            };

            var addPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Padding = new Padding(0, 6, 0, 0),
            };
            var customBox = new TextBox { Width = 120, PlaceholderText = "e.g. 50x100" };
            var addButton = new Button { Text = "Add Custom Size", AutoSize = true };
            addButton.Click += (s, e) =>
            {
                var label = customBox.Text.Trim();
                if (label.Length == 0)
                    return;
                if (!thickness.PlateSizes.Contains(label, StringComparer.OrdinalIgnoreCase))
                {
                    thickness.PlateSizes.Add(label);
                    MarkDirty();
                    RefreshList();
                }
                customBox.Clear();
            };
            addPanel.Controls.Add(customBox);
            addPanel.Controls.Add(addButton);

            panel.Controls.Add(list);
            panel.Controls.Add(addPanel);
            return panel;
        }

        private string LengthUnitSuffix(object _) =>
            _currentMachine?.Units == UnitSystem.Millimeters ? "mm" : "in";

        private static GroupBox CreateGroup(string title) =>
            new()
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(10, 16, 10, 10),
            };

        private static TabPage CreateTab(string text, Control content)
        {
            var page = new TabPage(text) { Padding = new Padding(0) };
            page.Controls.Add(content);
            return page;
        }

        private static TableLayoutPanel CreateDetailLayout()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(4),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        private static void AddField(
            TableLayoutPanel layout,
            ref int row,
            string label,
            Control control
        )
        {
            layout.RowCount = row + 1;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(
                new Label
                {
                    Text = label,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 8, 10, 0),
                },
                0,
                row
            );
            control.Anchor = AnchorStyles.Left;
            control.Margin = new Padding(0, 4, 0, 4);
            layout.Controls.Add(control, 1, row);
            row++;
        }

        private const int InputWidth = 180;
        private const int NumericWidth = 90;

        private static TextBox CreateTextBox(string value, Action<string> setter)
        {
            var textBox = new TextBox { Text = value, Width = InputWidth };
            textBox.TextChanged += (s, e) => setter(textBox.Text);
            return textBox;
        }

        private static ComboBox CreateDropdown(
            string value,
            IReadOnlyList<string> options,
            Action<string> setter,
            bool allowCustom = false
        )
        {
            var combo = new ComboBox
            {
                DropDownStyle = allowCustom ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
                Width = InputWidth,
            };
            combo.Items.AddRange(options.Cast<object>().ToArray());
            combo.Text = value;
            if (!allowCustom && !options.Contains(value) && options.Count > 0)
                combo.SelectedItem = options[0];
            combo.SelectedIndexChanged += (s, e) => setter(combo.Text);
            combo.TextChanged += (s, e) => setter(combo.Text);
            return combo;
        }

        private static NumericUpDown CreateNumericBox(
            double value,
            Action<double> setter,
            int decimals,
            decimal max,
            decimal increment
        )
        {
            var numeric = new NumericUpDown
            {
                DecimalPlaces = decimals,
                Minimum = 0,
                Maximum = max,
                Increment = increment,
                Value = System.Math.Clamp((decimal)value, 0, max),
                Width = NumericWidth,
            };
            numeric.ValueChanged += (s, e) => setter((double)numeric.Value);
            return numeric;
        }

        private Control CreateLabeledNumeric(
            double value,
            Action<double> setter,
            int decimals,
            Func<object, string> suffix,
            decimal max,
            decimal increment
        )
        {
            var row = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Anchor = AnchorStyles.Left,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var numeric = CreateNumericBox(value, setter, decimals, max, increment);

            var suffixLabel = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(6, 6, 0, 0),
                Text = suffix(null),
            };

            row.Controls.Add(numeric, 0, 0);
            row.Controls.Add(suffixLabel, 1, 0);
            return row;
        }

        private static ComboBox CreateEnumCombo<T>(T currentValue, Action<T> setter)
            where T : struct, Enum
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = InputWidth };
            combo.Items.AddRange(Enum.GetNames<T>().Cast<object>().ToArray());
            combo.SelectedItem = currentValue.ToString();
            combo.SelectedIndexChanged += (s, e) =>
            {
                if (Enum.TryParse<T>(combo.SelectedItem?.ToString(), out var val))
                    setter(val);
            };
            return combo;
        }

        private void Save_Click(object sender, EventArgs e) => SaveAll();

        private void SaveAll()
        {
            foreach (var id in _pendingDeletes)
                _provider.DeleteMachine(id);
            _pendingDeletes.Clear();

            foreach (TreeNode machineNode in _tree.Nodes)
            {
                if (machineNode.Tag is MachineConfig machine)
                    _provider.SaveMachine(machine);
            }

            _dirty = false;
            MessageBox.Show(
                "Machine configurations saved.",
                "Saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void AddMachine_Click(object sender, EventArgs e)
        {
            var machine = new MachineConfig { Name = "New Machine" };
            var node = BuildMachineNode(machine);
            _tree.Nodes.Add(node);
            _tree.SelectedNode = node;
            MarkDirty();
        }

        private void RemoveMachine_Click(object sender, EventArgs e)
        {
            if (_tree.SelectedNode?.Tag is not MachineConfig machine)
                return;
            if (
                MessageBox.Show(
                    $"Delete machine '{machine.Name}'?",
                    "Confirm",
                    MessageBoxButtons.YesNo
                ) != DialogResult.Yes
            )
                return;

            _pendingDeletes.Add(machine.Id);
            _tree.Nodes.Remove(_tree.SelectedNode);
            MarkDirty();
        }

        private void AddMaterial_Click(object sender, EventArgs e)
        {
            if (_currentMachine is null || _currentMachineNode is null)
                return;

            var material = new MaterialConfig { Name = "New Material" };
            _currentMachine.Materials.Add(material);
            var node = BuildMaterialNode(material);
            _currentMachineNode.Nodes.Add(node);
            _currentMachineNode.Expand();
            _tree.SelectedNode = node;
            MarkDirty();
        }

        private void RemoveMaterial_Click(object sender, EventArgs e)
        {
            if (_tree.SelectedNode?.Tag is not MaterialConfig material)
                return;
            if (_currentMachine is null)
                return;
            if (
                MessageBox.Show(
                    $"Delete material '{material.Name}'?",
                    "Confirm",
                    MessageBoxButtons.YesNo
                ) != DialogResult.Yes
            )
                return;

            _currentMachine.Materials.Remove(material);
            _tree.Nodes.Remove(_tree.SelectedNode);
            MarkDirty();
        }

        private void AddThickness_Click(object sender, EventArgs e)
        {
            var materialNode = _tree.SelectedNode;
            var material = materialNode?.Tag as MaterialConfig;
            if (material is null && materialNode?.Tag is ThicknessConfig)
                materialNode = materialNode.Parent;
            material = materialNode?.Tag as MaterialConfig;
            if (material is null || _currentMachine is null)
                return;

            var thickness = new ThicknessConfig { Value = 0.250 };
            material.Thicknesses.Add(thickness);
            var node = BuildThicknessNode(thickness);
            materialNode.Nodes.Add(node);
            materialNode.Expand();
            _tree.SelectedNode = node;
            MarkDirty();
        }

        private void RemoveThickness_Click(object sender, EventArgs e)
        {
            if (_tree.SelectedNode?.Tag is not ThicknessConfig thickness)
                return;
            var material = _tree.SelectedNode.Parent?.Tag as MaterialConfig;
            if (material is null || _currentMachine is null)
                return;
            if (
                MessageBox.Show(
                    $"Delete thickness '{ThicknessLabel(thickness)}'?",
                    "Confirm",
                    MessageBoxButtons.YesNo
                ) != DialogResult.Yes
            )
                return;

            material.Thicknesses.Remove(thickness);
            _tree.Nodes.Remove(_tree.SelectedNode);
            MarkDirty();
        }

        private void Import_Click(object sender, EventArgs e)
        {
            using (
                var dialog = new OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    Title = "Import Machine Configuration",
                }
            )
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
                    };
                    var machine = JsonSerializer.Deserialize<MachineConfig>(json, options);
                    if (machine is null)
                        return;

                    machine.Id = Guid.NewGuid();
                    var node = BuildMachineNode(machine);
                    _tree.Nodes.Add(node);
                    _tree.SelectedNode = node;
                    MarkDirty();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to import: {ex.Message}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
        }

        private void Export_Click(object sender, EventArgs e)
        {
            if (_currentMachine is null)
                return;

            using (
                var dialog = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    FileName = $"{_currentMachine.Name}.json",
                    Title = "Export Machine Configuration",
                }
            )
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
                    };
                    var json = JsonSerializer.Serialize(_currentMachine, options);
                    File.WriteAllText(dialog.FileName, json);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to export: {ex.Message}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
        }
    }
}
