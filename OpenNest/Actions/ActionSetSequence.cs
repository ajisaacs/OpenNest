using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Converters;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.Actions
{
    [DisplayName("Set Sequence")]
    public class ActionSetSequence : Action
    {
        private readonly SequenceForm SequenceForm;
        private readonly List<Pair> ShapePartPairs;
        private readonly Pen pen;

        private Shape ClosestShape;
        private Part ClosestPart;
        private int sequenceNumber = 1;

        public int SequenceNumber
        {
            get { return sequenceNumber; }
            set
            {
                if (value <= SequenceForm.numericUpDown1.Maximum && value >= SequenceForm.numericUpDown1.Minimum)
                {
                    sequenceNumber = value;
                    SequenceForm.numericUpDown1.Value = sequenceNumber;
                }
            }
        }

        public ActionSetSequence(PlateView plateView)
            : base(plateView)
        {
            SequenceForm = new Forms.SequenceForm();
            SequenceForm.numericUpDown1.DataBindings.Add("Value", this, "SequenceNumber", false, DataSourceUpdateMode.OnPropertyChanged);
            SequenceForm.numericUpDown1.Maximum = plateView.Plate.Parts.Count;
            SequenceForm.Owner = Application.OpenForms[0];
            SequenceForm.Show();
            plateView.Invalidate();

            pen = new Pen(Color.Blue, 2.0f);

            ShapePartPairs = new List<Pair>();

            foreach (var part in plateView.Plate.Parts)
            {
                var entities = ConvertProgram.ToGeometry(part.Program).Where(e => e.Layer == SpecialLayers.Cut).ToList();
                entities.ForEach(entity => entity.Offset(part.Location));
                var shapes = ShapeBuilder.GetShapes(entities);
                var shape = new Shape();
                shape.Entities.AddRange(shapes);
                ShapePartPairs.Add(new Pair() { Part = part, Shape = shape });
            }

            plateView.MouseMove += plateView_MouseMove;
            plateView.MouseClick += plateView_MouseClick;
            plateView.Paint += plateView_Paint;

            SequenceNumber = 1;
        }

        private void plateView_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
                CancelAction();

            if (e.Button != MouseButtons.Left)
                return;

            if (SequenceNumber > plateView.Plate.Parts.Count)
                SequenceNumber = plateView.Plate.Parts.Count;

            if (ClosestPart == null)
                return;

            plateView.Plate.Parts.Remove(ClosestPart);
            plateView.Plate.Parts.Insert(SequenceNumber - 1, ClosestPart);

            SequenceNumber++;

            plateView.Invalidate();
        }

        private void plateView_MouseMove(object sender, MouseEventArgs e)
        {
            var pair = GetClosestShape();
            ClosestShape = pair.HasValue ? pair.Value.Shape : null;
            ClosestPart = pair.HasValue ? pair.Value.Part : null;
            plateView.Invalidate();
        }

        private void plateView_Paint(object sender, PaintEventArgs e)
        {
            if (ClosestShape == null) return;

            var path = ClosestShape.GetGraphicsPath();
            path.Transform(plateView.Matrix);
            e.Graphics.DrawPath(pen, path);
            path.Dispose();
        }

        private Pair? GetClosestShape()
        {
            Pair? closestShape = null;
            double distance = double.MaxValue;

            for (int i = 0; i < ShapePartPairs.Count; i++)
            {
                var shape = ShapePartPairs[i];
                var closestPt = shape.Shape.ClosestPointTo(plateView.CurrentPoint);
                var distance2 = closestPt.DistanceTo(plateView.CurrentPoint);

                if (distance2 < distance)
                {
                    closestShape = shape;
                    distance = distance2;
                }
            }

            return closestShape;
        }

        public override void DisconnectEvents()
        {
            plateView.Paint -= plateView_Paint;
            plateView.MouseMove -= plateView_MouseMove;
            plateView.MouseClick -= plateView_MouseClick;
            plateView.Invalidate();

            SequenceForm.Close();
        }

        public override void CancelAction()
        {
            SequenceNumber = 1;
            SequenceForm.numericUpDown1.Value = SequenceNumber + 1;
        }

        public override bool IsBusy()
        {
            return false;
        }

        private struct Pair
        {
            public Part Part;
            public Shape Shape;
        }
    }
}
