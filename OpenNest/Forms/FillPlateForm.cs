using System.Drawing;
using System.Windows.Forms;
using OpenNest.Collections;
using OpenNest.Controls;

namespace OpenNest.Forms
{
    public partial class FillPlateForm : Form
    {
        private DrawingCollection Drawings;

        public FillPlateForm(DrawingCollection drawings)
        {
            InitializeComponent();
            Drawings = drawings;
            UpdateDrawingList();
        }

        public Drawing SelectedDrawing { get; protected set; }

        public void UpdateDrawingList()
        {
            tableLayoutPanel1.Controls.Clear();
            tableLayoutPanel1.RowStyles.Clear();
            tableLayoutPanel1.RowCount = Drawings.Count + 1;

            var controls = new PlateView[Drawings.Count];

            int index = 0;

            foreach (var dwg in Drawings)
            {
                var control = new PlateView();
                control.DrawOrigin = false;
                control.AllowPan = false;
                control.AllowSelect = false;
                control.AllowZoom = false;
                control.BackColor = Color.White;
                control.Plate.Size = new OpenNest.Size(0, 0);
                control.AddPartFromDrawing(dwg, Vector.Zero);
                control.MouseDoubleClick += (sender, e) =>
                {
                    SelectedDrawing = control.Plate.Parts.Count > 0 ? control.Plate.Parts[0].BaseDrawing : null;
                    Close();
                };
                control.Dock = DockStyle.Fill;
                controls[index] = control;
                tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
                index++;
            }

            tableLayoutPanel1.Controls.AddRange(controls);
        }
    }
}
