using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Sequencing;

namespace OpenNest.Actions
{
    [DisplayName("Set Sequence")]
    public class ActionSetSequence : Action
    {
        private readonly Forms.SequenceForm sequenceForm;
        private readonly Pen pen = new Pen(Color.Blue, 2.0f);
        private LayoutPart hoveredPart;
        private bool disconnecting;

        // Escape ends this tool; it must not resurrect its closed window.
        public override bool ResumeOnEscape => false;

        public ActionSetSequence(PlateView plateView)
            : base(plateView)
        {
            sequenceForm = new Forms.SequenceForm();
            sequenceForm.numericUpDown1.Maximum = System.Math.Max(1, plateView.Plate.Parts.Count);
            sequenceForm.DoneClicked += (_, _) => plateView.EndAction();
            sequenceForm.FormClosed += (_, _) =>
            {
                if (!disconnecting)
                    plateView.EndAction();
            };
            sequenceForm.Show(plateView.FindForm());

            plateView.MouseMove += plateView_MouseMove;
            plateView.MouseLeave += plateView_MouseLeave;
            plateView.MouseClick += plateView_MouseClick;
            plateView.Paint += plateView_Paint;
        }

        private void plateView_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                plateView.EndAction();
                return;
            }
            if (e.Button != MouseButtons.Left)
                return;

            // Hit-test at the click, not at the previous mouse-move location.
            var hit = plateView.GetPartAtControlPoint(e.Location);
            var part = hit?.BasePart;
            if (part == null || !ManualPartSequencing.Move(plateView.Plate, part, (int)sequenceForm.numericUpDown1.Value))
                return;

            if (sequenceForm.numericUpDown1.Value < sequenceForm.numericUpDown1.Maximum)
                sequenceForm.numericUpDown1.Value++;
            plateView.Invalidate();
        }

        private void plateView_MouseMove(object sender, MouseEventArgs e)
        {
            var hit = plateView.GetPartAtControlPoint(e.Location);
            if (ReferenceEquals(hit, hoveredPart))
                return;
            hoveredPart = hit;
            plateView.Invalidate();
        }

        private void plateView_MouseLeave(object sender, EventArgs e)
        {
            hoveredPart = null;
            plateView.Invalidate();
        }

        private void plateView_Paint(object sender, PaintEventArgs e)
        {
            var path = hoveredPart?.SelectionPath;
            if (path != null)
                e.Graphics.DrawPath(pen, path);
        }

        public override void DisconnectEvents()
        {
            disconnecting = true;
            plateView.Paint -= plateView_Paint;
            plateView.MouseMove -= plateView_MouseMove;
            plateView.MouseLeave -= plateView_MouseLeave;
            plateView.MouseClick -= plateView_MouseClick;
            hoveredPart = null;
            sequenceForm.Close();
            sequenceForm.Dispose();
            pen.Dispose();
            plateView.Invalidate();
        }

        public override void CancelAction() { }

        public override bool IsBusy() => false;
    }
}
