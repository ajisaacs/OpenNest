using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class NestValidationFormTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DiscardIsDefaultAndMalformedOutputCannotBeKept(bool canKeep)
    {
        RunSta(() =>
        {
            using var form = new NestValidationForm(new[] { "part A and part B violate spacing" }, canKeep);
            var buttons = form.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().ToArray();
            var discard = Assert.Single(buttons, b => b.Text == "Discard");
            var keep = Assert.Single(buttons, b => b.Text == "Keep anyway");
            Assert.Same(discard, form.AcceptButton);
            Assert.Same(discard, form.CancelButton);
            Assert.Equal(canKeep, keep.Enabled);
            Assert.Contains("violate spacing", form.Controls.OfType<TextBox>().Single().Text);
        });
    }

    private sealed class TestProgress(CancellationTokenSource cts) : NestProgressForm(cts)
    {
        public FormClosingEventArgs RequestClose()
        {
            var args = new FormClosingEventArgs(CloseReason.UserClosing, false);
            OnFormClosing(args);
            return args;
        }
    }

    [Fact]
    public void ClosingWholeJobProgressWaitsForWorkerAndRequestsCancellation()
    {
        RunSta(() =>
        {
            using var cts = new CancellationTokenSource();
            using var form = new TestProgress(cts) { HoldOpenUntilCompleted = true };
            Assert.True(form.Controls.Find("stopButton", true).Single().Enabled);
            Assert.True(form.RequestClose().Cancel);
            Assert.True(cts.IsCancellationRequested);
            form.ShowCompleted();
            Assert.False(form.RequestClose().Cancel);
        });
    }

    [Fact]
    public void CompletionClosesWithoutCancellingSuccessfulJob()
    {
        RunSta(() =>
        {
            using var cts = new CancellationTokenSource();
            using var form = new TestProgress(cts) { HoldOpenUntilCompleted = true };
            form.ShowCompleted();
            Assert.False(form.RequestClose().Cancel);
            Assert.False(cts.IsCancellationRequested);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
}
