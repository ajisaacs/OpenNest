using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using OpenNest.Forms;

namespace OpenNest.WinForms.Tests.Forms;

public class LeadInSidePanelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PanelClosesWithoutEscapeReopeningIt(bool useCloseButton) => RunSta(() =>
    {
        using var form = new EditNestForm(new Nest("lead-in panel"));
        form.Show();

        form.PlaceLeadIn_Click(null!, EventArgs.Empty);
        Assert.True(form.IsSidePanelVisible);
        Assert.Equal("Place Lead-in", form.PlateView.Status);

        if (useCloseButton)
        {
            var close = Assert.IsType<Button>(
                Assert.Single(form.Controls.Find("sidePanelCloseButton", true))
            );
            close.PerformClick();
        }
        else
        {
            form.PlateView.ProcessEscapeKey();
        }

        Assert.False(form.IsSidePanelVisible);
        Assert.Equal("Select", form.PlateView.Status);

        // Escape from Select normally resumes the previous action; it must not
        // bring back a panel the user just closed.
        form.PlateView.ProcessEscapeKey();
        Assert.False(form.IsSidePanelVisible);
        Assert.Equal("Select", form.PlateView.Status);
    });

    private static void RunSta(System.Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The STA test did not complete.");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
