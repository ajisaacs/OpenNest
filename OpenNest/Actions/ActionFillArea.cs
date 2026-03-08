using System.ComponentModel;
using System.Windows.Forms;
using OpenNest.Controls;

namespace OpenNest.Actions
{
    [DisplayName("Fill Area")]
    public class ActionFillArea : ActionSelectArea
    {
        private Drawing drawing;

        public ActionFillArea(PlateView plateView, Drawing drawing)
            : base(plateView)
        {
            plateView.PreviewKeyDown += plateView_PreviewKeyDown;
            this.drawing = drawing;
        }

        private void plateView_PreviewKeyDown(object sender, System.Windows.Forms.PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                FillArea();
        }

        private void FillArea()
        {
            var engine = new NestEngine(plateView.Plate);
            engine.Fill(new NestItem { Drawing = drawing }, SelectedArea);

            plateView.Invalidate();
            Update();
        }

        public override void DisconnectEvents()
        {
            plateView.PreviewKeyDown -= plateView_PreviewKeyDown;
            base.DisconnectEvents();
        }
    }
}
