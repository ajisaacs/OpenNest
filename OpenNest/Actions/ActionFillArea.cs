using OpenNest.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenNest.Actions
{
    [DisplayName("Fill Area")]
    public class ActionFillArea : ActionSelectArea
    {
        private Drawing drawing;
        private IProgress<NestProgress> progress;
        private CancellationTokenSource cts;
        private Action<List<Part>> onFillComplete;

        public ActionFillArea(PlateView plateView, Drawing drawing)
            : this(plateView, drawing, null, null, null)
        {
        }

        public ActionFillArea(PlateView plateView, Drawing drawing,
            IProgress<NestProgress> progress, CancellationTokenSource cts,
            Action<List<Part>> onFillComplete)
            : base(plateView)
        {
            plateView.PreviewKeyDown += plateView_PreviewKeyDown;
            this.drawing = drawing;
            this.progress = progress;
            this.cts = cts;
            this.onFillComplete = onFillComplete;
        }

        private void plateView_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                FillArea();
            else if (e.KeyCode == Keys.Escape && cts != null)
                cts.Cancel();
        }

        private async void FillArea()
        {
            if (progress != null && cts != null)
            {
                try
                {
                    var engine = NestEngineRegistry.Create(plateView.Plate);
                    var parts = await Task.Run(() =>
                        engine.Fill(new NestItem { Drawing = drawing },
                            SelectedArea, progress, cts.Token));

                    onFillComplete?.Invoke(parts);
                }
                catch (Exception)
                {
                    onFillComplete?.Invoke(new List<Part>());
                }
            }
            else
            {
                var engine = NestEngineRegistry.Create(plateView.Plate);
                engine.Fill(new NestItem { Drawing = drawing }, SelectedArea);
                plateView.Invalidate();
            }

            Update();
        }

        public override void DisconnectEvents()
        {
            plateView.PreviewKeyDown -= plateView_PreviewKeyDown;
            base.DisconnectEvents();
        }
    }
}
