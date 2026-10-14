using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Tests.Data;

/// <summary>
/// Decision logic of the desktop Clamps dialog: edits one nest's rail in its units, keeps
/// "apply to this nest" (OK) separate from "save as machine defaults" (immediate write).
/// </summary>
public class ClampRailEditorTests
{
    private static readonly double[] MachineCenters = { 6, 21, 36, 51, 66, 81, 96, 111 };

    [Fact]
    public void NestWithoutARailStartsEmptyAtTheDefaultClampSize()
    {
        var editor = new ClampRailEditor(null, Units.Inches, applyToNewNests: false);

        Assert.Equal(1.5, editor.Width);
        Assert.Equal(0.625, editor.Depth);
        Assert.Empty(editor.Centers);
        Assert.Null(editor.BuildRail());

        var metric = new ClampRailEditor(null, Units.Millimeters, applyToNewNests: false);
        Assert.Equal(1.5 * 25.4, metric.Width, 9);
    }

    [Fact]
    public void NestRailIsEditedInPlace()
    {
        var rail = new ClampRail(1.5, 0.625, MachineCenters);

        var editor = new ClampRailEditor(rail, Units.Inches, applyToNewNests: true);

        Assert.Equal(MachineCenters, editor.Centers);
        Assert.Equal(rail, editor.BuildRail());
        Assert.True(editor.ApplyToNewNests);
    }

    [Fact]
    public void CentersStaySortedAndRefuseInvalidOrDuplicateValues()
    {
        var editor = new ClampRailEditor(null, Units.Inches, applyToNewNests: false);

        Assert.True(editor.TryAddCenter(21, out _));
        Assert.True(editor.TryAddCenter(6, out var none));
        Assert.Null(none);
        Assert.False(editor.TryAddCenter(21, out var duplicate));
        Assert.Contains("already", duplicate);
        Assert.False(editor.TryAddCenter(-1, out var negative));
        Assert.Contains("origin", negative);
        Assert.False(editor.TryAddCenter(double.NaN, out _));
        Assert.Equal(new[] { 6.0, 21 }, editor.Centers);

        editor.RemoveCenterAt(0);
        editor.RemoveCenterAt(5); // out of range: ignored
        Assert.Equal(new[] { 21.0 }, editor.Centers);
    }

    [Fact]
    public void ApplyingToTheNestReResolvesEveryPlate()
    {
        var nest = new Nest();
        nest.PlateDefaults.Size = new Size(48, 120);
        var full = nest.CreatePlate();
        var shorter = nest.CreatePlate();
        shorter.Size = new Size(48, 60);
        var editor = new ClampRailEditor(null, Units.Inches, applyToNewNests: false);
        foreach (var center in MachineCenters)
            Assert.True(editor.TryAddCenter(center, out _));

        editor.ApplyTo(nest);

        Assert.Equal(8, full.ClampZones.Count);
        Assert.Equal(4, shorter.ClampZones.Count);
        Assert.Equal(8, nest.CreatePlate().ClampZones.Count);

        // Removing every clamp and applying removes them from the nest.
        while (editor.Centers.Count > 0)
            editor.RemoveCenterAt(0);
        editor.ApplyTo(nest);
        Assert.Null(nest.PlateDefaults.ClampRail);
        Assert.All(nest.Plates, p => Assert.Empty(p.ClampZones));
    }

    [Fact]
    public void InvalidClampSizeIsRefusedBeforeApplyingOrSaving()
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        var editor = new ClampRailEditor(null, Units.Inches, applyToNewNests: true);
        editor.TryAddCenter(6, out _);
        editor.Width = 0;

