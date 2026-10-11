using System.Drawing;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;

namespace OpenNest.Forms;

/// <summary>A detached historical archive viewer; it has no Document or save/post actions.</summary>
public sealed class NestRevisionPreviewForm : Form
{
    private readonly PlateView preview;
    private readonly Label position = new() { AutoSize = true, Margin = new Padding(10) };
    private readonly Button previous = new() { Text = "Previous plate", AutoSize = true };
    private readonly Button next = new() { Text = "Next plate", AutoSize = true };
    private readonly Nest nest;
    private int index = -1;

    public NestRevisionPreviewForm(Nest nest, NestRecord metadata)
    {
        this.nest = nest;
        Text = $"Read-only — {metadata.Name} — Version {metadata.Revision}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 700);
        MinimumSize = new Size(640, 420);
        ShowInTaskbar = false;
        preview = new PlateView
        {
            Dock = DockStyle.Fill,
            AllowSelect = false,
            AllowDrop = false,
            DrawOrigin = false,
            Cursor = Cursors.Default,
            TabStop = false
        };
        preview.SizeChanged += (_, _) => { if (index >= 0) preview.ZoomToFit(); };
        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 60,
            Text = $"Read-only archive: {metadata.Id}, version {metadata.Revision}. No remote document is bound; this window cannot Save, Restore or Post.\nCustomer: {metadata.Customer}; Status: {metadata.Status}; Made by: {metadata.MadeBy}\nComments: {metadata.Comments}",
            AutoEllipsis = true
        };
        previous.Click += (_, _) => ShowPlate(index - 1);
        next.Click += (_, _) => ShowPlate(index + 1);
        var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
        CancelButton = close;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
        bar.Controls.AddRange(new Control[] { previous, position, next, close });
        Controls.Add(preview);
        Controls.Add(heading);
        Controls.Add(bar);
        Shown += (_, _) => ShowPlate(0);
        previous.Enabled = next.Enabled = false;
        if (nest.Plates.Count == 0) position.Text = "No archived plates";
    }

    private void ShowPlate(int selected)
    {
        if (selected < 0 || selected >= nest.Plates.Count) return;
        index = selected;
        preview.Plate = nest.Plates[index];
        preview.ZoomToFit();
        position.Text = $"Plate {index + 1} of {nest.Plates.Count}";
        previous.Enabled = index > 0;
        next.Enabled = index + 1 < nest.Plates.Count;
    }
}
