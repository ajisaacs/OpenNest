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
    public void LoadLeavesOutCodesOnOtherOpenNests() => RunSta(() =>
    {
        var editing = Guid.NewGuid();
        using var form = new LedgerTestForm
        {
            ServerNestId = editing,
            InUse = LedgerCodesInUse.Known([Use("B-1", "2026-0042"), Use("D-1", "2026-0043")]),
        };
        GetGrid(form);

        Assert.True(form.LoadLedgerStockAsync().IsCompleted);

        Assert.Equal(editing, form.ExcludedNestId);
        var row = Assert.Single(form.LedgerRows);
        Assert.Equal(["D-2"], row.ItemCodes);
        Assert.Equal(1, row.Quantity);
        Assert.Equal("Ledger: 0 sheet sizes, 1 drop (4 on other nests)", Label(form).Text);
    });

    [Fact]
    public void LoadWithoutOtherNests_OffersEverythingAndSaysSo() => RunSta(() =>
    {
        using var form = new LedgerTestForm { InUse = LedgerCodesInUse.Unknown("the nest server could not be reached") };
        GetGrid(form);

        Assert.True(form.LoadLedgerStockAsync().IsCompleted);

        Assert.Equal(new[] { 3, 2 }, form.LedgerRows.Select(r => r.Quantity));
        Assert.Equal("Ledger: 1 sheet size, 2 drops (other nests not checked: the nest server could not be reached)",
            Label(form).Text);
    });

    [Fact]
    public void EveryRowOnOtherNests_IsNotReportedAsNoStock() => RunSta(() =>
    {
        using var form = new LedgerTestForm
        {
            InUse = LedgerCodesInUse.Known([Use("B-1", "A"), Use("D-1", "A"), Use("D-2", "A")]),
        };
        GetGrid(form);

        Assert.True(form.LoadLedgerStockAsync().IsCompleted);

        Assert.Empty(form.LedgerRows);
        Assert.Equal("Ledger: 0 sheet sizes, 0 drops (5 on other nests)", Label(form).Text);
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

    [Fact]
    public void RankedRowsShowTheirGradeAndSubstitutesAreMarkedButTicked() => RunSta(() =>
    {
        using var form = new LedgerTestForm(new Nest { Material = new Material("Steel", "HR"), Thickness = 0.0598 });
        var grid = GetGrid(form);
        Assert.IsAssignableFrom<IList>(grid.DataSource).Clear();
        form.ApplyLedgerStock(new LedgerStockResult.Ok(new[]
        {
            Spec(11, 1, "A1011", "HR"), Spec(12, 2, "A1011", "P&O"),
        }, new[]
        {
            Item("H-1", LedgerItemKinds.FullSheet, 48, 96, 1) with { MaterialSpecId = 11, Rank = 1 },
            Item("P-1", LedgerItemKinds.Drop, 24, 36, 1, "B3") with { MaterialSpecId = 12, Rank = 2 },
        }));

        var rows = Assert.IsAssignableFrom<IList>(grid.DataSource);
        Assert.Equal(new[] { "A1011 HR", "A1011 P&O for HR" }, rows.Cast<object>().Select(r => (string)GetValue(r, "Source")));
        Assert.All(rows.Cast<object>(), r => Assert.True((bool)GetValue(r, "Use")));
        Assert.Equal(new[] { "ledger:1", "ledger:2" }, form.GetPlateOptions().Select(o => o.Id));
        Assert.Contains("1 substitute row", Label(form).Text);

        var source = grid.Columns.Cast<DataGridViewColumn>().Single(c => c.DataPropertyName == "Source").Index;
        Assert.Equal(System.Drawing.SystemColors.GrayText, FormattedForeColor(grid, source, 0));
        Assert.Equal(AutoNestForm.SubstituteColor, FormattedForeColor(grid, source, 1));

        var tooltip = AutoNestForm.LedgerTooltip(form.LedgerRows[1], "HR");
        Assert.StartsWith("Substitute: MaterialLedger accepts A1011 P&O in place of HR (rank 2).", tooltip);
        Assert.Contains("Items: P-1", tooltip);
        Assert.Contains("Locations: B3", tooltip);
    });

    [Fact]
    public void SourceTextFallsBackForUnlabeledAndIrregularRows()
    {
        var plain = new LedgerStockRow("ledger:1", new Size(24, 36), 1, true, false, new[] { "D-1" }, new[] { "R1" });
        Assert.Equal("Ledger", AutoNestForm.LedgerSource(plain, "HR"));
        Assert.Equal("Ledger (irr.)", AutoNestForm.LedgerSource(plain with { IsIrregular = true }, "HR"));
        Assert.Equal("A36 (substitute)", AutoNestForm.LedgerSource(plain with { Rank = 4, Label = "A36" }, ""));
        Assert.Equal("A1011 P&O for HR (irr.)",
            AutoNestForm.LedgerSource(plain with { Rank = 2, Label = "A1011 P&O", IsIrregular = true }, "HR"));
        Assert.Equal("A36 HR", AutoNestForm.LedgerSource(plain with { Rank = 0, Label = "A36 HR" }, "HR"));
        Assert.StartsWith("Items:", AutoNestForm.LedgerTooltip(plain, "HR"));
    }

    [Fact]
    public void LedgerRowsCarryTheItemCodesAutoNestAssigns() => RunSta(() =>
    {
        using var form = new LedgerTestForm();
        form.LoadPlateOptions(Manual(0, 0), 0.5);
        GetGrid(form);
        Assert.Empty(form.LedgerRows);

        form.ApplyLedgerStock(Stock);

        Assert.Equal(new[] { "ledger:1", "ledger:2" }, form.LedgerRows.Select(r => r.Id));
        Assert.Equal(new[] { "B-1", "B-1", "B-1" }, form.LedgerRows[0].ItemCodes);
        Assert.Equal(new[] { "D-1", "D-2" }, form.LedgerRows[1].ItemCodes);
        Assert.Equal("R1", form.LedgerRows[1].ItemLocations["D-2"]);
    });

    private sealed class LedgerTestForm : AutoNestForm
    {
        public LedgerTestForm() : this(LedgerNest()) { }

        public LedgerTestForm(Nest nest) : base(nest) { }

        public bool Configured { get; set; } = true;
        public LedgerStockResult Reply { get; set; } = Stock;
        public TaskCompletionSource<LedgerStockResult>? Pending { get; set; }
        public List<(string, string, double)> Requests { get; } = new();
        public CancellationToken LastToken { get; private set; }
        public LedgerCodesInUse InUse { get; set; } = LedgerCodesInUse.Known([]);
        public Guid? ExcludedNestId { get; private set; }

        internal override Task<LedgerCodesInUse> FetchCodesInUseAsync(Guid excludeNestId, CancellationToken cancellationToken)
        {
            ExcludedNestId = excludeNestId;
            return Task.FromResult(InUse);
        }

        internal override bool LedgerConfigured => Configured;

        internal override Task<LedgerStockResult> FetchLedgerStockAsync(string material, string grade,
            double thicknessIn, CancellationToken cancellationToken)
        {
            Requests.Add((material, grade, thicknessIn));
            LastToken = cancellationToken;
            return Pending?.Task ?? Task.FromResult(Reply);
        }
    }

    private static OpenNest.Data.NestStockCodeUse Use(string code, string nest) =>
        new() { ItemCode = code, NestId = Guid.NewGuid(), NestName = nest, Status = NestStatus.Quote };

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

    private static LedgerStockSpec Spec(long id, int rank, string grade, string finish) =>
        new(id, $"{grade} {finish}", 0.0598m, "16 ga") { Rank = rank, MaterialFamily = "Steel", Grade = grade, Finish = finish };

    private static void SetValue(object row, string property, object value) =>
        TypeDescriptor.GetProperties(row)[property]!.SetValue(row, value);

    private static object GetValue(object row, string property) =>
        TypeDescriptor.GetProperties(row)[property]!.GetValue(row)!;

    /// <summary>The fore colour the grid's CellFormatting handlers give a cell.</summary>
    private static System.Drawing.Color FormattedForeColor(DataGridView grid, int column, int row)
    {
        var args = new DataGridViewCellFormattingEventArgs(column, row, grid[column, row].Value, typeof(string),
            new DataGridViewCellStyle());
        typeof(DataGridView).GetMethod("OnCellFormatting",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                new[] { typeof(DataGridViewCellFormattingEventArgs) }, null)!
            .Invoke(grid, new object[] { args });
        return args.CellStyle!.ForeColor;
    }

    private static void RunSta(Action action) =>
        StaTestThread.Run(action, TimeSpan.FromSeconds(15), "The STA test did not complete.");
}
