using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Diagnostics;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

public class ValidateNestFormTests
{
    [Fact]
    public void RapidContactCrosshairIsDrawnAtContactAndClearedWithFinding()
    {
        RunSta(() =>
        {
            using var view = new PlateView();
            view.ShowValidationFinding(new PostVerificationFinding(PostVerificationKind.RapidCrossing,
                1, 2, 1, "crossing")
            {
                RapidStart = new Vector(-1, 0),
                RapidEnd = new Vector(1, 0),
                ContactPoints = Array.AsReadOnly(new[] { Vector.Zero })
            });
            using var bitmap = new Bitmap(120, 120);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.TranslateTransform(60, 60);
            graphics.Clear(Color.Black);
            view.ValidationOverlay.Draw(graphics);
            // Off the horizontal rapid, on the contact's vertical crosshair.
            var pixel = bitmap.GetPixel(60, 65);
            Assert.True(pixel.R > 150 && pixel.B > 50 && pixel.G < 100);
            view.ClearValidationFinding();
            graphics.Clear(Color.Black);
            view.ValidationOverlay.Draw(graphics);
            Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(60, 65).ToArgb());
        });
    }

#pragma warning disable xUnit1031 // Completed tasks on a dedicated STA thread.
    [Fact]
    public void GridNavigatesMainViewAndRestoresViewportOnDisposal()
    {
        RunSta(() =>
        {
            var nest = new Nest("interactive inspection");
            var program = new CNC.Program();
            program.MoveTo(0, 0);
            program.LineTo(5, 0);
            program.LineTo(5, 5);
            program.LineTo(0, 5);
            program.LineTo(0, 0);
            nest.CreatePlate().Parts.Add(new Part(new Drawing("first", program)));
            var second = nest.CreatePlate();
            second.Parts.Add(new Part(new Drawing("second", program)));
            second.Parts.Add(new Part(second.Parts[0].BaseDrawing) { Location = new Vector(2, 2) });
            using var editor = new EditNestForm(nest);
            editor.Show();
            editor.PlateManager.LoadAt(0);
            var view = editor.PlateView;
            view.ZoomToArea(-10, -10, 40, 40);
            var viewport = view.CaptureViewport();
            view.OverlapDisplay = OverlapDisplayMode.Centroids;
            using (var form = new ValidateNestForm(editor,
                (value, token) => Task.FromResult(PostVerificationAnalyzer.Analyze(value, token))))
            {
                form.Show(editor);
                form.ValidateAsync().GetAwaiter().GetResult();
                Assert.True(form.TopLevel);
                Assert.False(form.Modal);
                Assert.True(editor.Enabled);
                Assert.True(view.Enabled);
                Assert.True(view.InspectionOnly);
                var grid = Find<DataGridView>(form, "warningGrid");
                Assert.True(grid.ReadOnly);
                Assert.Equal(DataGridViewSelectionMode.FullRowSelect, grid.SelectionMode);
                var row = grid.Rows.Cast<DataGridViewRow>().Single(row =>
                    row.Tag is PostVerificationFinding { Kind: PostVerificationKind.Overlap });
                grid.CurrentCell = row.Cells[0];
                Assert.Same(second, view.Plate);
                Assert.Equal(1, editor.PlateManager.CurrentIndex);
                Assert.NotNull(view.ValidationOverlay.Finding.Overlap);
                Assert.True(view.ValidationOverlay.IsAnimating);
                using var bitmap = new Bitmap(view.Width, view.Height);
                view.DrawToBitmap(bitmap, view.ClientRectangle);
                Assert.Contains("centroid", Find<TextBox>(form, "reportBox").Text);
                view.SelectAll();
                var scale = view.ViewScale;
                Invoke(view, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 100, 100, 120));
                Assert.True(view.ViewScale > scale);
                var pan = view.CaptureViewport();
                Invoke(view, "OnMouseMove", new MouseEventArgs(MouseButtons.Middle, 0, 120, 110, 0));
                Assert.NotEqual(pan.Origin, view.CaptureViewport().Origin);
                Invoke(view, "OnKeyDown", new KeyEventArgs(Keys.Delete));
                Invoke(view, "ProcessDialogKey", Keys.Right);
                Invoke(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Middle, 1, 120, 110, 0));
                Invoke(view, "OnMouseUp", new MouseEventArgs(MouseButtons.Middle, 1, 120, 110, 0));
                Assert.Equal(2, second.Parts.Count);
                Assert.All(second.Parts, part => Assert.Equal(0, part.Rotation));
            }
            Assert.Same(nest.Plates[0], view.Plate);
            Assert.Equal(viewport, view.CaptureViewport());
            Assert.Equal(OverlapDisplayMode.Centroids, view.OverlapDisplay);
            Assert.Null(view.ValidationOverlay.Finding);
            Assert.False(view.ValidationOverlay.IsAnimating);
            Assert.False(view.InspectionOnly);
            Assert.Equal(new Vector(2, 2), second.Parts[1].Location);
        });
    }

    [Fact]
    public void ModelessCancellationKeepsEditingLockedUntilWorkerStops()
    {
        RunSta(() =>
        {
            var pending = new TaskCompletionSource<PostVerificationReport>();
            using var editor = new EditNestForm(new Nest("inspection"));
            editor.Show();
            using var form = new ValidateNestForm(editor, (_, _) => pending.Task);
            form.Show(editor);
            var validation = form.ValidateAsync();
            Assert.False(validation.IsCompleted);
            Assert.True(editor.Enabled);
            Assert.True(editor.PlateView.Enabled);
            Assert.True(editor.PlateView.InspectionOnly);
            form.Close();
            Assert.True(form.Visible);
            Assert.True(editor.PlateView.InspectionOnly);
            pending.SetCanceled();
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!form.IsDisposed && timeout.Elapsed < TimeSpan.FromSeconds(5))
                Application.DoEvents();
            Assert.True(form.IsDisposed);
            Assert.False(editor.PlateView.InspectionOnly);
        });
    }

    [Fact]
    public void PlateSwitchClearsAnimation()
    {
        RunSta(() =>
        {
            using var view = new PlateView();
            view.ShowValidationFinding(new PostVerificationFinding(PostVerificationKind.MissingLeadIn,
                1, 1, null, "missing")
            { Location = new Vector(3, 4) });
            Assert.True(view.ValidationOverlay.IsAnimating);
            view.Plate = new Plate();
            Assert.False(view.ValidationOverlay.IsAnimating);
            Assert.Null(view.ValidationOverlay.Finding);
        });
    }

    [Fact]
    public void ChecksAllPlatesWithoutChangingProgramsOrPlacements()
    {
        RunSta(() =>
        {
            var nest = new Nest("PEP inspection");
            var program = new CNC.Program();
            program.Codes.Add(new RapidMove(0, 0));
            program.Codes.Add(new LinearMove(5, 0));
            program.Codes.Add(new LinearMove(5, 5));
            program.Codes.Add(new LinearMove(0, 5));
            program.Codes.Add(new LinearMove(0, 0));
            var first = new Part(new Drawing("square", program));
            var second = new Part(first.BaseDrawing);
            nest.CreatePlate().Parts.Add(first);
            nest.CreatePlate().Parts.Add(second);
            var firstProgram = first.Program;
            var secondProgram = second.Program;
            var firstCodes = firstProgram.Codes.ToArray();
            var location = first.Location;
            var rotation = first.Rotation;
            using var form = new ValidateNestForm(nest,
                (value, token) => Task.FromResult(PostVerificationAnalyzer.Analyze(value, token)));

            form.ValidateAsync().GetAwaiter().GetResult();

            var report = Find<TextBox>(form, "reportBox");
            Assert.True(report.ReadOnly);
            Assert.Contains("Plate 1, part 1", report.Text);
            Assert.Contains("Plate 2, part 1", report.Text);
            Assert.DoesNotContain("Post processor", report.Text);
            Assert.Contains("Warnings found", Find<Label>(form, "summaryLabel").Text);
            Assert.Same(first, nest.Plates[0].Parts[0]);
            Assert.Same(second, nest.Plates[1].Parts[0]);
            Assert.Same(firstProgram, first.Program);
            Assert.Same(secondProgram, second.Program);
            Assert.Equal(firstCodes, first.Program.Codes.ToArray());
            Assert.Equal(location, first.Location);
            Assert.Equal(rotation, first.Rotation);
            Assert.Same(Find<Button>(form, "closeButton"), form.AcceptButton);
            Assert.Same(form.AcceptButton, form.CancelButton);
            Assert.Empty(form.Controls.Find("postButton", true));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DistinguishesClearAndIncompleteChecks(bool incomplete)
    {
        RunSta(() =>
        {
            var nest = new Nest("inspection");
            if (incomplete)
                nest.CreatePlate().Parts.Add(new Part(new Drawing("invalid", new CNC.Program())));
            using var form = new ValidateNestForm(nest,
                (value, token) => Task.FromResult(PostVerificationAnalyzer.Analyze(value, token)));
            form.ValidateAsync().GetAwaiter().GetResult();
            Assert.Contains(incomplete ? "Validation incomplete" : "No warnings found",
                Find<Label>(form, "summaryLabel").Text);
        });
    }

    [Fact]
    public void FailureIsReportedWithoutAnAllClear()
    {
        RunSta(() =>
        {
            using var form = new ValidateNestForm(new Nest("inspection"),
                (_, _) => Task.FromException<PostVerificationReport>(new InvalidOperationException("test failure")));
            form.ValidateAsync().GetAwaiter().GetResult();
            Assert.Contains("Validation failed", Find<Label>(form, "summaryLabel").Text);
            Assert.Contains("test failure", Find<TextBox>(form, "reportBox").Text);
            Assert.Equal("Close", Find<Button>(form, "closeButton").Text);
        });
    }

    [Theory]
    [InlineData("Cancel")]
    [InlineData("Escape")]
    [InlineData("Close")]
    public void RealModalCancellationKeepsOwnerDisabledUntilWorkerStops(string action)
    {
        RunSta(() =>
        {
            var pending = new TaskCompletionSource<PostVerificationReport>();
            using var owner = new Form();
            using var form = new ValidateNestForm(new Nest("inspection"), (_, _) => pending.Task);
            Exception? failure = null;
            owner.Show();
            using var timeout = new System.Windows.Forms.Timer { Interval = 10000 };
            timeout.Tick += (_, _) =>
            {
                failure ??= new TimeoutException("Modal cancellation did not finish.");
                pending.TrySetCanceled();
                form.Close();
            };
            form.Shown += (_, _) => form.BeginInvoke(new System.Action(() =>
            {
                try
                {
                    Assert.False(IsWindowEnabled(owner.Handle));
                    if (action == "Cancel")
                        Find<Button>(form, "closeButton").PerformClick();
                    else if (action == "Escape")
                        Invoke(form, "ProcessDialogKey", Keys.Escape);
                    else
                        form.Close();

                    // ShowDialog must stay active while the worker ignores cancellation.
                    Assert.True(form.Visible);
                    Assert.False(IsWindowEnabled(owner.Handle));
                    Assert.False(Find<Button>(form, "closeButton").Enabled);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    pending.TrySetCanceled();
                }
            }));
            timeout.Start();
            var result = form.ShowDialog(owner);
            timeout.Stop();
            Assert.Equal(DialogResult.Cancel, result);
            Assert.True(IsWindowEnabled(owner.Handle));
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    private static void Invoke(Control form, string method, params object?[] arguments) =>
        form.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(form, arguments);

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(30), "STA validation test timed out.");

    private static T Find<T>(Form form, string name) where T : Control =>
        Assert.IsType<T>(form.Controls.Find(name, true).Single());
}
