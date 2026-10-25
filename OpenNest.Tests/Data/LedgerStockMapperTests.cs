using OpenNest.Data.MaterialLedger;
using OpenNest.Geometry;

namespace OpenNest.Tests.Data;

public class LedgerStockMapperTests
{
    [Fact]
    public void BundlesAndSheetsAreOneRowEachAndDropsOfOneSizeShareARow()
    {
        var mapping = LedgerStockMapper.Map(new[]
        {
            Item("B-1", LedgerItemKinds.FullSheetBundle, 48, 96, quantity: 3),
            Item("S-1", LedgerItemKinds.FullSheet, 48, 96, quantity: 1),
            Item("D-2", LedgerItemKinds.Drop, 24, 36, location: "R2"),
            Item("D-9", LedgerItemKinds.Drop, 30, 40, location: "R1"),
            Item("D-1", LedgerItemKinds.Drop, 36.0004m, 24, location: "R1"),
            Item("D-3", LedgerItemKinds.Drop, 24, 36, location: "R1"),
        }, Units.Inches);

        Assert.Equal(4, mapping.Rows.Count);
        var bundle = mapping.Rows[0];
        Assert.Equal("ledger:1", bundle.Id);
        Assert.False(bundle.IsDrop);
        Assert.Equal(3, bundle.Quantity);
        Assert.Equal(new[] { "B-1", "B-1", "B-1" }, bundle.ItemCodes);
        Assert.Equal(new Size(48, 96), bundle.Size);
        Assert.Equal(1, mapping.Rows[1].Quantity);

        // Width is the short side; drops matching after ML's 3-place rounding group, in ledger order.
        var drops = mapping.Rows[2];
        Assert.True(drops.IsDrop);
        Assert.Equal(new Size(24, 36), drops.Size);
        Assert.Equal(3, drops.Quantity);
        Assert.Equal(new[] { "D-2", "D-1", "D-3" }, drops.ItemCodes);
        Assert.Equal(new[] { "R2", "R1" }, drops.Locations);
        Assert.Equal(new[] { "D-9" }, mapping.Rows[3].ItemCodes);

        Assert.Equal(2, mapping.SheetRows);
        Assert.Equal(4, mapping.Drops);
        Assert.Equal("Ledger: 2 sheet sizes, 4 drops", mapping.Summary);
    }

    [Fact]
    public void DropsOfOneSizeFromDifferentSpecsKeepTheirOwnRows()
    {
        var mapping = LedgerStockMapper.Map(new[]
        {
            Item("D-1", LedgerItemKinds.Drop, 24, 36, spec: 11, rank: 1),
            Item("D-2", LedgerItemKinds.Drop, 24, 36, spec: 12, rank: 2),
            Item("D-3", LedgerItemKinds.Drop, 24, 36, spec: 11, rank: 1, location: "R2"),
        }, Units.Inches, specs: new[] { Spec(11, 1, "A1011", "HR"), Spec(12, 2, "A1011", "P&O") });

        Assert.Equal(2, mapping.Rows.Count);
        Assert.Equal(new[] { "D-1", "D-3" }, mapping.Rows[0].ItemCodes);
        Assert.Equal("A1011 HR", mapping.Rows[0].Label);
        Assert.False(mapping.Rows[0].IsSubstitute);
        Assert.Equal(new[] { "D-2" }, mapping.Rows[1].ItemCodes);
        Assert.Equal("A1011 P&O", mapping.Rows[1].Label);
        Assert.Equal(2, mapping.Rows[1].Rank);
        Assert.True(mapping.Rows[1].IsSubstitute);
        Assert.Equal("Ledger: 0 sheet sizes, 3 drops, 1 substitute row", mapping.Summary);
    }

