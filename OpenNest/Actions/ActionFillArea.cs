using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Engine;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Forms;

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
            : this(plateView, drawing, null, null, null) { }

        public ActionFillArea(
            PlateView plateView,
            Drawing drawing,
            IProgress<NestProgress> progress,
            CancellationTokenSource cts,
            Action<List<Part>> onFillComplete
        )
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
            var strategy = EngineSelection.FillStrategy;
            if (progress != null && cts != null)
            {
                try
                {
                    var parts = await Task.Run(() =>
                        PlateFillService.FillItem(
                            strategy,
                            plateView.Plate,
                            new NestItem { Drawing = drawing },
                            SelectedArea,
                            progress,
                            cts.Token
                        )
                    );

                    onFillComplete?.Invoke(parts);
                }
                catch (Exception)
                {
                    onFillComplete?.Invoke(new List<Part>());
                }
            }
            else
            {
                var parts = PlateFillService.FillItem(
                    strategy,
                    plateView.Plate,
                    new NestItem { Drawing = drawing },
                    SelectedArea,
                    null,
                    CancellationToken.None
                );
                plateView.Plate.Parts.AddRange(parts);
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
