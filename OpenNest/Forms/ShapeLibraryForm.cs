using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Math;
using OpenNest.Shapes;

namespace OpenNest.Forms
{
    public partial class ShapeLibraryForm : Form
    {
        private static readonly Regex FriendlyNamePattern = new(
            @"(?<=[a-z0-9])([A-Z])",
            RegexOptions.None,
            TimeSpan.FromMilliseconds(100)
        );

        private const int ThumbnailSize = 96;
        private const int TileWidth = 124;
        private const int TileHeight = 136;
        private const int TileColumns = 3;
        private const int LVM_SETICONSPACING = 0x1000 + 53;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly List<Drawing> addedDrawings = new List<Drawing>();
        private readonly List<ShapeEntry> shapeEntries = new List<ShapeEntry>();
        private readonly List<PropertyInfo> parameters = new List<PropertyInfo>();
        private readonly Dictionary<string, object> values = new Dictionary<string, object>();
        private readonly HashSet<string> existingNames;
        private readonly ShapeLibraryInputs lastInputs;

        private ShapeEntry selectedEntry;
        private bool suppressPreview;
        private Control editor;

        // The next preview shows a different shape (or configuration): fit it and reset dragged dimensions.
        private bool newShape = true;

        /// <param name="existingDrawingNames">Names already in the nest, so added shapes get unique names.</param>
        /// <param name="lastInputs">
        /// Inputs from an earlier session to start from: the last shape, the quantity and each
        /// shape's values. The form records into it as it goes; read <see cref="LastInputs"/> to save.
        /// </param>
        public ShapeLibraryForm(IEnumerable<string> existingDrawingNames = null, ShapeLibraryInputs lastInputs = null)
        {
            existingNames =
                existingDrawingNames != null
                    ? new HashSet<string>(existingDrawingNames, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.lastInputs = lastInputs ?? new ShapeLibraryInputs();

            InitializeComponent();
            Load += (_, _) =>
            {
                DialogBounds.FitToOwnerScreen(this);
                FitShapeGrid();
            };
            DiscoverShapes();
            PopulateShapeList();

            shapeListView.SelectedIndexChanged += ShapeListView_SelectedIndexChanged;
            configComboBox.SelectedIndexChanged += ConfigComboBox_SelectedIndexChanged;
            previewBox.LabelClicked += PreviewBox_LabelClicked;
            previewBox.ViewChanged += PreviewBox_ViewChanged;
            addButton.Click += AddButton_Click;
            closeButton.Click += (s, e) => Close();

            quantityUpDown.Value = System.Math.Clamp(this.lastInputs.Quantity, quantityUpDown.Minimum, quantityUpDown.Maximum);

            if (shapeListView.Items.Count > 0)
            {
                var item =
                    shapeListView.Items.Cast<ListViewItem>()
                        .FirstOrDefault(i => ((ShapeEntry)i.Tag).ShapeType.Name == this.lastInputs.LastShape)
                    ?? shapeListView.Items[0];
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
                // The list raises selection events only once its window exists.
                ShowShape((ShapeEntry)item.Tag);
            }
        }

        public List<Drawing> GetDrawings() => addedDrawings;

        /// <summary>The shape shown, the quantity and each shape's inputs, for the next session to start from.</summary>
        public ShapeLibraryInputs LastInputs
        {
            get
            {
                RememberInputs();
                return lastInputs;
            }
        }

        private void DiscoverShapes()
        {
            var baseType = typeof(ShapeDefinition);
            var shapeTypes = baseType
                .Assembly.GetTypes()
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
            var images = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size(ThumbnailSize, ThumbnailSize),
            };
            shapeListView.LargeImageList = images;
            shapeListView.BackColor = ColorScheme.Default.LibraryBackgroundColor;
            shapeListView.ForeColor = ColorScheme.Default.LibraryOutlineColor;
            shapeListView.HandleCreated += (_, _) => ApplyTileSpacing();
            Disposed += (_, _) => images.Dispose();

            foreach (var entry in shapeEntries)
            {
                images.Images.Add(CreateThumbnail(entry));

                var text = entry.DisplayName;
                if (entry.HasConfigurations)
                    text += $" ({entry.Configurations.Count})";

                shapeListView.Items.Add(
                    new ListViewItem(text, images.Images.Count - 1) { Tag = entry }
                );
            }
        }

