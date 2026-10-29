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
using OpenNest.Support;

namespace OpenNest.Forms
{
    public partial class MachineConfigForm : Form
    {
        private static readonly string[] AssistGasOptions = { "O2", "N2", "Air" };

        private static readonly string[] CutOffDirectionOptions = Enum.GetNames<CutDirection>();

        private readonly IDataProvider _provider;
        private readonly HashSet<Guid> _pendingDeletes = new();

        private MachineConfig _currentMachine;
        private TreeNode _currentMachineNode;
        private bool _dirty;

        // The tab last shown for each kind of node, so moving between thicknesses (or machines)
        // keeps the same tab open.
        private string _machineTab;
        private string _thicknessTab;

        private MachineConfigForm()
        {
            InitializeComponent();
            _tree.AfterSelect += Tree_AfterSelect;
            _tree.MouseDown += Tree_MouseDown;
        }

        public MachineConfigForm(IDataProvider provider)
            : this()
        {
            _provider = provider;
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

            _tree.ExpandAll();
            if (_tree.Nodes.Count > 0)
                _tree.SelectedNode = _tree.Nodes[0];
        }

        private static TreeNode BuildMachineNode(MachineConfig machine)
        {
            var machineNode = new TreeNode(machine.Name) { Tag = machine };
            foreach (var group in machine.Materials.GroupBy(m => SharedListNames.Key(m.Name)))
                machineNode.Nodes.Add(BuildMaterialGroupNode(group.First().Name, group));
            return machineNode;
        }

        private static TreeNode BuildMaterialGroupNode(
            string name,
            IEnumerable<MaterialConfig> grades
        )
        {
            var groupNode = new TreeNode(name) { Tag = name };
            foreach (var grade in grades)
                groupNode.Nodes.Add(BuildGradeNode(grade));
            return groupNode;
        }

        private static TreeNode BuildGradeNode(MaterialConfig material)
        {
            var node = new TreeNode(GradeLabel(material)) { Tag = material };
            foreach (var thickness in material.Thicknesses)
                node.Nodes.Add(BuildThicknessNode(thickness));
            return node;
        }

        private static string GradeLabel(MaterialConfig material) =>
            string.IsNullOrWhiteSpace(material.Grade) ? "(no grade)" : material.Grade;

        private static TreeNode BuildThicknessNode(ThicknessConfig thickness) =>
            new(ThicknessLabel(thickness)) { Tag = thickness };

        private static string ThicknessLabel(ThicknessConfig thickness)
        {
            var value = thickness.Value.ToString("0.####");
            return string.IsNullOrWhiteSpace(thickness.Name) ? value : $"{value} ({thickness.Name.Trim()})";
        }

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
                case string groupName:
                    _currentMachineNode = e.Node.Parent;
                    _currentMachine = _currentMachineNode?.Tag as MachineConfig;
                    ShowMaterialGroupDetails(groupName);
                    break;
                case MaterialConfig material:
                    _currentMachineNode = e.Node.Parent?.Parent;
                    _currentMachine = _currentMachineNode?.Tag as MachineConfig;
                    ShowMaterialDetails(material, e.Node);
                    break;
                case ThicknessConfig thickness:
                    _currentMachineNode = e.Node.Parent?.Parent?.Parent;
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
                case string:
                    menu.Items.Add("Add Grade", null, AddGrade_Click);
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Delete Material", null, RemoveMaterial_Click);
                    break;
                case MaterialConfig:
                    menu.Items.Add("Add Thickness", null, AddThickness_Click);
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Delete Grade", null, RemoveGrade_Click);
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
                    "Changing units does not convert existing thickness, kerf, lead, "
                        + "motion and clamp values — they will keep their current numbers. Continue?",
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
            var general = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            // Docked to the top, so the last added sits first.
            general.Controls.Add(BuildMotionGroup(machine));
            general.Controls.Add(group);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(CreateTab("General", general));
            tabs.TabPages.Add(CreateTab("Clamps", BuildClampsTab(machine)));
            tabs.TabPages.Add(CreateTab("Support Bed", BuildSupportBedTab(machine)));
            RememberTab(tabs, _machineTab, t => _machineTab = t);

