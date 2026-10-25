using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// Shows every drawing of a set in one <see cref="DrawingSetPreviewControl"/>, all at the
    /// same scale, labeled with its name and the set's quantity. Each member's current
    /// revision is downloaded when the window is shown; members that are gone or fail to
    /// load are counted in the header and left out. Closing cancels the downloads.
    /// </summary>
    public class DrawingSetPreviewForm : Form
    {
        private const int Concurrency = 4;

        private readonly DrawingSetRecord set;
        private readonly Func<DrawingLibraryRecord, CancellationToken, Task<Drawing>> load;
        private readonly CancellationTokenSource cancel = new();
        private readonly DrawingSetPreviewControl preview;

        /// <param name="set">The set read with its members' current drawings.</param>
        /// <param name="load">Reads the drawing a record's package holds, or null when the revision is gone.</param>
        public DrawingSetPreviewForm(DrawingSetRecord set, Func<DrawingLibraryRecord, CancellationToken, Task<Drawing>> load)
        {
            this.set = set ?? throw new ArgumentNullException(nameof(set));
            this.load = load ?? throw new ArgumentNullException(nameof(load));

            preview = new DrawingSetPreviewControl { Dock = DockStyle.Fill };
            var closeButton = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
            };
            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(6),
            };
            bottom.Controls.Add(closeButton);
            Controls.Add(preview);
            Controls.Add(bottom);

            CancelButton = closeButton;
            Font = new Font("Segoe UI", 9.75F);
            Text = $"Preview — {set.Name} (revision {set.Revision})";
            StartPosition = FormStartPosition.CenterParent;
            Size = new System.Drawing.Size(1100, 800);
            MinimumSize = new System.Drawing.Size(400, 300);
            ShowIcon = false;
            ShowInTaskbar = false;
        }

        internal DrawingSetPreviewControl Preview => preview;

        /// <summary>Completes once every member has been read and shown; for tests.</summary>
        internal Task Loaded { get; private set; } = Task.CompletedTask;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Loaded = LoadAsync();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            cancel.Cancel();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                cancel.Dispose();
            base.Dispose(disposing);
        }

        internal async Task LoadAsync()
        {
            var members = set.Members;
            if (members.Count == 0)
            {
                preview.SetInfo("This set has no drawings.");
                return;
            }

            var token = cancel.Token;
            var drawings = new Drawing[members.Count];
            var failed = 0;
            preview.SetInfo($"Loading {members.Count} drawings...");

            using var gate = new SemaphoreSlim(Concurrency);
            var reads = members.Select(async (member, i) =>
            {
                if (member.Drawing == null)
                {
                    Interlocked.Increment(ref failed);
                    return;
                }

                await gate.WaitAsync(token);
                try
                {
                    drawings[i] = await load(member.Drawing, token);
                    if (!DrawingSetPreviewControl.CanShow(drawings[i]))
                        Interlocked.Increment(ref failed);
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    // The preview is best-effort: a drawing that fails to load is counted and skipped.
                    Interlocked.Increment(ref failed);
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();

            try
            {
                await Task.WhenAll(reads);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (IsDisposed || token.IsCancellationRequested)
                return;

            preview.ShowDrawings(members.Select((member, i) => (drawings[i], Label(member))));
            preview.SetInfo(Header(members, failed).ToArray());
        }

        private static string Label(DrawingSetMember member) =>
            $"{member.Drawing?.Name} ×{member.Quantity}";

        private IEnumerable<string> Header(IReadOnlyList<DrawingSetMember> members, int failed)
        {
            var parts = members.Sum(m => m.Quantity);
            yield return $"{members.Count} drawings, {parts} parts — all shown at the same scale";

            var units = members.Where(m => m.Drawing != null).Select(m => m.Drawing.Units).Distinct().ToList();
            if (units.Count > 1)
                yield return "This set mixes inches and millimeters; drawings in different units are not to scale with each other.";
            if (failed > 0)
                yield return $"{failed} drawing{(failed == 1 ? "" : "s")} could not be loaded and {(failed == 1 ? "is" : "are")} not shown.";
        }
    }
}
