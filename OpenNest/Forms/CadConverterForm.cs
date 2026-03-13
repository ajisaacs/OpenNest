using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Properties;
using System;
using System.Threading;

namespace OpenNest.Forms
{
    public partial class CadConverterForm : Form
    {
        public CadConverterForm()
        {
            InitializeComponent();

            Items = new BindingList<CadConverterItem>();
            dataGridView1.DataSource = Items;
            dataGridView1.DataError += dataGridView1_DataError;
        }

        private BindingList<CadConverterItem> Items { get; set; }

        private void SetRotation(Shape shape, RotationType rotation)
        {
            try
            {
                var dir = shape.ToPolygon(3).RotationDirection();

                if (dir != rotation)
                    shape.Reverse();
            }
            catch { }
        }

        private void LoadItem(CadConverterItem item)
        {
            entityView1.Entities.Clear();
            entityView1.Entities.AddRange(item.Entities);
            entityView1.ZoomToFit();

            item.Entities.ForEach(e => e.IsVisible = true);

            // Layers
            checkedListBox1.Items.Clear();

            var layers = item.Entities
                .Where(e => e.Layer != null)
                .Select(e => e.Layer.Name)
                .ToList()
                .Distinct();

            foreach (var layer in layers)
                checkedListBox1.Items.Add(layer, true);

            // Colors
            checkedListBox2.Items.Clear();

            var colors = item.Entities
                .Select(e => e.Color.ToArgb())
                .Distinct()
                .Select(argb => new ColorItem(Color.FromArgb(argb)));

            foreach (var color in colors)
                checkedListBox2.Items.Add(color, false);

            // Line Types
            checkedListBox3.Items.Clear();

            var lineTypes = item.Entities
                .Select(e => e.LineTypeName ?? "Continuous")
                .Distinct();

            foreach (var lineType in lineTypes)
                checkedListBox3.Items.Add(lineType, false);
        }

        private static Color GetNextColor()
        {
            //if (colorIndex >= Colors.Length)
            //    colorIndex = 0;

            //var color = Colors[colorIndex];

            //colorIndex++;

            return new HSLColor(new Random().NextDouble() * 240, 240, 160);
 
        }

        public List<Drawing> GetDrawings()
        {
            var drawings = new List<Drawing>();

            foreach (var item in Items)
            {
                var entities = item.Entities.Where(e => e.Layer.IsVisible && e.IsVisible).ToList();

                if (entities.Count == 0)
                    continue;
                
                var drawing = new Drawing(item.Name);
                drawing.Color = GetNextColor();
                drawing.Customer = item.Customer;
                drawing.Source.Path = item.Path;
                drawing.Quantity.Required = item.Quantity;

                var shape = new ShapeProfile(entities);

                SetRotation(shape.Perimeter, RotationType.CW);

                foreach (var cutout in shape.Cutouts)
                    SetRotation(cutout, RotationType.CCW);

                entities = new List<Entity>();
                entities.AddRange(shape.Perimeter.Entities);

                shape.Cutouts.ForEach(cutout => entities.AddRange(cutout.Entities));

                var pgm = ConvertGeometry.ToProgram(entities);
                var firstCode = pgm[0];

                if (firstCode.Type == CodeType.RapidMove)
                {
                    var rapid = (RapidMove)firstCode;

                    drawing.Source.Offset = rapid.EndPoint;

                    pgm.Offset(-rapid.EndPoint);
                    pgm.Codes.RemoveAt(0);
                }

                drawing.Program = pgm;
                drawings.Add(drawing);

                Thread.Sleep(20);
            }

            return drawings;
        }

        private CadConverterItem CurrentItem
        {
            get
            {
                return dataGridView1.SelectedRows.Count != 0
                    ? Items[dataGridView1.SelectedRows[0].Index]
                    : null;
            }
        }

        public void AddFile(string file)
        {
            var importer = new DxfImporter();
            importer.SplinePrecision = Settings.Default.ImportSplinePrecision;

            var entities = new List<Entity>();

            if (!importer.GetGeometry(file, out entities))
            {
                MessageBox.Show("Failed to import file \"" + file + "\"");
                return;
            }

            lock (Items)
            {
                Items.Add(new CadConverterItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Entities = entities,
                    Path = file,
                    Quantity = 1,
                    Customer = string.Empty
                });
            }
        }

