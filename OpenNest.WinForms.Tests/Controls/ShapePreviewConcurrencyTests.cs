using System.Drawing;
using System.Windows.Forms;
using OpenNest.CNC;
using OpenNest.Controls;

namespace OpenNest.WinForms.Tests.Controls;

public class ShapePreviewConcurrencyTests
{
    private static Program Square(double size)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(size, 0));
        program.Codes.Add(new LinearMove(size, size));
        program.Codes.Add(new LinearMove(0, size));
        program.Codes.Add(new LinearMove(0, 0));
        return program;
    }

    // Parallel test collections paint previews on separate STA threads at once.
    // GDI+ objects are not thread-safe, so any pen, brush or font shared between
    // views fails with "Object is currently in use elsewhere".
    [Fact]
    public void PreviewsPaintAtTheSameTimeOnSeparateThreads()
    {
        const int threadCount = 4;
        const int paints = 200;
        var start = new Barrier(threadCount);
        var failures = new List<Exception>();
        var threads = new List<Thread>();

        for (var t = 0; t < threadCount; t++)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    using var preview = new ShapePreviewControl { Size = new Size(200, 200) };
                    preview.ShowDrawing(new Drawing("square", Square(10)));
                    preview.SetInfo("10 x 10", "1 part");
                    using var bitmap = new Bitmap(200, 200);

                    start.SignalAndWait(TimeSpan.FromSeconds(10));
                    for (var i = 0; i < paints; i++)
                        preview.DrawToBitmap(bitmap, new Rectangle(0, 0, 200, 200));
                }
                catch (Exception ex)
                {
                    lock (failures)
                        failures.Add(ex);
                }
            })
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA);
            threads.Add(thread);
            thread.Start();
        }

        foreach (var thread in threads)
            Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "A preview thread did not finish.");

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
