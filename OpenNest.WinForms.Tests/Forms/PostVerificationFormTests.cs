using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class PostVerificationFormTests
{
#pragma warning disable xUnit1031 // Dedicated STA; only await completed tasks, or explicitly pump until completion.
    [Fact]
    public void WarningsRequireFreshAcknowledgmentAndUncheckingRevokesPermission()
    {
        RunSta(() =>
        {
            var nest = WarningNest();
            using var form = new PostVerificationForm(nest,
                (_, _) => Task.FromResult(PostVerificationAnalyzer.Analyze(nest)));
            form.VerifyAsync().GetAwaiter().GetResult();
            var consent = Control<CheckBox>(form, "acknowledgeBox");
            var post = Control<Button>(form, "postButton");
            Assert.False(consent.Checked);
            Assert.False(post.Enabled);
            Invoke(form, "PostButton_Click", null, EventArgs.Empty);
            Assert.NotEqual(DialogResult.OK, form.DialogResult);
            consent.Checked = true;
            Assert.True(post.Enabled);
            consent.Checked = false;
            Assert.False(post.Enabled);
            consent.Checked = true;
            Invoke(form, "PostButton_Click", null, EventArgs.Empty);
            Assert.Equal(DialogResult.OK, form.DialogResult);

            using var retry = new PostVerificationForm(nest,
                (_, _) => Task.FromResult(PostVerificationAnalyzer.Analyze(nest)));
            retry.VerifyAsync().GetAwaiter().GetResult();
            Assert.False(Control<CheckBox>(retry, "acknowledgeBox").Checked);
            Assert.False(Control<Button>(retry, "postButton").Enabled);
        });
    }

    [Fact]
    public void ClearReportIsDisplayedAndCancelIsDefault()
    {
        RunSta(() =>
        {
            var nest = WarningNest();
            foreach (var move in nest.Plates[0].Parts[0].Program.Codes.OfType<LinearMove>())
                move.Layer = LayerType.Scribe;
            foreach (var move in nest.Plates[0].Parts[0].BaseDrawing.Program.Codes.OfType<LinearMove>())
                move.Layer = LayerType.Scribe;
            using var form = new PostVerificationForm(nest,
                (_, _) => Task.FromResult(PostVerificationAnalyzer.Analyze(nest)));
            form.VerifyAsync().GetAwaiter().GetResult();
            Assert.NotEmpty(Control<TextBox>(form, "reportBox").Text);
            Assert.True(Control<Button>(form, "postButton").Enabled);
            Assert.False(Control<CheckBox>(form, "acknowledgeBox").Enabled);
            Assert.Equal("Continue Posting", Control<Button>(form, "postButton").Text);
            Assert.Same(Control<Button>(form, "cancelButton"), form.AcceptButton);
            Assert.Same(Control<Button>(form, "cancelButton"), form.CancelButton);
        });
    }

    [Fact]
    public void UnexpectedFailureCannotBeOverridden()
    {
        RunSta(() =>
        {
            using var form = new PostVerificationForm(WarningNest(),
                (_, _) => Task.FromException<PostVerificationReport>(new InvalidOperationException("test failure")));
            form.VerifyAsync().GetAwaiter().GetResult();
            Control<CheckBox>(form, "acknowledgeBox").Checked = true;
            Assert.False(Control<Button>(form, "postButton").Enabled);
            Assert.Contains("test failure", Control<TextBox>(form, "reportBox").Text);
            Invoke(form, "PostButton_Click", null, EventArgs.Empty);
            Assert.NotEqual(DialogResult.OK, form.DialogResult);
        });
    }

    [Fact]
    public void DisposalIsIdempotent()
    {
        RunSta(() =>
        {
            var form = new PostVerificationForm(WarningNest());
            form.Dispose();
            form.Dispose();
        });
    }

    [Fact]
    public void ClosingDuringVerificationWaitsForWorkerAndDiscardsLateResult()
    {
        RunSta(() =>
        {
            var pending = new TaskCompletionSource<PostVerificationReport>();
            var nest = WarningNest();
            var token = CancellationToken.None;
            using var form = new PostVerificationForm(nest, (_, cancellation) =>
            {
                token = cancellation;
                return pending.Task;
            });
            var work = form.VerifyAsync();
            try
            {
                var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
                Invoke(form, "OnFormClosing", closing);
                Assert.True(closing.Cancel);
                Assert.True(token.IsCancellationRequested);
                Assert.False(work.IsCompleted);
                pending.SetResult(PostVerificationAnalyzer.Analyze(nest));
                var until = DateTime.UtcNow.AddSeconds(10);
                while (!work.IsCompleted && DateTime.UtcNow < until)
                    Application.DoEvents();
                Assert.True(work.IsCompleted);
                work.GetAwaiter().GetResult();
                Assert.NotEqual(DialogResult.OK, form.DialogResult);
            }
            finally
            {
                pending.TrySetCanceled();
            }
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
            using var form = new PostVerificationForm(WarningNest(), (_, _) => pending.Task);
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
                        Control<Button>(form, "cancelButton").PerformClick();
                    else if (action == "Escape")
                        Invoke(form, "ProcessDialogKey", Keys.Escape);
                    else
                        form.Close();

                    // ShowDialog must stay active while the worker ignores cancellation.
                    Assert.True(form.Visible);
                    Assert.False(IsWindowEnabled(owner.Handle));
                    Assert.False(Control<Button>(form, "postButton").Enabled);
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

    private static Nest WarningNest()
    {
        var nest = new Nest("verification");
        var program = new CNC.Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(5, 0));
        program.Codes.Add(new LinearMove(5, 5));
        program.Codes.Add(new LinearMove(0, 5));
        program.Codes.Add(new LinearMove(0, 0));
        nest.CreatePlate().Parts.Add(new Part(new Drawing("square", program)));
        return nest;
    }

    private static T Control<T>(Form form, string name) where T : Control =>
        Assert.IsType<T>(form.Controls.Find(name, true).Single());

    private static void Invoke(Form form, string method, params object?[] arguments) =>
        form.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, arguments);

    private static void RunSta(System.Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(30), "STA verification test timed out.");
}