            var host = new Panel { Dock = DockStyle.Fill, Height = 420 };
            host.Controls.Add(tabs);
            _detailPanel.Controls.Add(host);
        }

        // The machine's edge-clamp rail, which new nests assigned the machine copy. The first edit
        // stores the shown values (the default clamp size, off, when it has none) with the change.
        private Control BuildClampsTab(MachineConfig machine)
        {
            var shown = machine.GetClampSettings();
            var editor = new ClampRailEditor(null, shown.Units, shown.Enabled);
            editor.LoadMachineDefaults(shown);

            MachineClamps Clamps() => machine.Clamps ??= new MachineClamps
            {
                Enabled = shown.Enabled,
                Width = shown.Width,
                Depth = shown.Depth,
                RemovalMinutes = shown.RemovalMinutes,
                Clearance = shown.Clearance,
            };

            void Set(Action<MachineClamps> apply)
            {
                var clamps = Clamps();
                apply(clamps);
                var edges = new ClampSettings();
                edges.SetClamps(editor.Clamps);
                clamps.Centers = edges.Centers.ToList();
                clamps.FarSideCenters = edges.FarSideCenters.ToList();
                clamps.OriginEndCenters = edges.OriginEndCenters.ToList();
                clamps.FarEndCenters = edges.FarEndCenters.ToList();
                MarkDirty();
            }

            var layout = CreateDetailLayout();
            var row = 0;
            var enabledBox = new CheckBox
            {
                Text = "Give new nests these clamps",
                AutoSize = true,
                Checked = shown.Enabled,
            };
            enabledBox.CheckedChanged += (s, e) => Set(c => c.Enabled = enabledBox.Checked);
            AddField(layout, ref row, "New nests:", enabledBox);
            AddField(layout, ref row, "Clamp width:",
                CreateLabeledNumeric(shown.Width, v => Set(c => c.Width = v), 4, LengthUnitSuffix, 1000, 0.125m));
            AddField(layout, ref row, "Clamp depth:",
                CreateLabeledNumeric(shown.Depth, v => Set(c => c.Depth = v), 4, LengthUnitSuffix, 1000, 0.125m));
            AddField(layout, ref row, "Removal time:",
                CreateLabeledNumeric(shown.RemovalMinutes, v => Set(c => c.RemovalMinutes = v), 2, _ => "min per clamp", 1000, 0.5m));
            AddField(layout, ref row, "Clearance:", CreateClampClearance(shown.Clearance, Set));

            var centersGroup = new GroupBox
            {
                Text = $"Clamp centers, measured from the plate origin ({LengthUnitSuffix(null)})",
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
            };
            var centersList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            var entry = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false };
            var edgeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            foreach (var edge in ClampEdgeText.All)
                edgeBox.Items.Add(ClampEdgeText.Name(edge));
            edgeBox.SelectedIndex = 0;
            var centerBox = CreateNumericBox(0, _ => { }, 4, 100000, 0.125m);
            var addButton = new Button { Text = "Add", AutoSize = true };
            var removeButton = new Button { Text = "Remove", AutoSize = true, Enabled = false };
            entry.Controls.Add(edgeBox);
            entry.Controls.Add(new Label { Text = "Center:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            entry.Controls.Add(centerBox);
            entry.Controls.Add(addButton);
            entry.Controls.Add(removeButton);
            centersGroup.Controls.Add(centersList);
            centersGroup.Controls.Add(entry);

            void RefreshCenters()
            {
                centersList.BeginUpdate();
                centersList.Items.Clear();
                foreach (var clamp in editor.Clamps)
                    centersList.Items.Add(
                        $"{ClampEdgeText.Name(clamp.Edge)}:  {clamp.Center.ToString("0.####", System.Globalization.CultureInfo.CurrentCulture)}");
                centersList.EndUpdate();
                removeButton.Enabled = centersList.SelectedIndex >= 0;
            }

            addButton.Click += (s, e) =>
            {
                var clamp = new RailClamp(ClampEdgeText.All[edgeBox.SelectedIndex], (double)centerBox.Value);
                if (!editor.TryAddClamp(clamp, out var error))
                {
                    MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Set(_ => { });
                RefreshCenters();
            };
            removeButton.Click += (s, e) =>
            {
                var index = centersList.SelectedIndex;
                editor.RemoveCenterAt(index);
                Set(_ => { });
                RefreshCenters();
                if (centersList.Items.Count > 0)
                    centersList.SelectedIndex = System.Math.Min(index, centersList.Items.Count - 1);
            };
            centersList.SelectedIndexChanged += (s, e) => removeButton.Enabled = centersList.SelectedIndex >= 0;
            RefreshCenters();

            var note = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(4, 0, 0, 8),
                Text = "New nests assigned this machine copy these clamps. Changing them here never "
                    + "moves the clamps of existing nests; use Load Machine Defaults in Nest > Clamps for that.",
            };

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            panel.Controls.Add(centersGroup);
            panel.Controls.Add(note);
            panel.Controls.Add(layout);
            return panel;
        }

        // The machine's support bed, which new nests assigned the machine receive. A machine without
        // one shows the legacy shared support-grid.json; the first edit stores the shown values.
        private Control BuildSupportBedTab(MachineConfig machine)
        {
            var shown = MachineSettings.LoadSupportGridSettings(machine)
                .ConvertedTo(MachineClamps.UnitsOf(machine.Units));
            string Suffix(object _) => shown.Units == Units.Millimeters ? "mm" : "in";

            void Set(Action<SupportGridSettings> apply)
            {
                machine.SupportGrid ??= shown;
                apply(machine.SupportGrid);
                MarkDirty();
            }

            var layout = CreateDetailLayout();
            var row = 0;
            var enabledBox = new CheckBox
            {
                Text = "Give new nests this support bed",
                AutoSize = true,
                Checked = shown.Enabled,
            };
            enabledBox.CheckedChanged += (s, e) => Set(g => g.Enabled = enabledBox.Checked);
            AddField(layout, ref row, "New nests:", enabledBox);

            var kindCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = InputWidth };
            kindCombo.Items.AddRange(new object[] { "Grid of points", "Slats" });
            kindCombo.SelectedIndex = shown.UseSlats ? 1 : 0;
            AddField(layout, ref row, "Bed:", kindCombo);

            var gridFields = new List<Control>
            {
                AddSupportField(layout, ref row, "Pitch along X:",
                    CreateLabeledNumeric(shown.PitchX, v => Set(g => g.PitchX = v), 4, Suffix, 100000, 0.125m, 0.0001m)),
                AddSupportField(layout, ref row, "Pitch along Y:",
                    CreateLabeledNumeric(shown.PitchY, v => Set(g => g.PitchY = v), 4, Suffix, 100000, 0.125m, 0.0001m)),
                AddSupportField(layout, ref row, "First point, X from the origin:",
                    CreateLabeledNumeric(shown.OffsetX, v => Set(g => g.OffsetX = v), 4, Suffix, 100000, 0.125m, -100000m)),
                AddSupportField(layout, ref row, "First point, Y from the origin:",
                    CreateLabeledNumeric(shown.OffsetY, v => Set(g => g.OffsetY = v), 4, Suffix, 100000, 0.125m, -100000m)),
            };

            var axisCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = InputWidth };
            axisCombo.Items.AddRange(new object[] { "Along X (the full length)", "Along Y (the full width)" });
            axisCombo.SelectedIndex = shown.SlatAxis == SlatAxis.X ? 0 : 1;
            axisCombo.SelectedIndexChanged += (s, e) =>
                Set(g => g.SlatAxis = axisCombo.SelectedIndex == 0 ? SlatAxis.X : SlatAxis.Y);
            var slatFields = new List<Control>
            {
                AddSupportField(layout, ref row, "Slats run:", axisCombo),
                AddSupportField(layout, ref row, "Pitch between slats:",
                    CreateLabeledNumeric(shown.SlatPitch, v => Set(g => g.SlatPitch = v), 4, Suffix, 100000, 0.125m, 0.0001m)),
                AddSupportField(layout, ref row, "Slat thickness:",
                    CreateLabeledNumeric(shown.SlatThickness, v => Set(g => g.SlatThickness = v), 4, Suffix, 100000, 0.125m)),
                AddSupportField(layout, ref row, "First slat's center from the origin:",
                    CreateLabeledNumeric(shown.SlatOffset, v => Set(g => g.SlatOffset = v), 4, Suffix, 100000, 0.125m, -100000m)),
            };

            AddField(layout, ref row, "Registration tolerance:",
                CreateLabeledNumeric(shown.RegistrationTolerance, v => Set(g => g.RegistrationTolerance = v), 4, Suffix, 100000, 0.125m));
            AddField(layout, ref row, "Stability margin:",
                CreateLabeledNumeric(shown.StabilityMargin, v => Set(g => g.StabilityMargin = v), 4, Suffix, 100000, 0.125m));

            void UpdateEnabled()
            {
                foreach (var field in gridFields)
                    field.Enabled = kindCombo.SelectedIndex == 0;
                foreach (var field in slatFields)
                    field.Enabled = kindCombo.SelectedIndex == 1;
            }

            kindCombo.SelectedIndexChanged += (s, e) =>
            {
                Set(g => g.UseSlats = kindCombo.SelectedIndex == 1);
                UpdateEnabled();
            };
            UpdateEnabled();

            var note = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(4, 0, 0, 8),
                Text = "What cut parts rest on: a grid of contact points, one every pitch along X and Y, or "
                    + "slats running the full length or width of the table, one every pitch. New nests "
                    + "assigned this machine get this bed. Changing it here never changes the bed of "
                    + "existing nests; use Load Machine Defaults in Nest > Support Bed for that.",
            };

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            panel.Controls.Add(layout);
            panel.Controls.Add(note);
            return panel;
        }

        // Adds the row and returns its input, so the caller can enable it with the bed layout.
        private static Control AddSupportField(TableLayoutPanel layout, ref int row, string label, Control control)
        {
            AddField(layout, ref row, label, control);
            return control;
        }

        private GroupBox BuildMotionGroup(MachineConfig machine)
        {
            var group = CreateGroup("Motion (cut time estimates)");
            var layout = CreateDetailLayout();
            var row = 0;
            var shown = machine.GetMotionOrPlaceholder();
            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                ForeColor = SystemColors.GrayText,
            };

            void UpdateNote() =>
                note.Text = machine.Motion == null
                    ? "Placeholder values. Enter the controller's acceleration and corner settings."
                    : "Used by Simulate Cutting and Calculate Cut Time.";

            // The first edit stores the placeholder values along with the change.
            void Set(Action<MotionConfig> apply)
            {
                machine.Motion ??= new MotionConfig
                {
                    CutAcceleration = shown.CutAcceleration,
                    RapidAcceleration = shown.RapidAcceleration,
                    CornerDeviation = shown.CornerDeviation,
                };
                apply(machine.Motion);
                UpdateNote();
                MarkDirty();
            }

            string AccelerationSuffix(object _) => LengthUnitSuffix(null) + "/s²";

            AddField(layout, ref row, "Cut acceleration:",
                CreateLabeledNumeric(shown.CutAcceleration, v => Set(m => m.CutAcceleration = v), 0, AccelerationSuffix, 100000, 10));
            AddField(layout, ref row, "Rapid acceleration:",
                CreateLabeledNumeric(shown.RapidAcceleration, v => Set(m => m.RapidAcceleration = v), 0, AccelerationSuffix, 100000, 10));
            AddField(layout, ref row, "Corner deviation:",
                CreateLabeledNumeric(shown.CornerDeviation, v => Set(m => m.CornerDeviation = v), 4, LengthUnitSuffix, 10, 0.0005m));

            UpdateNote();
            layout.RowCount = row + 1;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(note, 0, row);
            layout.SetColumnSpan(note, 2);

            group.Controls.Add(layout);
            return group;
        }

        private void ShowMaterialGroupDetails(string groupName)
        {
            var label = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(10, 16, 10, 10),
                Text = $"Select a grade of '{groupName}' below, or right-click to add one.",
            };
            _detailPanel.Controls.Add(label);
        }

        private void ShowMaterialDetails(MaterialConfig material, TreeNode node)
        {
            var group = CreateGroup("General");
            var layout = CreateDetailLayout();
            var row = 0;

            var nameBox = CreateTextBox(
                material.Name,
                v =>
                {
                    material.Name = v;
                    MarkDirty();
                }
            );
            nameBox.Leave += (s, e) => RelocateGradeNode(node, material.Name);
            AddField(layout, ref row, "Name:", nameBox);
            AddField(
                layout,
                ref row,
                "Grade:",
                CreateTextBox(material.Grade, v =>
                {
                    material.Grade = v;
                    node.Text = GradeLabel(material);
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
            tabs.TabPages.Add(CreateTab("Outside Cuts", BuildOutsideCutsTab(thickness)));
            tabs.TabPages.Add(CreateTab("Inside Cuts", BuildInsideCutsTab(thickness)));
            tabs.TabPages.Add(CreateTab("Cut Off", BuildCutOffTab(thickness.CutOff)));
            tabs.TabPages.Add(CreateTab("Plate Sizes", BuildPlateSizesTab(thickness)));
            RememberTab(tabs, _thicknessTab, t => _thicknessTab = t);

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
            var nameBox = CreateTextBox(
                thickness.Name,
                v =>
                {
                    thickness.Name = v;
                    node.Text = ThicknessLabel(thickness);
                    MarkDirty();
                }
            );
            nameBox.PlaceholderText = "e.g. 16 ga";
            AddField(layout, ref row, "Name / Gauge:", nameBox);
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

        private Control BuildOutsideCutsTab(ThicknessConfig thickness)
        {
            var editor = CreateLeadsEditor(CNC.CuttingStrategy.ContourType.External);
            // Clean Hole is for holes only, so outside cuts do not offer it.
            editor.AllowCleanHole = false;
            ShowLeads(editor, thickness, thickness.LeadIn, thickness.LeadOut);
            editor.LeadInChanged += (s, e) =>
            {
                thickness.LeadIn = MachineCuttingDefaults.ToConfig(editor.LeadIn, thickness.LeadIn);
                LeadsEdited(editor, thickness);
            };
            editor.LeadOutChanged += (s, e) =>
            {
                thickness.LeadOut = MachineCuttingDefaults.ToConfig(editor.LeadOut, thickness.LeadOut);
                LeadsEdited(editor, thickness);
            };
            editor.VisibleChanged += (s, e) =>
            {
                if (editor.Visible)
                    ShowLeads(editor, thickness, thickness.LeadIn, thickness.LeadOut);
            };

            var page = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            page.Controls.Add(editor);
            page.Controls.Add(CreateTabNote("Part perimeters. Inside cuts use these too unless given their own."));
            return page;
        }

        // Inside cuts (cutouts and holes) follow the outside cuts until they get leads of their
        // own, which start as a copy of the outside ones.
        private Control BuildInsideCutsTab(ThicknessConfig thickness)
        {
            var editor = CreateLeadsEditor(CNC.CuttingStrategy.ContourType.Internal);
            var sameBox = new CheckBox
            {
                Text = "Same as outside cuts",
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(10, 0, 0, 0),
                Checked = thickness.InsideLeadIn == null && thickness.InsideLeadOut == null,
            };

            void Show()
            {
                ShowLeads(editor, thickness, thickness.InsideLeadIn ?? thickness.LeadIn,
                    thickness.InsideLeadOut ?? thickness.LeadOut);
                editor.FieldsEnabled = !sameBox.Checked;
            }

            sameBox.CheckedChanged += (s, e) =>
            {
                thickness.InsideLeadIn = sameBox.Checked ? null : Copy(thickness.LeadIn);
                thickness.InsideLeadOut = sameBox.Checked ? null : Copy(thickness.LeadOut);
                MarkDirty();
                Show();
            };
            editor.LeadInChanged += (s, e) =>
            {
                thickness.InsideLeadIn = MachineCuttingDefaults.ToConfig(editor.LeadIn, thickness.InsideLeadIn);
                LeadsEdited(editor, thickness);
            };
            editor.LeadOutChanged += (s, e) =>
            {
                thickness.InsideLeadOut = MachineCuttingDefaults.ToConfig(editor.LeadOut, thickness.InsideLeadOut);
                LeadsEdited(editor, thickness);
            };
            // Shown again on each visit, so following the outside cuts shows their current values.
            editor.VisibleChanged += (s, e) =>
            {
                if (editor.Visible)
                    Show();
            };
            Show();

            var page = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            page.Controls.Add(editor);
            page.Controls.Add(sameBox);
            page.Controls.Add(CreateTabNote("Cutouts and holes, full circles included."));
            return page;
        }

        // The machine holds no arc sweep (leads are half circles) and takes a clean hole's kerf
        // from the thickness; Line + Line is never applied from it, so it is not offered.
        private Controls.LeadsEditor CreateLeadsEditor(CNC.CuttingStrategy.ContourType emphasis)
        {
            return new Controls.LeadsEditor
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 0, 10, 10),
                Emphasis = emphasis,
                AllowLineLine = false,
                ShowArcSweep = false,
                ShowCleanHoleKerf = false,
                LengthUnit = LengthUnitSuffix(null),
            };
        }

        private static void ShowLeads(Controls.LeadsEditor editor, ThicknessConfig thickness, LeadConfig leadIn, LeadConfig leadOut)
        {
            editor.LeadIn = MachineCuttingDefaults.LeadInOf(leadIn, thickness.Kerf) ?? new CNC.CuttingStrategy.NoLeadIn();
            editor.LeadOut = MachineCuttingDefaults.LeadOutOf(leadOut);
            editor.PreviewParameters = MachineCuttingDefaults.LeadsOf(thickness);
        }

        private void LeadsEdited(Controls.LeadsEditor editor, ThicknessConfig thickness)
        {
            editor.PreviewParameters = MachineCuttingDefaults.LeadsOf(thickness);
            MarkDirty();
        }

        private static LeadConfig Copy(LeadConfig lead) =>
            new() { Type = lead.Type, Length = lead.Length, Angle = lead.Angle, Radius = lead.Radius };

        private static Label CreateTabNote(string text) =>
            new()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(10, 8, 0, 6),
                Text = text,
            };

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

        private static void RememberTab(TabControl tabs, string lastTab, Action<string> store)
        {
            var page = tabs.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == lastTab);
            if (page is not null)
                tabs.SelectedTab = page;
            tabs.SelectedIndexChanged += (s, e) => store(tabs.SelectedTab?.Text);
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
            decimal increment,
            decimal min = 0
        )
        {
            var numeric = new NumericUpDown
            {
                DecimalPlaces = decimals,
                Minimum = min,
                Maximum = max,
                Increment = increment,
                Value = System.Math.Clamp((decimal)value, min, max),
                Width = NumericWidth,
            };
            numeric.ValueChanged += (s, e) => setter((double)numeric.Value);
            return numeric;
        }

        // Unchecked keeps each sheet's bottom edge spacing (a null clearance), as before clamps
        // had their own.
        private Control CreateClampClearance(double? clearance, Action<Action<MachineClamps>> set)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left };
            var ownBox = new CheckBox
            {
                Text = "Own clearance from parts",
                AutoSize = true,
                Checked = clearance.HasValue,
                Margin = new Padding(0, 5, 6, 0),
            };
            var numeric = CreateNumericBox(clearance ?? 0, v => set(c => c.Clearance = ownBox.Checked ? v : null),
                4, 1000, 0.0625m);
            numeric.Enabled = ownBox.Checked;
            ownBox.CheckedChanged += (s, e) =>
            {
                numeric.Enabled = ownBox.Checked;
                set(c => c.Clearance = ownBox.Checked ? (double)numeric.Value : null);
            };
            row.Controls.Add(ownBox);
            row.Controls.Add(numeric);
            row.Controls.Add(new Label
            {
                AutoSize = true,
                Margin = new Padding(6, 6, 0, 0),
                Text = LengthUnitSuffix(null),
            });
            return row;
        }

        private Control CreateLabeledNumeric(
            double value,
            Action<double> setter,
            int decimals,
            Func<object, string> suffix,
            decimal max,
            decimal increment,
            decimal min = 0
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

            var numeric = CreateNumericBox(value, setter, decimals, max, increment, min);

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
            try
            {
                foreach (var id in _pendingDeletes.ToList())
                {
                    _provider.DeleteMachine(id);
                    _pendingDeletes.Remove(id);
                }

                foreach (TreeNode machineNode in _tree.Nodes)
                {
                    if (machineNode.Tag is MachineConfig machine)
                        _provider.SaveMachine(machine);
                }
            }
            catch (IOException ex)
            {
                // Shared machines: the server is unreachable or someone else saved first.
                // Everything not yet saved stays pending.
                MessageBox.Show(
                    this,
                    "The machine configurations could not all be saved.\n\n" + ex.Message
                        + (ex is SharedSettingSaveConflictException
                            ? "\n\nClose this window without saving and open it again to see their changes."
                            : ""),
                    "Save",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
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

            var material = new MaterialConfig { Name = "New Material", Grade = "New Grade" };
            _currentMachine.Materials.Add(material);
            var groupNode = new TreeNode(material.Name) { Tag = material.Name };
            var gradeNode = BuildGradeNode(material);
            groupNode.Nodes.Add(gradeNode);
            _currentMachineNode.Nodes.Add(groupNode);
            _currentMachineNode.Expand();
            groupNode.Expand();
            _tree.SelectedNode = gradeNode;
            MarkDirty();
        }

        private void AddGrade_Click(object sender, EventArgs e)
        {
            if (_tree.SelectedNode?.Tag is not string groupName || _currentMachine is null)
                return;

            var groupNode = _tree.SelectedNode;
            var material = new MaterialConfig { Name = groupName, Grade = "New Grade" };
            _currentMachine.Materials.Add(material);
            var gradeNode = BuildGradeNode(material);
            groupNode.Nodes.Add(gradeNode);
            groupNode.Expand();
            _tree.SelectedNode = gradeNode;
            MarkDirty();
        }

        private void RemoveMaterial_Click(object sender, EventArgs e)
        {
            if (_tree.SelectedNode?.Tag is not string groupName || _currentMachine is null)
                return;
            if (
                MessageBox.Show(
                    $"Delete material '{groupName}' and all of its grades?",
                    "Confirm",
                    MessageBoxButtons.YesNo
                ) != DialogResult.Yes
            )
                return;

            var key = SharedListNames.Key(groupName);
            _currentMachine.Materials.RemoveAll(m => SharedListNames.Key(m.Name) == key);
            _tree.Nodes.Remove(_tree.SelectedNode);
            MarkDirty();
        }

        private void RemoveGrade_Click(object sender, EventArgs e)
        {
            if (_tree.SelectedNode?.Tag is not MaterialConfig material || _currentMachine is null)
                return;
            if (
                MessageBox.Show(
                    $"Delete grade '{GradeLabel(material)}'?",
                    "Confirm",
                    MessageBoxButtons.YesNo
                ) != DialogResult.Yes
            )
                return;

            _currentMachine.Materials.Remove(material);
            var gradeNode = _tree.SelectedNode;
            var groupNode = gradeNode.Parent;
            groupNode.Nodes.Remove(gradeNode);
            if (groupNode.Nodes.Count == 0)
                groupNode.Remove();
            MarkDirty();
        }

        private void RelocateGradeNode(TreeNode gradeNode, string newName)
        {
            var groupNode = gradeNode.Parent;
            if (groupNode is null || SharedListNames.Key(groupNode.Text) == SharedListNames.Key(newName))
                return;

            var machineNode = groupNode.Parent;
            if (machineNode is null)
                return;

            groupNode.Nodes.Remove(gradeNode);

            var targetKey = SharedListNames.Key(newName);
            var targetGroup = machineNode
                .Nodes.Cast<TreeNode>()
                .FirstOrDefault(n => SharedListNames.Key(n.Text) == targetKey);
            if (targetGroup is null)
            {
                targetGroup = new TreeNode(newName) { Tag = newName };
                machineNode.Nodes.Add(targetGroup);
            }
            targetGroup.Nodes.Add(gradeNode);
            targetGroup.Expand();

            if (groupNode.Nodes.Count == 0)
                groupNode.Remove();

            _tree.SelectedNode = gradeNode;
        }

        private void AddThickness_Click(object sender, EventArgs e)
        {
            if (_tree.SelectedNode?.Tag is not MaterialConfig material || _currentMachine is null)
                return;

            var materialNode = _tree.SelectedNode;
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