    [Fact]
    public void RowsRunInRankOrderAndKeepLedgerOrderWithinARank()
    {
        // Out of rank order on purpose; within a rank the input order is the ledger's own.
        var mapping = LedgerStockMapper.Map(new[]
        {
            Item("P-2", LedgerItemKinds.FullSheet, 48, 96, spec: 12, rank: 2, location: "R1"),
            Item("H-9", LedgerItemKinds.Drop, 24, 36, spec: 11, rank: 1, location: "A1"),
            Item("H-1", LedgerItemKinds.Drop, 24, 36, spec: 11, rank: 1, location: "B2"),
            Item("X-1", LedgerItemKinds.FullSheet, 60, 120, spec: 10, rank: 0, location: "Z9"),
            Item("H-5", LedgerItemKinds.FullSheet, 48, 96, spec: 11, rank: 1, location: "C3"),
            Item("L-1", LedgerItemKinds.FullSheet, 48, 96, spec: 13, rank: 3, location: "A1"),
        }, Units.Inches, specs: new[]
        {
            Spec(10, 0, "A36", "HR"), Spec(11, 1, "A1011", "HR"), Spec(12, 2, "A1011", "P&O"), Spec(13, 3, "A1011", "Laser"),
        });

        Assert.Equal(new[] { 0, 1, 1, 2, 3 }, mapping.Rows.Select(r => r.Rank));
        Assert.Equal(new[] { "X-1", "H-9", "H-5", "P-2", "L-1" }, mapping.Rows.Select(r => r.ItemCodes[0]));
        Assert.Equal(new[] { "H-9", "H-1" }, mapping.Rows[1].ItemCodes);
        Assert.Equal(new[] { "A1", "B2" }, mapping.Rows[1].Locations);
        Assert.Equal(new[] { "ledger:1", "ledger:2", "ledger:3", "ledger:4", "ledger:5" }, mapping.Rows.Select(r => r.Id));
        Assert.Equal(new[] { false, false, false, true, true }, mapping.Rows.Select(r => r.IsSubstitute));
    }

    // Steel/HR lists A1011 grades first; at 3/8 only its A36 (rank 3) and A572 (rank 4) entries
    // match, so A36 is the preferred plate and only A572 is a substitute, as ML labels them.
    [Fact]
    public void SubstitutesAreRankedAgainstTheBestRankMatchedAtThisThickness()
    {
        var mapping = LedgerStockMapper.Map(new[]
        {
            Item("A36-1", LedgerItemKinds.FullSheet, 60, 120, spec: 30, rank: 3),
            Item("A36-2", LedgerItemKinds.Drop, 20, 70, spec: 30, rank: 3),
            Item("GR50-1", LedgerItemKinds.FullSheet, 60, 120, spec: 31, rank: 4),
        }, Units.Inches, specs: new[] { Spec(30, 3, "A36", null), Spec(31, 4, "A572 GR50", null) });

        Assert.Equal(new[] { false, false, true }, mapping.Rows.Select(r => r.IsSubstitute));
        Assert.Equal(1, mapping.SubstituteRows);
        Assert.All(mapping.Rows, r => Assert.Equal(3, r.PreferredRank));
    }

    // The preferred spec may have nothing on the rack: it still sets the bar for substitutes.
    [Fact]
    public void AnOutOfStockPreferredSpecStillMakesLowerRanksSubstitutes()
    {
        var mapping = LedgerStockMapper.Map(new[]
        {
            Item("PO-1", LedgerItemKinds.FullSheet, 48, 96, spec: 41, rank: 2),
        }, Units.Inches, specs: new[] { Spec(40, 1, "A1011", "HR"), Spec(41, 2, "A1011", "HRPO") });

        Assert.True(Assert.Single(mapping.Rows).IsSubstitute);
    }

    [Fact]
    public void AnOlderLedgerWithoutRanksOrGradesIsRankOneAndUnlabeled()
    {
        var mapping = LedgerStockMapper.Map(new[]
        {
            Item("S-1", LedgerItemKinds.FullSheet, 48, 96, spec: 7),
            Item("D-1", LedgerItemKinds.Drop, 24, 36, spec: 7),
        }, Units.Inches, specs: new[] { new LedgerStockSpec(7, "A36 1/4", 0.25m, null) });

        Assert.All(mapping.Rows, row =>
        {
            Assert.Equal(1, row.Rank);
            Assert.Null(row.Label);
            Assert.False(row.IsSubstitute);
        });
        Assert.Equal(new[] { "S-1", "D-1" }, mapping.Rows.Select(r => r.ItemCodes[0]));
    }

    [Fact]
    public void ItemRankFallsBackToItsSpecsRank()
    {
        var row = Assert.Single(LedgerStockMapper.Map(new[] { Item("S-1", LedgerItemKinds.FullSheet, 48, 96, spec: 12) },
            Units.Inches, specs: new[] { Spec(12, 2, "A36", null) }).Rows);
        Assert.Equal(2, row.Rank);
        Assert.Equal("A36", row.Label);
    }

