using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;
using OpenNest.Data.MaterialLedger;
using OpenNest.Engine.Jobs;
using OpenNest.Forms;
using OpenNest.Geometry;

namespace OpenNest.WinForms.Tests.Forms;

public class AutoNestFormLedgerStockTests
{
    private static readonly LedgerStockResult Stock = new LedgerStockResult.Ok(Array.Empty<LedgerStockSpec>(), new[]
    {
        Item("B-1", LedgerItemKinds.FullSheetBundle, 48, 96, 3),
        Item("D-1", LedgerItemKinds.Drop, 24, 36, 1),
        Item("D-2", LedgerItemKinds.Drop, 24, 36, 1),
    });

    [Fact]
    public void LedgerRowsJoinManualRowsAndAreNeverSaved() => RunSta(() =>
    {
        using var form = new LedgerTestForm();
        form.LoadPlateOptions(Manual(0, 0), 0.5);
        GetGrid(form);

        form.ApplyLedgerStock(Stock);

        Assert.True(form.TryGetPlateOptions(out var options, out var saved, out var error), error);
        Assert.Equal(4, options.Count);
        Assert.Equal(new string?[] { null, null, "ledger:1", "ledger:2" }, options.Select(o => o.Id));
        Assert.Equal(new int?[] { null, null, 3, 2 }, options.Select(o => o.Quantity));
        Assert.Equal(24, options[3].Width);
        Assert.Equal(36, options[3].Length);
        Assert.Equal(new[] { 60.0, 72.0 }, saved.Select(o => o.Width));
        Assert.All(saved, o => Assert.Null(o.Quantity));
        Assert.All(saved, o => Assert.Null(o.Id));

        // A second load replaces the ledger rows and keeps the typed ones.
        form.ApplyLedgerStock(new LedgerStockResult.Ok(Array.Empty<LedgerStockSpec>(),
            new[] { Item("S-9", LedgerItemKinds.FullSheet, 60, 144, 1) }));
        Assert.True(form.TryGetPlateOptions(out options, out saved, out error), error);
        Assert.Equal(3, options.Count);
        Assert.Equal("ledger:1", options[2].Id);
        Assert.Equal(144, options[2].Length);
        Assert.Equal(2, saved.Count);
        Assert.Equal("Ledger: 1 sheet size, 0 drops", Label(form).Text);
    });

    [Fact]
    public void QuantitiesReachEngineStock() => RunSta(() =>
    {
        using var form = new LedgerTestForm();
        form.LoadPlateOptions(Manual(0, 0), 0.5);
        var grid = GetGrid(form);
        form.ApplyLedgerStock(Stock);
        var rows = Assert.IsAssignableFrom<IList>(grid.DataSource);
        SetValue(rows[0]!, "Quantity", 4);
        SetValue(rows[3]!, "Quantity", 1);
        SetValue(rows[1]!, "Use", false);

        Assert.True(form.TryGetPlateOptions(out var options, out var saved, out var error), error);
        var stock = NestStockBuilder.FromTemplate(new Plate(new Size(48, 96)), options);

        Assert.Equal(new int?[] { 4, 3, 1 }, stock.Select(s => s.Quantity));
        Assert.Equal(new[] { "option-0", "ledger:1", "ledger:2" }, stock.Select(s => s.Id));
        Assert.Equal(2, saved.Count);
    });

