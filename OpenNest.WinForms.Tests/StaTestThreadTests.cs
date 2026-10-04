using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace OpenNest.WinForms.Tests;

public class StaTestThreadTests
{
    private const string CallbackMessage = "thrown from a queued UI callback";

    [Fact]
    public void QueuedCallbackExceptionFailsTheTestInsteadOfOpeningADialog()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StaTestThread.Run(
                ThrowFromQueuedCallback,
                TimeSpan.FromSeconds(30),
                "The STA test did not complete."
            )
        );

        Assert.Equal(CallbackMessage, error.Message);
    }

    [Fact]
    public void WithoutThrowExceptionModeTheCallbackExceptionNeverReachesTheTestBody()
    {
        // Control: the default mode routes the same exception to the thread's
        // ThreadException handler (or, with none, to a modal dialog), so the
        // test body itself completes without seeing it.
        Exception? routed = null;
        Exception? bodyFailure = null;
        var thread = new Thread(() =>
        {
            Application.ThreadException += (_, e) => routed = e.Exception;
            try
            {
                ThrowFromQueuedCallback();
            }
            catch (Exception ex)
            {
                bodyFailure = ex;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The control STA thread did not complete.");
        if (bodyFailure != null)
            ExceptionDispatchInfo.Capture(bodyFailure).Throw();
        Assert.IsType<InvalidOperationException>(routed);
        Assert.Equal(CallbackMessage, routed!.Message);
    }

    private static void ThrowFromQueuedCallback()
    {
        using var control = new Control();
        _ = control.Handle;
        control.BeginInvoke(new Action(() => throw new InvalidOperationException(CallbackMessage)));
        Application.DoEvents();
    }
}
