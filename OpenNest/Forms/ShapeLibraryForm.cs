using OpenNest.Shapes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class ShapeLibraryForm : Form
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly List<Drawing> addedDrawings = new List<Drawing>();
        private readonly List<ShapeEntry> shapeEntries = new List<ShapeEntry>();
        private readonly List<ParameterBinding> parameterBindings = new List<ParameterBinding>();

        private ShapeEntry selectedEntry;
        private bool suppressPreview;

        public ShapeLibraryForm()
        {
            InitializeComponent();
            DiscoverShapes();
            PopulateShapeList();

            shapeListBox.DrawItem += ShapeListBox_DrawItem;
            shapeListBox.SelectedIndexChanged += ShapeListBox_SelectedIndexChanged;
            configComboBox.SelectedIndexChanged += ConfigComboBox_SelectedIndexChanged;
            addButton.Click += AddButton_Click;
            closeButton.Click += (s, e) => Close();

            if (shapeListBox.Items.Count > 0)
                shapeListBox.SelectedIndex = 0;
        }

        public List<Drawing> GetDrawings() => addedDrawings;

        private void DiscoverShapes()
        {
            var baseType = typeof(ShapeDefinition);
            var shapeTypes = baseType.Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToList();

            var configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configurations");

            foreach (var type in shapeTypes)
            {
                var entry = new ShapeEntry { ShapeType = type };
                entry.DisplayName = FriendlyName(type.Name);

                var configPath = Path.Combine(configDir, type.Name + ".json");
                if (File.Exists(configPath))
                    entry.Configurations = LoadConfigurations(type, configPath);

                shapeEntries.Add(entry);
            }
        }

        private List<ShapeDefinition> LoadConfigurations(Type shapeType, string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                var listType = typeof(List<>).MakeGenericType(shapeType);
                var list = JsonSerializer.Deserialize(json, listType, JsonOptions);
                return ((System.Collections.IEnumerable)list).Cast<ShapeDefinition>().ToList();
            }
            catch
            {
                return null;
            }
        }

        private void PopulateShapeList()
        {
            foreach (var entry in shapeEntries)
                shapeListBox.Items.Add(entry);
        }

        private void ShapeListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            e.DrawBackground();

            var entry = (ShapeEntry)shapeListBox.Items[e.Index];
            var textColor = (e.State & DrawItemState.Selected) != 0
                ? SystemColors.HighlightText
                : SystemColors.ControlText;

            var text = entry.DisplayName;
            if (entry.HasConfigurations)
                text += $"  ({entry.Configurations.Count})";

            using (var brush = new SolidBrush(textColor))
            {
                var format = new StringFormat { LineAlignment = StringAlignment.Center };
                var rect = new RectangleF(8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                e.Graphics.DrawString(text, e.Font, brush, rect, format);
            }

            e.DrawFocusRectangle();
        }

        private void ShapeListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (shapeListBox.SelectedIndex < 0) return;

            selectedEntry = (ShapeEntry)shapeListBox.SelectedItem;
            suppressPreview = true;

            var hasConfigs = selectedEntry.HasConfigurations;
            configLabel.Visible = hasConfigs;
            configComboBox.Visible = hasConfigs;

            if (hasConfigs)
            {
                configComboBox.Items.Clear();
                foreach (var cfg in selectedEntry.Configurations)
                    configComboBox.Items.Add(cfg.Name);

                configComboBox.SelectedIndex = 0;
            }
            else
            {
                nameTextBox.Text = selectedEntry.DisplayName;
                var defaults = (ShapeDefinition)Activator.CreateInstance(selectedEntry.ShapeType);
                defaults.SetPreviewDefaults();
                BuildParameterControls(selectedEntry.ShapeType, defaults);
            }

            suppressPreview = false;
            UpdatePreview();
        }

        private void ConfigComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (configComboBox.SelectedIndex < 0 || selectedEntry == null) return;

            var config = selectedEntry.Configurations[configComboBox.SelectedIndex];
            nameTextBox.Text = config.Name;

            suppressPreview = true;
            BuildParameterControls(selectedEntry.ShapeType, config);
            suppressPreview = false;
            UpdatePreview();
        }

        private void BuildParameterControls(Type shapeType, ShapeDefinition sourceValues)
        {
            parametersPanel.SuspendLayout();
            parametersPanel.Controls.Clear();
            parameterBindings.Clear();

            var props = shapeType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.CanRead && p.CanWrite && p.Name != "Name")
                .ToArray();

            var panelWidth = parametersPanel.ClientSize.Width - parametersPanel.Padding.Horizontal;
            var y = 4;

            foreach (var prop in props)
            {
                var label = new Label
                {
                    Text = FriendlyName(prop.Name),
                    Location = new Point(parametersPanel.Padding.Left, y),
                    AutoSize = true
                };

                y += 18;

                Control editor;
                if (prop.PropertyType == typeof(bool))
                {
                    var cb = new CheckBox
                    {
                        Location = new Point(parametersPanel.Padding.Left, y),
                        AutoSize = true,
                        Checked = sourceValues != null && (bool)prop.GetValue(sourceValues)
                    };
                    cb.CheckedChanged += (s, ev) => UpdatePreview();
                    editor = cb;
                }
                else
                {
                    var tb = new TextBox
                    {
                        Location = new Point(parametersPanel.Padding.Left, y),
                        Width = panelWidth,
                        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                    };

                    if (sourceValues != null)
                    {
                        if (prop.PropertyType == typeof(int))
                            tb.Text = ((int)prop.GetValue(sourceValues)).ToString();
                        else
                            tb.Text = ((double)prop.GetValue(sourceValues)).ToString("G");
                    }

                    tb.TextChanged += (s, ev) => UpdatePreview();
                    editor = tb;
                }

                parameterBindings.Add(new ParameterBinding { Property = prop, Control = editor });

                parametersPanel.Controls.Add(label);
                parametersPanel.Controls.Add(editor);

                y += 30;
            }

            parametersPanel.ResumeLayout(true);
        }

        private void UpdatePreview()
        {
            if (suppressPreview || selectedEntry == null) return;

            try
            {
                var shape = CreateShapeFromInputs();
                if (shape == null) return;

                var drawing = shape.GetDrawing();
                previewBox.ShowDrawing(drawing);

                if (drawing?.Program != null)
                {
                    var bb = drawing.Program.BoundingBox();
                    previewBox.SetInfo(
                        nameTextBox.Text,
                        string.Format("{0:F3} x {1:F3}", bb.Size.Length, bb.Size.Width));
                }
            }
            catch
            {
                previewBox.ShowDrawing(null);
            }
        }

        private ShapeDefinition CreateShapeFromInputs()
        {
            var shape = (ShapeDefinition)Activator.CreateInstance(selectedEntry.ShapeType);
            shape.Name = nameTextBox.Text;

            foreach (var binding in parameterBindings)
            {
                if (binding.Property.PropertyType == typeof(bool))
                {
                    var cb = (CheckBox)binding.Control;
                    binding.Property.SetValue(shape, cb.Checked);
                    continue;
                }

                var tb = (TextBox)binding.Control;

                if (binding.Property.PropertyType == typeof(int))
                {
                    if (int.TryParse(tb.Text, out var intVal))
                    {
                        binding.Property.SetValue(shape, intVal);
                        tb.ForeColor = SystemColors.WindowText;
                    }
                    else
                    {
                        tb.ForeColor = Color.Red;
                        return null;
                    }
                }
                else
                {
                    var val = ArchUnits.GetLengthInches(tb);
                    if (double.IsNaN(val))
                        return null;

                    binding.Property.SetValue(shape, val);
                }
            }

            return shape;
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            try
            {
                var shape = CreateShapeFromInputs();
                if (shape == null) return;

                var drawing = shape.GetDrawing();
                drawing.Color = Drawing.GetNextColor();
                drawing.Quantity.Required = (int)quantityUpDown.Value;

                addedDrawings.Add(drawing);
                DialogResult = DialogResult.OK;

                addButton.Text = $"Added ({addedDrawings.Count})";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to create shape: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static string FriendlyName(string name)
        {
            if (name.EndsWith("Shape"))
                name = name.Substring(0, name.Length - 5);

            return Regex.Replace(name, @"(?<=[a-z0-9])([A-Z])", " $1");
        }

        private class ShapeEntry
        {
            public Type ShapeType { get; set; }
            public string DisplayName { get; set; }
            public List<ShapeDefinition> Configurations { get; set; }
            public bool HasConfigurations => Configurations != null && Configurations.Count > 0;

            public override string ToString() => DisplayName;
        }

        private class ParameterBinding
        {
            public PropertyInfo Property { get; set; }
            public Control Control { get; set; }
        }
    }
}
