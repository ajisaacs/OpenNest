using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace OpenNest.WinForms.Tests;

/// <summary>
/// Runs a test body on its own STA thread and rethrows its failure on the
/// calling test thread.
/// </summary>
/// <remarks>
/// By default WinForms catches an exception raised while dispatching a window
/// message or a BeginInvoke/SynchronizationContext callback and hands it to the
/// thread's ThreadException handler. With none registered it opens a modal
/// ThreadExceptionDialog in an interactive session, which blocks the test until
/// its join timeout, and silently ignores the exception otherwise. Both the
/// unhandled-exception mode and the handler are per-thread, so they cannot be
/// configured once for the assembly: each STA thread selects
/// <see cref="UnhandledExceptionMode.ThrowException"/> before it creates a
/// window, and the exception then propagates to the code pumping messages
/// (Show, DoEvents, ShowDialog) and fails the test.
/// </remarks>
internal static class StaTestThread
{
    public static void Run(Action body, TimeSpan timeout, string timeoutMessage)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(timeout), timeoutMessage);
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