    [Fact]
    public void BlankQuantityCellMeansUnlimited() => RunSta(() =>
    {
        using var form = new LedgerTestForm();
        form.LoadPlateOptions(new List<PlateOption> { new() { Width = 48, Length = 96, Quantity = 2 } }, 0.5);
        var grid = GetGrid(form);
        ((TabControl)form.Controls.Find("tabControl", true).Single()).SelectedIndex = 1;
        form.Show();
        grid.Focus();
        grid.CurrentCell = grid.Rows[0].Cells[QuantityColumn(grid)];
        Assert.True(grid.BeginEdit(false));
        grid.EditingControl.Text = "";

        Assert.True(form.TryGetPlateOptions(out var options, out var error), error);
        Assert.Null(Assert.Single(options).Quantity);
    });

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(2, true)]
    public void LedgerQuantityCannotExceedTheFreeCount(int quantity, bool valid) => RunSta(() =>
    {
        using var form = new LedgerTestForm();
        var grid = GetGrid(form);
        Assert.IsAssignableFrom<IList>(grid.DataSource).Clear();
        form.ApplyLedgerStock(Stock);
        SetValue(Assert.IsAssignableFrom<IList>(grid.DataSource)[0]!, "Quantity", quantity);

        Assert.Equal(valid, form.TryGetPlateOptions(out var options, out var error));
        if (valid)
            Assert.Equal(2, options[0].Quantity);
        else
            Assert.Contains("3 free", error);
    });

    [Fact]
    public void CostsAreIgnoredWhileLedgerStockIsOffered() => RunSta(() =>
    {
        using var form = new LedgerTestForm();
        form.LoadPlateOptions(Manual(25, 40), 0.5);
        var grid = GetGrid(form);
        form.ApplyLedgerStock(Stock);

        Assert.True(form.TryGetPlateOptions(out var options, out var saved, out var error), error);
        Assert.All(options, o => Assert.Equal(0, o.Cost));
        Assert.Equal(new[] { 25.0, 40.0 }, saved.Select(o => o.Cost));
        Assert.NotNull(NestStockBuilder.FromTemplate(new Plate(new Size(48, 96)), options));
        Assert.Contains("costs ignored", Label(form).Text);

        // Unticking every ledger row restores the typed costs for the run.
        var rows = Assert.IsAssignableFrom<IList>(grid.DataSource);
        SetValue(rows[2]!, "Use", false);
        SetValue(rows[3]!, "Use", false);
        Assert.True(form.TryGetPlateOptions(out options, out error), error);
        Assert.Equal(new[] { 25.0, 40.0 }, options.Select(o => o.Cost));
    });

    [Fact]
    public void FailuresAreReportedAndUnmappedClearsLedgerRows() => RunSta(() =>
    {
        using var form = new LedgerTestForm();
        form.LoadPlateOptions(Manual(0, 0), 0.5);
        GetGrid(form);
        form.ApplyLedgerStock(Stock);

        form.ApplyLedgerStock(new LedgerStockResult.Unreachable("No answer within 5 s."));
        Assert.Contains("unreachable", Label(form).Text);
        Assert.Equal(4, form.GetPlateOptions().Count);

        form.ApplyLedgerStock(new LedgerStockResult.Unauthorized());
        Assert.Contains("API key", Label(form).Text);

        form.ApplyLedgerStock(new LedgerStockResult.Unmapped());
        Assert.Equal("MaterialLedger has no mapping for Steel A36 0.25 in.", Label(form).Text);
        Assert.Equal(2, form.GetPlateOptions().Count);
    });

    [Fact]
    public void LoadAsksForTheNestMaterialInInches() => RunSta(() =>
    {
        var nest = LedgerNest();
        nest.Units = Units.Millimeters;
        nest.Thickness = 6.35;
        using var form = new LedgerTestForm(nest) { Reply = Stock };
        GetGrid(form);

        Assert.True(form.LoadLedgerStockAsync().IsCompleted);

        Assert.Equal(("Steel", "A36", 0.25), form.Requests.Single());
        Assert.Equal(25.4 * 48, form.GetPlateOptions().First(o => o.Id == "ledger:1").Width, 9);
    });

    [Fact]
    public void ClosingTheFormCancelsAPendingLoad() => RunSta(() =>
    {
        using var form = new LedgerTestForm { Pending = new TaskCompletionSource<LedgerStockResult>() };
        GetGrid(form);
        var load = form.LoadLedgerStockAsync();
        Assert.False(load.IsCompleted);
        Assert.False(Button(form).Enabled);

        form.Show();
        form.Close();

        Assert.True(form.LastToken.IsCancellationRequested);
    });

    [Fact]
    public void LoadButtonNeedsALedgerAndAMaterialAndThickness() => RunSta(() =>
    {
        using var form = new LedgerTestForm();
        Assert.True(Button(form).Enabled);

        form.Configured = false;
        form.UpdateLedgerButton();
        Assert.False(Button(form).Enabled);

        using var noMaterial = new LedgerTestForm(new Nest { Thickness = 0.25 });
        Assert.False(Button(noMaterial).Enabled);

        using var noThickness = new LedgerTestForm(new Nest { Material = new Material("Steel", "A36") });
        Assert.False(Button(noThickness).Enabled);
    });

    [Fact]
    public void TooltipNamesItemsAndLocations()
    {
        var row = LedgerStockMapper.Map(new[]
        {
            Item("D-1", LedgerItemKinds.Drop, 24, 36, 1, "R1"),
            Item("D-2", LedgerItemKinds.Drop, 24, 36, 1, "R2"),
        }, Units.Inches).Rows.Single();

        var tooltip = AutoNestForm.LedgerTooltip(row);
        Assert.Contains("D-1, D-2", tooltip);
        Assert.Contains("R1, R2", tooltip);
    }

    private sealed class LedgerTestForm : AutoNestForm
    {
        public LedgerTestForm() : this(LedgerNest()) { }

        public LedgerTestForm(Nest nest) : base(nest) { }

        public bool Configured { get; set; } = true;
        public LedgerStockResult Reply { get; set; } = Stock;
        public TaskCompletionSource<LedgerStockResult>? Pending { get; set; }
        public List<(string, string, double)> Requests { get; } = new();
        public CancellationToken LastToken { get; private set; }

        internal override bool LedgerConfigured => Configured;

        internal override Task<LedgerStockResult> FetchLedgerStockAsync(string material, string grade,
            double thicknessIn, CancellationToken cancellationToken)
        {
            Requests.Add((material, grade, thicknessIn));
            LastToken = cancellationToken;
            return Pending?.Task ?? Task.FromResult(Reply);
        }
    }

    private static Nest LedgerNest() => new() { Material = new Material("Steel", "A36"), Thickness = 0.25 };

    private static List<PlateOption> Manual(double firstCost, double secondCost) => new()
    {
        new() { Width = 60, Length = 120, Cost = firstCost },
        new() { Width = 72, Length = 144, Cost = secondCost },
    };

    private static LedgerStockItem Item(string code, string kind, decimal width, decimal length, int quantity,
        string location = "R1") =>
        new(code, 7, kind, width, length, quantity, LedgerItemShapes.Rectangular, location, location, null, null,
            DateTime.UtcNow);

    private static DataGridView GetGrid(AutoNestForm form)
    {
        form.BindingContext = new BindingContext();
        var grid = Assert.IsType<DataGridView>(form.Controls.Find("plateGrid", true).Single());
        grid.CreateControl();
        return grid;
    }

    private static int QuantityColumn(DataGridView grid) =>
        grid.Columns.Cast<DataGridViewColumn>().Single(c => c.DataPropertyName == "Quantity").Index;

    private static Label Label(AutoNestForm form) =>
        Assert.IsType<Label>(form.Controls.Find("ledgerSummaryLabel", true).Single());

    private static Button Button(AutoNestForm form) =>
        Assert.IsType<Button>(form.Controls.Find("ledgerButton", true).Single());

    private static void SetValue(object row, string property, object value) =>
        TypeDescriptor.GetProperties(row)[property]!.SetValue(row, value);

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(15), "The STA test did not complete.");
}
