using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace OpenNest.WinForms.Tests;

internal static class TestAssemblySetup
{
    // SetUnhandledExceptionMode only changes which *default* dialog style WinForms would
    // show; it still shows one. A registered ThreadException handler is what actually
    // suppresses it (WinForms always prefers the handler over the default dialog), which
    // matters here because none of our tests call Application.Run() to install one.
    // Without this, an exception thrown inside a WndProc callback (e.g. a DataGridView
    // commit failure during Show()) pops a modal ThreadExceptionDialog that blocks the
    // desktop until someone dismisses it by hand, instead of failing the test normally.
    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.ThreadException += (_, e) =>
            Console.Error.WriteLine($"Suppressed ThreadExceptionDialog: {e.Exception}");
    }
}