        Assert.Throws<ArgumentOutOfRangeException>(() => editor.ApplyTo(nest));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.ToMachineDefaults());
        Assert.Empty(plate.ClampZones);
        Assert.Null(nest.PlateDefaults.ClampRail);
    }

    [Fact]
    public void MachineDefaultsCaptureTheEditedValuesInTheNestsUnits()
    {
        var editor = new ClampRailEditor(null, Units.Millimeters, applyToNewNests: true);
        editor.Width = 40;
        editor.Depth = 16;
        editor.TryAddCenter(150, out _);

        var settings = editor.ToMachineDefaults();

        Assert.True(settings.Enabled);
        Assert.Equal(Units.Millimeters, settings.Units);
        Assert.Equal(40, settings.Width);
        Assert.Equal(16, settings.Depth);
        Assert.Equal(new[] { 150.0 }, settings.Centers);

        // Without any clamp listed, saved defaults never enable clamps for new nests.
        editor.RemoveCenterAt(0);
        Assert.False(editor.ToMachineDefaults().Enabled);
        editor.ApplyToNewNests = false;
        editor.TryAddCenter(150, out _);
        Assert.False(editor.ToMachineDefaults().Enabled);
    }

    [Fact]
    public void LoadingMachineDefaultsOnlyFillsTheEditorInTheNestsUnits()
    {
        var nest = new Nest { Units = Units.Millimeters };
        var plate = nest.CreatePlate();
        var defaults = new ClampSettings
        {
            Enabled = true,
            Units = Units.Inches,
            Width = 1.5,
            Depth = 0.625,
            Centers = new[] { 21.0, 6 },
        };
        var editor = new ClampRailEditor(null, Units.Millimeters, applyToNewNests: false);

        editor.LoadMachineDefaults(defaults);

        Assert.Equal(1.5 * 25.4, editor.Width, 9);
        Assert.Equal(0.625 * 25.4, editor.Depth, 9);
        Assert.Equal(new[] { 6 * 25.4, 21 * 25.4 }, editor.Centers);
        Assert.True(editor.ApplyToNewNests);
        Assert.Empty(plate.ClampZones); // nothing applied until OK
        Assert.Null(nest.PlateDefaults.ClampRail);

        // Disabled defaults still load their values but do not ask to enable new nests.
        defaults.Enabled = false;
        editor.LoadMachineDefaults(defaults);
        Assert.False(editor.ApplyToNewNests);
        Assert.Equal(2, editor.Centers.Count);
    }

    [Fact]
    public void ConvertedSettingsKeepEnabledAndScaleEveryLength()
    {
        var inches = new ClampSettings { Enabled = true, Width = 1.5, Depth = 0.625, Centers = new[] { 6.0 } };

        var metric = inches.ConvertedTo(Units.Millimeters);

        Assert.True(metric.Enabled);
        Assert.Equal(Units.Millimeters, metric.Units);
        Assert.Equal(38.1, metric.Width, 9);
        Assert.Equal(15.875, metric.Depth, 9);
        Assert.Equal(152.4, metric.Centers[0], 9);
        Assert.Equal(1.5, inches.Width); // the source is not modified
        Assert.Equal(1.5, inches.ConvertedTo(Units.Inches).Width);
    }

    // D9: the editor carries removal minutes through the same three boundaries as size.

    [Fact]
    public void RemovalMinutesStartsFromTheRailOrMachineDefault()
    {
        var rail = new ClampRail(1.5, 0.625, MachineCenters, removalMinutes: 7);
        Assert.Equal(7, new ClampRailEditor(rail, Units.Inches, false).RemovalMinutes);
        Assert.Equal(ClampRail.DefaultRemovalMinutes,
            new ClampRailEditor(null, Units.Inches, false).RemovalMinutes);
    }

    [Fact]
    public void RemovalMinutesIsNeverConvertedBetweenUnitsOnLoadOrBuild()
    {
        var editor = new ClampRailEditor(null, Units.Millimeters, applyToNewNests: true);
        editor.RemovalMinutes = 8;
        editor.TryAddCenter(150, out _);

        Assert.Equal(8, editor.ToMachineDefaults().RemovalMinutes);
        Assert.Equal(8, editor.BuildRail()!.RemovalMinutes);

        var defaults = new ClampSettings
        {
            Enabled = true,
            Units = Units.Inches,
            RemovalMinutes = 9,
            Centers = new[] { 6.0 },
        };
        editor.LoadMachineDefaults(defaults);
        Assert.Equal(9, editor.RemovalMinutes); // a time, so unaffected by the unit change
    }

    [Fact]
    public void InvalidRemovalMinutesIsRefusedBeforeSaving()
    {
        var editor = new ClampRailEditor(null, Units.Inches, applyToNewNests: true);
        editor.TryAddCenter(6, out _);
        editor.RemovalMinutes = -1;

        Assert.Throws<ArgumentOutOfRangeException>(() => editor.ToMachineDefaults());
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.BuildRail());
    }
}