        private static Image CreateThumbnail(ShapeEntry entry)
        {
            var thumbnail = new Bitmap(ThumbnailSize, ThumbnailSize);

            try
            {
                var shape = entry.HasConfigurations
                    ? entry.Configurations[0]
                    : (ShapeDefinition)Activator.CreateInstance(entry.ShapeType);
                if (!entry.HasConfigurations)
                    shape.SetPreviewDefaults();

                var program = shape.GetDrawing()?.Program;
                if (program == null)
                    return thumbnail;

                // GetImage draws without antialiasing; render at twice the size and
                // downsample so curved edges stay smooth at thumbnail scale.
                var renderSize = new Size(ThumbnailSize * 2, ThumbnailSize * 2);
                var scheme = ColorScheme.Default;
                using var brush = new SolidBrush(scheme.LibraryFillColor);
                using var pen = new Pen(scheme.LibraryOutlineColor, 2f);
                using var large = program.GetImage(renderSize, pen, brush, scheme.LibraryEtchColor);
                using var graphics = Graphics.FromImage(thumbnail);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(large, 0, 0, ThumbnailSize, ThumbnailSize);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"Shape thumbnail for '{entry.DisplayName}' failed: {ex.Message}"
                );
            }

            return thumbnail;
        }

        private void ApplyTileSpacing()
        {
            var spacing = (TileHeight << 16) | TileWidth;
            SendMessage(shapeListView.Handle, LVM_SETICONSPACING, IntPtr.Zero, (IntPtr)spacing);
        }

        private void FitShapeGrid()
        {
            var width = TileWidth * TileColumns + SystemInformation.VerticalScrollBarWidth + 8;
            splitContainer.SplitterDistance = System.Math.Min(
                width,
                splitContainer.Width - splitContainer.Panel2MinSize - splitContainer.SplitterWidth
            );
        }

        private void ShapeListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Changing selection raises a deselect first; keep the current shape until the new one arrives.
            if (shapeListView.SelectedItems.Count == 0)
                return;

