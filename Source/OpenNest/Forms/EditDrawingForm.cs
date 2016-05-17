using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class EditDrawingForm : Form
    {
        private Drawing drawing;

        public EditDrawingForm()
        {
            InitializeComponent();
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

        private Image DrawingImage
        {
            get { return pictureBox1.Image; }
            set { pictureBox1.Image = value; }
        }

        private void UpdateImage()
        {
            var brush = new SolidBrush(colorDialog1.Color);
            var pen = new Pen(ControlPaint.Dark(colorDialog1.Color));
            DrawingImage = drawing.Program.GetImage(pictureBox1.Size, pen, brush);

            pen.Dispose();
            brush.Dispose();
        }

        public void LoadDrawing(Drawing drawing)
        {
            this.drawing = drawing;

            colorDialog1.Color = drawing.Color;
            DrawingName = drawing.Name;
            Customer = drawing.Customer;
            Quantity = drawing.Quantity.Required;
            Priority = drawing.Priority;
            UpdateImage();
        }

        public void SaveDrawing(Drawing drawing)
        {
            drawing.Name = DrawingName;
            drawing.Customer = Customer;
            drawing.Quantity.Required = Quantity;
            drawing.Priority = Priority;
            drawing.Color = colorDialog1.Color;
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var result = colorDialog1.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK)
                UpdateImage();
        }
    }
}