    [Fact]
    public void UnknownShapesAreSkippedAndCounted()
    {
        var mapping = LedgerStockMapper.Map(new[]
        {
            Item("U-1", LedgerItemKinds.Drop, 20, 30, shape: LedgerItemShapes.Unknown),
            Item("U-2", LedgerItemKinds.Drop, 20, 30, shape: "SomeFutureShape"),
            Item("S-1", LedgerItemKinds.FullSheet, 60, 120),
        }, Units.Inches);

        Assert.Equal("S-1", Assert.Single(Assert.Single(mapping.Rows).ItemCodes));
        Assert.Equal(2, mapping.SkippedUnknownShape);
        Assert.Equal("Ledger: 1 sheet size, 0 drops (2 skipped: 2 unknown shape)", mapping.Summary);
    }

    [Fact]
    public void IrregularDropsAreMarkedAndKeptApartFromRectangularOnes()
    {
        var mapping = LedgerStockMapper.Map(new[]
        {
            Item("D-1", LedgerItemKinds.Drop, 20, 30),
            Item("D-2", LedgerItemKinds.Drop, 20, 30, shape: LedgerItemShapes.IrregularConservativeRectangle),
        }, Units.Inches);

        Assert.Equal(2, mapping.Rows.Count);
        Assert.False(mapping.Rows[0].IsIrregular);
        Assert.True(mapping.Rows[1].IsIrregular);
    }

    [Fact]
    public void ItemsNoPartFitsAreSkipped()
    {
        var mapping = LedgerStockMapper.Map(new[]
        {
            Item("D-1", LedgerItemKinds.Drop, 10, 50),
            Item("D-2", LedgerItemKinds.Drop, 20, 25),
            Item("D-3", LedgerItemKinds.Drop, 5, 5),
        }, Units.Inches, new[] { new Size(22, 12), new Size(40, 30) });

        Assert.Equal("D-2", Assert.Single(Assert.Single(mapping.Rows).ItemCodes));
        Assert.Equal(2, mapping.SkippedTooSmall);
        Assert.Contains("2 too small for any part", mapping.Summary);
    }

    [Fact]
    public void HeldOutItemsAndUnknownKindsAreIgnored()
    {
        var mapping = LedgerStockMapper.Map(new[]
        {
            Item("S-1", LedgerItemKinds.FullSheet, 48, 96, quantity: 0),
            Item("C-1", "Coil", 48, 96),
        }, Units.Inches);

        Assert.Empty(mapping.Rows);
    }

    [Fact]
    public void MillimeterNestsGetMillimeterSizes()
    {
        var row = Assert.Single(LedgerStockMapper.Map(new[] { Item("S-1", LedgerItemKinds.FullSheet, 48, 96.5m) },
            Units.Millimeters).Rows);
        Assert.Equal(48 * 25.4, row.Size.Width, 9);
        Assert.Equal(96.5 * 25.4, row.Size.Length, 9);
    }

    [Theory]
    [InlineData(0.25, Units.Inches, 0.25)]
    [InlineData(0.18751, Units.Inches, 0.1875)]
    [InlineData(6.35, Units.Millimeters, 0.25)]
    [InlineData(3, Units.Millimeters, 0.1181)]
    public void ThicknessQueryIsInchesToFourPlaces(double thickness, Units units, double expected) =>
        Assert.Equal(expected, LedgerStockMapper.ThicknessInches(thickness, units));

    [Fact]
    public void LedgerIdsAreRecognized()
    {
        Assert.True(LedgerStockMapper.IsLedgerId("ledger:3"));
        Assert.False(LedgerStockMapper.IsLedgerId("option-0"));
        Assert.False(LedgerStockMapper.IsLedgerId(null));
    }

    private static LedgerStockItem Item(string code, string kind, decimal width, decimal length, int quantity = 1,
        string shape = LedgerItemShapes.Rectangular, string location = "R1", long spec = 7, int? rank = null) =>
        new(code, spec, kind, width, length, quantity, shape, location, location, null, null, DateTime.UtcNow)
        {
            Rank = rank,
        };

    private static LedgerStockSpec Spec(long id, int rank, string grade, string? finish) =>
        new(id, $"{grade} {finish}", 0.0598m, "16 ga") { Rank = rank, MaterialFamily = "Steel", Grade = grade, Finish = finish };
}