        public void AddFiles(IEnumerable<string> files)
        {
            Parallel.ForEach(files, AddFile);
        }

        private void dataGridView1_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            MessageBox.Show(e.Exception.Message);
        }

        private void dataGridView1_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            dataGridView1.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
        }

        private void dataGridView1_SelectionChanged(object sender, System.EventArgs e)
        {
            var currentItem = CurrentItem;

            if (currentItem != null)
                LoadItem(currentItem);
        }

        #region Colors

        private static Color[] Colors = new Color[]
        {
            Color.FromArgb(160, 255, 255),
            Color.FromArgb(160, 255, 160),
            Color.FromArgb(160, 160, 255),
            Color.FromArgb(255, 255, 160),
            Color.FromArgb(255, 160, 255),
            Color.FromArgb(255, 160, 160),

            Color.FromArgb(200, 255, 255),
            Color.FromArgb(200, 255, 200),
            Color.FromArgb(200, 200, 255),
            Color.FromArgb(255, 255, 200),
            Color.FromArgb(255, 200, 255),
            Color.FromArgb(255, 200, 200),
        };

        #endregion

        private void checkedListBox1_SelectedIndexChanged(object sender, System.EventArgs e)
        {
            var index = checkedListBox1.SelectedIndex;
            var layerName = checkedListBox1.Items[index].ToString();
            var isVisible = checkedListBox1.CheckedItems.Contains(layerName);

            CurrentItem.Entities.ForEach(entity =>
            {
                if (entity.Layer.Name == layerName)
                    entity.Layer.IsVisible = isVisible;
            });

            entityView1.Invalidate();
        }

        private void checkedListBox2_SelectedIndexChanged(object sender, System.EventArgs e)
        {
            UpdateEntityVisibility();
        }

        private void checkedListBox3_SelectedIndexChanged(object sender, System.EventArgs e)
        {
            UpdateEntityVisibility();
        }

        private void UpdateEntityVisibility()
        {
            var item = CurrentItem;
            if (item == null) return;

            var checkedColors = new HashSet<int>();
            for (var i = 0; i < checkedListBox2.Items.Count; i++)
            {
                if (checkedListBox2.GetItemChecked(i))
                    checkedColors.Add(((ColorItem)checkedListBox2.Items[i]).Argb);
            }

            var checkedLineTypes = new HashSet<string>();
            for (var i = 0; i < checkedListBox3.Items.Count; i++)
            {
                if (checkedListBox3.GetItemChecked(i))
                    checkedLineTypes.Add(checkedListBox3.Items[i].ToString());
            }

            item.Entities.ForEach(entity =>
            {
                entity.IsVisible = !checkedColors.Contains(entity.Color.ToArgb())
                                && !checkedLineTypes.Contains(entity.LineTypeName ?? "Continuous");
            });

            entityView1.Invalidate();
        }

        private void checkedListBox2_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            e.DrawBackground();

            var colorItem = (ColorItem)checkedListBox2.Items[e.Index];
            var swatchRect = new Rectangle(e.Bounds.Left + 20, e.Bounds.Top + 2, 16, e.Bounds.Height - 4);

            using (var brush = new SolidBrush(colorItem.Color))
                e.Graphics.FillRectangle(brush, swatchRect);

            e.Graphics.DrawRectangle(Pens.Gray, swatchRect);

            var textRect = new Rectangle(swatchRect.Right + 4, e.Bounds.Top, e.Bounds.Width - swatchRect.Right - 4, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, colorItem.ToString(), e.Font, textRect, e.ForeColor, TextFormatFlags.VerticalCenter);

            e.DrawFocusRectangle();
        }
    }

    class CadConverterItem
    {
        public string Name { get; set; }

        public string Customer { get; set; }

        public int Quantity { get; set; }

        [ReadOnly(true)]
        public string Path { get; set; }

        [Browsable(false)]
        public List<Entity> Entities { get; set; }
    }

    class ColorItem
    {
        public int Argb { get; }
        public Color Color { get; }

        public ColorItem(Color color)
        {
            Color = color;
            Argb = color.ToArgb();
        }

        public override string ToString() => $"RGB({Color.R}, {Color.G}, {Color.B})";
        public override bool Equals(object obj) => obj is ColorItem other && Argb == other.Argb;
        public override int GetHashCode() => Argb;
    }

    public class RandomColorGenerator
    {
        private readonly Random random;

        public RandomColorGenerator()
        {
            random = new Random();
        }

        public Color GetNext()
        {
            var r = random.Next(255);
            Thread.Sleep(20);
            var g = random.Next(255);
            Thread.Sleep(20);
            var b = random.Next(255);

            return Color.FromArgb(r, g, b);
        }
    }

    public class HSLColor
    {
        // Private data members below are on scale 0-1
        // They are scaled for use externally based on scale
        private double hue = 1.0;
        private double saturation = 1.0;
        private double luminosity = 1.0;

        private const double scale = 240.0;

        public double Hue
        {
            get { return hue * scale; }
            set { hue = CheckRange(value / scale); }
        }
        public double Saturation
        {
            get { return saturation * scale; }
            set { saturation = CheckRange(value / scale); }
        }
        public double Luminosity
        {
            get { return luminosity * scale; }
            set { luminosity = CheckRange(value / scale); }
        }

        private double CheckRange(double value)
        {
            if (value < 0.0)
                value = 0.0;
            else if (value > 1.0)
                value = 1.0;
            return value;
        }

        public override string ToString()
        {
            return String.Format("H: {0:#0.##} S: {1:#0.##} L: {2:#0.##}", Hue, Saturation, Luminosity);
        }

        public string ToRGBString()
        {
            Color color = (Color)this;
            return String.Format("R: {0:#0.##} G: {1:#0.##} B: {2:#0.##}", color.R, color.G, color.B);
        }

        #region Casts to/from System.Drawing.Color
        public static implicit operator Color(HSLColor hslColor)
        {
            double r = 0, g = 0, b = 0;
            if (hslColor.luminosity != 0)
            {
                if (hslColor.saturation == 0)
                    r = g = b = hslColor.luminosity;
                else
                {
                    double temp2 = GetTemp2(hslColor);
                    double temp1 = 2.0 * hslColor.luminosity - temp2;

                    r = GetColorComponent(temp1, temp2, hslColor.hue + 1.0 / 3.0);
                    g = GetColorComponent(temp1, temp2, hslColor.hue);
                    b = GetColorComponent(temp1, temp2, hslColor.hue - 1.0 / 3.0);
                }
            }
            return Color.FromArgb((int)(255 * r), (int)(255 * g), (int)(255 * b));
        }

        private static double GetColorComponent(double temp1, double temp2, double temp3)
        {
            temp3 = MoveIntoRange(temp3);
            if (temp3 < 1.0 / 6.0)
                return temp1 + (temp2 - temp1) * 6.0 * temp3;
            else if (temp3 < 0.5)
                return temp2;
            else if (temp3 < 2.0 / 3.0)
                return temp1 + ((temp2 - temp1) * ((2.0 / 3.0) - temp3) * 6.0);
            else
                return temp1;
        }
        private static double MoveIntoRange(double temp3)
        {
            if (temp3 < 0.0)
                temp3 += 1.0;
            else if (temp3 > 1.0)
                temp3 -= 1.0;
            return temp3;
        }
        private static double GetTemp2(HSLColor hslColor)
        {
            double temp2;
            if (hslColor.luminosity < 0.5)  //<=??
                temp2 = hslColor.luminosity * (1.0 + hslColor.saturation);
            else
                temp2 = hslColor.luminosity + hslColor.saturation - (hslColor.luminosity * hslColor.saturation);
            return temp2;
        }

        public static implicit operator HSLColor(Color color)
        {
            HSLColor hslColor = new HSLColor();
            hslColor.hue = color.GetHue() / 360.0; // we store hue as 0-1 as opposed to 0-360 
            hslColor.luminosity = color.GetBrightness();
            hslColor.saturation = color.GetSaturation();
            return hslColor;
        }
        #endregion

        public void SetRGB(int red, int green, int blue)
        {
            HSLColor hslColor = (HSLColor)Color.FromArgb(red, green, blue);
            this.hue = hslColor.hue;
            this.saturation = hslColor.saturation;
            this.luminosity = hslColor.luminosity;
        }

        public HSLColor() { }
        public HSLColor(Color color)
        {
            SetRGB(color.R, color.G, color.B);
        }
        public HSLColor(int red, int green, int blue)
        {
            SetRGB(red, green, blue);
        }
        public HSLColor(double hue, double saturation, double luminosity)
        {
            this.Hue = hue;
            this.Saturation = saturation;
            this.Luminosity = luminosity;
        }

    }
}
