using System.Windows.Forms;
using OpenNest.Engine.Jobs;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

public class AutoNestFormPlateSourceTests
{
    [Fact]
    public void OpensOnNewPlates() => RunSta(() =>
    {
        using var form = new AutoNestForm(new Nest());

        Assert.Equal(NestPlateSource.New, form.PlateSource);
    });

    [Theory]
    [InlineData(NestPlateSource.Current)]
    [InlineData(NestPlateSource.Both)]
    public void CurrentPlatesAreNeverNestedOnTheServer(NestPlateSource source) => RunSta(() =>
    {
        using var form = new AutoNestForm(new Nest()) { ServerNestingAvailable = true };
        var box = Assert.IsType<CheckBox>(form.Controls.Find("nestOnServerBox", true).Single());
        box.Checked = true;

        form.PlateSource = source;

        Assert.False(box.Enabled);
        Assert.False(form.NestOnServer);
        form.PlateSource = NestPlateSource.New;
        Assert.Equal(box.Checked, form.NestOnServer);
    });

    [Fact]
    public void NestOnServerIsReadableWhileTheFormIsHidden() => RunSta(() =>
    {
        // AutoNestCommand reads the choice after ShowDialog returns, when every control
        // reports Visible == false.
        using var form = new AutoNestForm(new Nest()) { ServerNestingAvailable = true };
        var box = Assert.IsType<CheckBox>(form.Controls.Find("nestOnServerBox", true).Single());
        box.Checked = true;

        Assert.True(form.ServerNestingAvailable);
        Assert.True(form.NestOnServer);
        box.Checked = false;
        Assert.False(form.NestOnServer);
        form.ServerNestingAvailable = false;
        box.Checked = true;
        Assert.False(form.ServerNestingAvailable);
        Assert.False(form.NestOnServer);
    });

    [Fact]
    public void CurrentPlatesOnlyOffersNoStockOptions() => RunSta(() =>
    {
        using var form = new AutoNestForm(new Nest()) { OptimizePlateSize = true };

        form.PlateSource = NestPlateSource.Current;
        Assert.False(form.OptimizePlateSize);

        form.PlateSource = NestPlateSource.Both;
        Assert.True(form.OptimizePlateSize);
    });

    [Fact]
    public void ReviewShowsPartsAlreadyPlacedWithoutCountingThem() => RunSta(() =>
    {
        var drawing = new Drawing("bracket", NestReviewFormTests.Rectangle());
        var stock = new NestPlateStock("existing", new Size(48, 96), quantity: 1, partSpacing: 0.25);
        var sheet = new ProposedPlate(0, stock, new[] { new Part(drawing, new Vector(20, 1)) })
        {
            Occupants = new[] { new Part(drawing, new Vector(1, 1)), new Part(drawing, new Vector(1, 30)) },
            ExistingPlateNumber = 2,
        };

        using var form = new NestReviewForm("Review", new[] { sheet }, 0,
            Array.Empty<NestUnplacedPart>(), ColorScheme.Default);

        Assert.Equal(1, form.Strip.PartCount);
        Assert.Equal(2, form.Strip.ExistingPartCount);
        // Three 10 x 20 parts now fill the sheet's material.
        Assert.Equal(600.0 / (48 * 96), form.Strip.Utilization, 6);
        Assert.Contains("already on the plates (2)", form.Summary);
    });

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(15), "The STA test did not complete.");
}
