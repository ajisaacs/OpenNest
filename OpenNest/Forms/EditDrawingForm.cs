using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class EditDrawingForm : Form
    {
        private const int PreviewInset = 24;

        private Drawing drawing;

        public EditDrawingForm()
        {
            InitializeComponent();
            pictureBox1.SizeChanged += (_, _) => UpdateImage();
        }

        private string DrawingName
        {
            get { return nameBox.Text; }
            set { nameBox.Text = value; }
        }

        private string Customer
        {
            get { return customerBox.Text; }
            set { customerBox.Text = value; }
        }

        private int Quantity
        {
            get { return (int)qtyBox.Value; }
            set { qtyBox.Value = value; }
        }

        private int Priority
        {
            get { return (int)priorityBox.Value; }
            set { priorityBox.Value = value; }
        }

        private bool CutFirst
        {
            get { return cutFirstBox.Checked; }
            set { cutFirstBox.Checked = value; }
        }

        private Image DrawingImage
        {
            get { return pictureBox1.Image; }
            set { pictureBox1.Image = value; }
        }

        private void UpdateImage()
        {
            if (drawing == null)
                return;

            var size = pictureBox1.ClientSize;
            if (size.Width <= PreviewInset || size.Height <= PreviewInset)
                return;

            var brush = new SolidBrush(colorDialog1.Color);
            var pen = new Pen(ColorScheme.Default.GetPartOutlineColor(colorDialog1.Color));
            var previous = DrawingImage;
            DrawingImage = drawing.Program.GetImage(
                new Size(size.Width - PreviewInset, size.Height - PreviewInset), pen, brush);
            previous?.Dispose();

            pen.Dispose();
            brush.Dispose();
        }

        private void UpdateColor()
        {
            colorSwatch.BackColor = colorDialog1.Color;
            UpdateImage();
        }

        private void UpdateNestedLabel()
        {
            if (drawing == null)
                return;

            var nested = drawing.Quantity.Nested;
            var remaining = System.Math.Max(Quantity - nested, 0);
            nestedLabel.Text = $"{nested} nested, {remaining} remaining";
        }

        private void LoadDetails()
        {
            var box = drawing.Program.BoundingBox();
            sizeLabel.Text = $"{box.Length:0.###} x {box.Width:0.###}";
            areaLabel.Text = $"{drawing.Area:0.###}";

            var path = drawing.Source?.Path;
            if (string.IsNullOrEmpty(path))
            {
                sourceLabel.Text = "-";
                toolTip.SetToolTip(sourceLabel, null);
            }
            else
            {
                sourceLabel.Text = System.IO.Path.GetFileName(path);
                toolTip.SetToolTip(sourceLabel, path);
            }
        }

        public void LoadDrawing(Drawing drawing)
        {
            this.drawing = drawing;

            colorDialog1.Color = drawing.Color;
            DrawingName = drawing.Name;
            Customer = drawing.Customer;
            Quantity = drawing.Quantity.Required;
            Priority = drawing.Priority;
            CutFirst = drawing.CutFirst;
            Text = string.IsNullOrEmpty(drawing.Name)
                ? "Drawing Properties"
                : $"Drawing Properties - {drawing.Name}";
            LoadDetails();
            UpdateNestedLabel();
            UpdateColor();
        }

        public void SaveDrawing(Drawing drawing)
        {
            drawing.Name = DrawingName;
            drawing.Customer = Customer;
            drawing.Quantity.Required = Quantity;
            drawing.Priority = Priority;
            drawing.CutFirst = CutFirst;
            drawing.Color = colorDialog1.Color;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            nameBox.SelectAll();
            nameBox.Focus();
        }

        private void QtyBox_ValueChanged(object sender, EventArgs e) => UpdateNestedLabel();

        private void ChangeColor_Click(object sender, EventArgs e)
        {
            if (colorDialog1.ShowDialog(this) == DialogResult.OK)
                UpdateColor();
        }
    }
}