            var entry = (ShapeEntry)shapeListView.SelectedItems[0].Tag;
            if (entry != selectedEntry)
                ShowShape(entry);
        }

        private void ShowShape(ShapeEntry entry)
        {
            CloseEditor();
            RememberInputs();
            selectedEntry = entry;
            suppressPreview = true;

            var hasConfigs = selectedEntry.HasConfigurations;
            configLabel.Visible = hasConfigs;
            configComboBox.Visible = hasConfigs;
            lastInputs.Shapes.TryGetValue(entry.ShapeType.Name, out var saved);

            if (hasConfigs)
            {
                configComboBox.Items.Clear();
                foreach (var cfg in selectedEntry.Configurations)
                    configComboBox.Items.Add(cfg.Name);

                var index = saved?.Configuration != null ? configComboBox.Items.IndexOf(saved.Configuration) : -1;
                if (index < 0)
                {
                    // No saved configuration, or it is gone: saved values no longer apply.
                    saved = null;
                    index = 0;
                }
                configComboBox.SelectedIndex = index;
                // Selecting the configuration loaded its values and ended the suppression.
                suppressPreview = true;
            }
            else
            {
                nameTextBox.Text = selectedEntry.DisplayName;
                var defaults = (ShapeDefinition)Activator.CreateInstance(selectedEntry.ShapeType);
                defaults.SetPreviewDefaults();
                LoadParameters(selectedEntry.ShapeType, defaults);
            }

            if (saved != null)
                RestoreValues(saved);

            suppressPreview = false;
            newShape = true;
            UpdatePreview();
        }

        /// <summary>Records the shown shape's inputs into <see cref="lastInputs"/>.</summary>
        private void RememberInputs()
        {
            lastInputs.Quantity = (int)quantityUpDown.Value;
            if (selectedEntry == null)
                return;

            lastInputs.LastShape = selectedEntry.ShapeType.Name;
            lastInputs.Shapes[selectedEntry.ShapeType.Name] = new ShapeLibraryInputs.ShapeInputs
            {
                Configuration = selectedEntry.HasConfigurations ? configComboBox.SelectedItem as string : null,
                Values = values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value), StringComparer.Ordinal),
            };
        }

        /// <summary>
        /// Puts saved values over the loaded ones. A value that no longer reads as its parameter's
        /// type is skipped; if the result no longer makes a shape, the loaded values stay.
        /// </summary>
        private void RestoreValues(ShapeLibraryInputs.ShapeInputs saved)
        {
            if (saved.Values == null)
                return;

            var restored = new Dictionary<string, object>(values);
            foreach (var prop in parameters)
            {
                if (!saved.Values.TryGetValue(prop.Name, out var element))
                    continue;
                try
                {
                    var value = element.Deserialize(prop.PropertyType);
                    if (value == null && prop.PropertyType.IsValueType)
                        continue;
                    if (value is double d && (!double.IsFinite(d) || d < 0))
                        continue;
                    if (value is int i && i < 0)
                        continue;
                    restored[prop.Name] = value;
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
                {
                    // Keep the loaded value.
                }
            }

            if (ShapeError(restored) != null)
                return;
            foreach (var pair in restored)
                values[pair.Key] = pair.Value;
        }

        private void ConfigComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (configComboBox.SelectedIndex < 0 || selectedEntry == null)
                return;

            var config = selectedEntry.Configurations[configComboBox.SelectedIndex];
            nameTextBox.Text = config.Name;

            suppressPreview = true;
            LoadParameters(selectedEntry.ShapeType, config);
            suppressPreview = false;
            newShape = true;
            UpdatePreview();
        }

        private void LoadParameters(Type shapeType, ShapeDefinition sourceValues)
        {
            CloseEditor();
            parameters.Clear();
            values.Clear();

            parameters.AddRange(shapeType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.CanRead && p.CanWrite && p.Name != "Name"));

            foreach (var prop in parameters)
                values[prop.Name] = sourceValues != null ? prop.GetValue(sourceValues) : DefaultOf(prop.PropertyType);
        }

        private static object DefaultOf(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

        private PropertyInfo Parameter(string name) => parameters.FirstOrDefault(p => p.Name == name);

        private void UpdatePreview(string error = null)
        {
            if (suppressPreview || selectedEntry == null)
                return;

            UpdatePipeSize();

            try
            {
                var shape = CreateShapeFromInputs();
                var drawing = shape.GetDrawing();
                if (drawing?.Program == null)
                {
                    InvalidatePreview();
                    return;
                }

                addButton.Enabled = true;
                nameTextBox.Text = shape.GenerateName();
                var sideView = shape.GetSideView();
                previewBox.ShowDrawing(drawing, newShape, sideView?.Entities);
                newShape = false;

                var dimensions = shape.GetDimensions().Select(d => (Dimension: d, OnSideView: false))
                    .Concat((sideView?.Dimensions ?? Array.Empty<ShapeDimension>()).Select(d => (Dimension: d, OnSideView: true)))
                    .Where(d => Parameter(d.Dimension.Property) != null)
                    .ToList();
                var placed = dimensions.Select(d => d.Dimension.Property)
                    .Concat(dimensions.Select(d => d.Dimension.CountProperty).Where(p => p != null))
                    .ToHashSet();
                previewBox.SetAnnotations(
                    dimensions.Select(item =>
                    {
                        var d = item.Dimension;
                        return new ShapePreviewControl.Annotation(d,
                            d.Prefix + FormatLength((double)values[d.Property]),
                            d.CountProperty != null && values.TryGetValue(d.CountProperty, out var count)
                                ? $"x {count} {d.CountNoun}"
                                : null,
                            item.OnSideView);
                    }).ToList(),
                    parameters.Where(p => !placed.Contains(p.Name)).Select(NoteFor).ToList());

                var bb = drawing.Program.BoundingBox();
                var info = string.Format("{0:F3} x {1:F3}", bb.Size.Length, bb.Size.Width);

                if (
                    shape is PipeFlangeShape flange
                    && !flange.Blind
                    && !string.IsNullOrEmpty(flange.PipeSize)
                    && !PipeSizes.TryGetOD(flange.PipeSize, out _)
                )
                {
                    info += "  — Invalid pipe size, no bore cut";
                }

                previewBox.SetInfo(nameTextBox.Text, info, error);
            }
            catch
            {
                InvalidatePreview();
            }
        }

        /// <summary>
        /// No drawing to dimension: every parameter becomes a note so the input can still be
        /// corrected.
        /// </summary>
        private void InvalidatePreview()
        {
            addButton.Enabled = false;
            previewBox.ShowDrawing(null);
            previewBox.SetAnnotations(null, parameters.Select(NoteFor).ToList());
            previewBox.SetInfo("Invalid shape input");
        }

        private ShapePreviewControl.Note NoteFor(PropertyInfo prop)
        {
            var value = values[prop.Name];
            var text = value switch
            {
                bool b => b ? "Yes" : "No",
                double d when IsUnitless(prop) => d.ToString("0.####", CultureInfo.CurrentCulture),
                double d => FormatLength(d),
                null => "None",
                _ => value.ToString(),
            };
            var enabled = prop.Name != nameof(PipeFlangeShape.PipeSize) || !Equals(values.GetValueOrDefault(nameof(PipeFlangeShape.Blind)), true);
            var label = prop.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? FriendlyName(prop.Name);
            return new ShapePreviewControl.Note(prop.Name, $"{label}: {text}", enabled);
        }

        private static bool IsUnitless(PropertyInfo prop) => prop.IsDefined(typeof(UnitlessAttribute));

        internal static string FormatLength(double inches) => Fraction.Format(inches) + "\"";

        /// <summary>The pipe sizes that fit inside the bolt holes: pipe OD + clearance &lt; pattern - hole.</summary>
        private List<string> FittingPipeSizes()
        {
            double Length(string name) => values.TryGetValue(name, out var v) && v is double d ? d : 0;
            var maxPipeOD = Length(nameof(PipeFlangeShape.HolePatternDiameter))
                - Length(nameof(PipeFlangeShape.HoleDiameter))
                - Length(nameof(PipeFlangeShape.PipeClearance));
            return PipeSizes.GetFittingSizes(maxPipeOD).Select(e => e.Label).ToList();
        }

        /// <summary>Keeps the pipe size one that fits, else the largest that does (none when none fits).</summary>
        private void UpdatePipeSize()
        {
            const string key = nameof(PipeFlangeShape.PipeSize);
            if (Parameter(key)?.PropertyType != typeof(string))
                return;

            var fitting = FittingPipeSizes();
            if (values[key] is string current && fitting.Contains(current))
                return;
            values[key] = fitting.Count > 0 ? fitting[^1] : null;
        }

        private ShapeDefinition CreateShapeFromInputs(IReadOnlyDictionary<string, object> source = null)
        {
            source ??= values;
            var shape = (ShapeDefinition)Activator.CreateInstance(selectedEntry.ShapeType);
            shape.Name = nameTextBox.Text;
            foreach (var prop in parameters)
                prop.SetValue(shape, source[prop.Name]);
            return shape;
        }

        #region On-drawing editors

        private void PreviewBox_LabelClicked(object sender, string key) => EditParameter(key);

        /// <summary>Keeps an open editor over its label as the view zooms, pans or the dimension moves.</summary>
        private void PreviewBox_ViewChanged(object sender, EventArgs e)
        {
            if (editor == null || previewBox.ActiveKey == null)
                return;
            var bounds = previewBox.LabelBounds(previewBox.ActiveKey);
            if (bounds == null)
                CloseEditor();
            else
                editor.Location = bounds.Value.Location;
        }

        /// <summary>
        /// Opens the editor for a parameter over its label: a text box for a number, a list for
        /// the pipe size; a yes/no parameter toggles at once.
        /// </summary>
        internal void EditParameter(string key)
        {
            var prop = Parameter(key);
            if (prop == null)
                return;

            CloseEditor();
            if (prop.PropertyType == typeof(bool))
            {
                values[key] = !(bool)values[key];
                UpdatePreview();
                return;
            }

            var bounds = previewBox.LabelBounds(key);
            if (bounds == null)
                return;

            if (prop.PropertyType == typeof(string))
                OpenListEditor(key, bounds.Value);
            else
                OpenTextEditor(key, bounds.Value);
        }

        internal Control ActiveEditor => editor;

        private void OpenTextEditor(string key, Rectangle bounds)
        {
            var value = values[key];
            var box = new DimensionBox
            {
                Font = previewBox.Font,
                Text = value is double d ? Fraction.Format(d) : Convert.ToString(value, CultureInfo.CurrentCulture),
                Location = bounds.Location,
                Width = System.Math.Max(bounds.Width + 24, 80),
            };
            box.Commit += (_, _) => TryCommit(key, box.Text);
            box.Cancel += (_, _) => CloseEditor();
            box.Advance += (_, back) =>
            {
                if (TryCommit(key, box.Text))
                    EditNext(key, back);
            };
            box.LostFocus += (_, _) =>
            {
                // Clicking away keeps a valid entry and drops an invalid one.
                if (editor == box && !TryCommit(key, box.Text))
                    CloseEditor();
            };
            Show(key, box);
            box.SelectAll();
        }

        private void OpenListEditor(string key, Rectangle bounds)
        {
            var combo = new ComboBox
            {
                Font = previewBox.Font,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = bounds.Location,
                Width = System.Math.Max(bounds.Width + 40, 110),
            };
            foreach (var label in FittingPipeSizes())
                combo.Items.Add(label);
            if (values[key] is string current && combo.Items.Contains(current))
                combo.SelectedItem = current;

            combo.SelectionChangeCommitted += (_, _) =>
            {
                values[key] = combo.SelectedItem as string;
                CloseEditor();
                UpdatePreview();
            };
            combo.DropDownClosed += (_, _) => BeginInvoke(() =>
            {
                if (editor == combo)
                    CloseEditor();
            });
            combo.LostFocus += (_, _) =>
            {
                if (editor == combo && !combo.DroppedDown)
                    CloseEditor();
            };
            Show(key, combo);
            combo.DroppedDown = combo.Items.Count > 0;
        }

        private void Show(string key, Control control)
        {
            editor = control;
            previewBox.Controls.Add(control);
            previewBox.ActiveKey = key;
            control.Focus();
        }

        private void CloseEditor()
        {
            var control = editor;
            if (control == null)
                return;
            editor = null; // first: removing the control raises LostFocus again
            previewBox.ActiveKey = null;
            previewBox.Controls.Remove(control);
            control.Dispose();
        }

        /// <summary>Tab moves to the next number on the drawing, Shift+Tab to the previous.</summary>
        private void EditNext(string key, bool back)
        {
            var keys = previewBox.LabelKeys
                .Where(k => Parameter(k) is { } p && (p.PropertyType == typeof(double) || p.PropertyType == typeof(int)))
                .ToList();
            if (keys.Count == 0)
                return;
            var index = keys.IndexOf(key);
            var next = index < 0 ? 0 : (index + (back ? keys.Count - 1 : 1)) % keys.Count;
            EditParameter(keys[next]);
        }

        /// <summary>
        /// Applies the typed text to <paramref name="key"/> when it is a valid value that still
        /// makes a shape; otherwise leaves the editor open with the text in red.
        /// </summary>
        private bool TryCommit(string key, string text)
        {
            var prop = Parameter(key);
            var error = ParseValue(prop, text, out var value);
            if (error == null)
            {
                var candidate = new Dictionary<string, object>(values) { [key] = value };
                error = ShapeError(candidate);
                if (error == null)
                {
                    values[key] = value;
                    CloseEditor();
                    UpdatePreview();
                    return true;
                }
            }

            if (editor != null)
                editor.ForeColor = Color.Red;
            UpdatePreviewInfoError(error);
            return false;
        }

        private void UpdatePreviewInfoError(string error) =>
            previewBox.SetInfo(nameTextBox.Text, error);

        private static string ParseValue(PropertyInfo prop, string text, out object value)
        {
            value = null;
            if (prop.PropertyType == typeof(int))
            {
                if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var count))
                    return $"'{text}' is not a whole number.";
                if (count < 0)
                    return "The value cannot be negative.";
                value = count;
                return null;
            }

            if (IsUnitless(prop))
            {
                if (!double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var number) || !double.IsFinite(number))
                    return $"'{text}' is not a number.";
                if (number < 0)
                    return "The value cannot be negative.";
                value = number;
                return null;
            }

            double length;
            try
            {
                length = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var plain)
                    ? plain
                    : ArchUnits.ParseToInches(text);
            }
            catch (FormatException)
            {
                return $"'{text}' is not a length.";
            }
            if (!double.IsFinite(length))
                return $"'{text}' is not a length.";
            if (length < 0)
                return "The value cannot be negative.";
            value = length;
            return null;
        }

        private string ShapeError(IReadOnlyDictionary<string, object> candidate)
        {
            try
            {
                var program = CreateShapeFromInputs(candidate).GetDrawing()?.Program;
                var box = program?.BoundingBox();
                return box != null && box.Length > 0 && box.Width > 0 ? null : "That value does not make a shape.";
            }
            catch
            {
                return "That value does not make a shape.";
            }
        }

        #endregion

        private void AddButton_Click(object sender, EventArgs e)
        {
            try
            {
                var shape = CreateShapeFromInputs();
                var drawing = shape.GetDrawing();
                drawing.Name = GetUniqueName(drawing.Name);
                drawing.Color = Drawing.GetNextColor();
                drawing.Quantity.Required = (int)quantityUpDown.Value;

                addedDrawings.Add(drawing);
                existingNames.Add(drawing.Name);
                DialogResult = DialogResult.OK;

                addButton.Text = $"Added ({addedDrawings.Count})";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to create shape: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private string GetUniqueName(string baseName)
        {
            if (!existingNames.Contains(baseName))
                return baseName;

            for (var i = 2; ; i++)
            {
                var candidate = $"{baseName} ({i})";
                if (!existingNames.Contains(candidate))
                    return candidate;
            }
        }

        internal static string FriendlyName(
            string name,
            Func<string, string> replace = null,
            Action<string> report = null
        )
        {
            if (name.EndsWith("Shape"))
                name = name.Substring(0, name.Length - 5);

            try
            {
                return replace != null ? replace(name) : FriendlyNamePattern.Replace(name, " $1");
            }
            catch (RegexMatchTimeoutException)
            {
                var diagnostic = $"Friendly-name formatting timed out for '{name}'; using the unchanged name.";
                if (report != null)
                    report(diagnostic);
                else
                    System.Diagnostics.Trace.TraceWarning(diagnostic);
                return name;
            }
        }

        private class ShapeEntry
        {
            public Type ShapeType { get; set; }
            public string DisplayName { get; set; }
            public List<ShapeDefinition> Configurations { get; set; }
            public bool HasConfigurations => Configurations != null && Configurations.Count > 0;

            public override string ToString() => DisplayName;
        }
    }
}
