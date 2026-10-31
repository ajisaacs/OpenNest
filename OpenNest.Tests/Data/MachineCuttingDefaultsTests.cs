using OpenNest.CNC.CuttingStrategy;
using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Tests.Data;

public class MachineCuttingDefaultsTests
{
    private static MachineConfig Machine(LeadConfig leadIn, LeadConfig leadOut = null, UnitSystem units = UnitSystem.Inches) => new()
    {
        Name = "Fiber 1",
        Units = units,
        Materials =
        {
            new MaterialConfig
            {
                Name = "Mild Steel",
                Grade = "A36",
                Thicknesses =
                {
                    new ThicknessConfig
                    {
                        Value = 0.25,
                        Kerf = 0.012,
                        LeadIn = leadIn,
                        LeadOut = leadOut ?? new LeadConfig { Type = "Line", Length = 0.0625, Angle = 80 },
                        CutOff = new CutOffConfig { PartClearance = 0.25, Overtravel = 0.125, MinSegmentLength = 0.5, Direction = "TowardOrigin" },
                    },
                },
            },
            new MaterialConfig
            {
                Name = "Mild Steel",
                Grade = "A572",
                Thicknesses = { new ThicknessConfig { Value = 0.25, Kerf = 0.02, LeadIn = new LeadConfig { Type = "None" } } },
            },
        },
    };

    private static Nest NestOf(double thickness, string grade = "", Units units = Units.Inches) =>
        new() { Units = units, Thickness = thickness, Material = new Material("Mild Steel", grade, 0.2836) };

    private static readonly LeadConfig LineArc = new() { Type = "Line + Arc", Length = 0.125, Angle = 90, Radius = 0.0625 };

    private static MachineConfig SpacedMachine(UnitSystem units = UnitSystem.Inches)
    {
        var machine = Machine(LineArc, units: units);
        var thickness = machine.Materials[0].Thicknesses[0];
        thickness.PartSpacing = 0.5;
        thickness.EdgeSpacing = new EdgeSpacingConfig { Left = 1, Bottom = 2, Right = 3, Top = 4 };
        return machine;
    }

    private static Nest SpacedNest(double thickness = 0.25, Units units = Units.Inches)
    {
        var nest = NestOf(thickness, "A36", units);
        nest.PlateDefaults.PartSpacing = 0.1;
        nest.PlateDefaults.EdgeSpacing = new Spacing(0.2, 0.2, 0.2, 0.2);
        return nest;
    }

    [Fact]
    public void ApplySpacingTo_SetsTheMachinesSpacing()
    {
        var nest = SpacedNest();

        Assert.True(MachineCuttingDefaults.ApplySpacingTo(nest, SpacedMachine()));

        Assert.Equal(0.5, nest.PlateDefaults.PartSpacing);
        Assert.Equal(new Spacing(1, 2, 3, 4), nest.PlateDefaults.EdgeSpacing);
    }

    [Fact]
    public void ApplySpacingTo_ConvertsToTheNestsUnits()
    {
        var nest = SpacedNest(6.35, Units.Millimeters);

        Assert.True(MachineCuttingDefaults.ApplySpacingTo(nest, SpacedMachine()));

        Assert.Equal(12.7, nest.PlateDefaults.PartSpacing, 6);
        Assert.Equal(25.4, nest.PlateDefaults.EdgeSpacing.Left, 6);
    }

    [Fact]
    public void ApplySpacingTo_KeepsWhatTheThicknessLeavesUnset()
    {
        var machine = SpacedMachine();
        machine.Materials[0].Thicknesses[0].EdgeSpacing = null;
        var nest = SpacedNest();

        Assert.True(MachineCuttingDefaults.ApplySpacingTo(nest, machine));

        Assert.Equal(0.5, nest.PlateDefaults.PartSpacing);
        Assert.Equal(new Spacing(0.2, 0.2, 0.2, 0.2), nest.PlateDefaults.EdgeSpacing);
    }

    [Fact]
    public void ApplySpacingTo_WithoutSpacingOnFile_ChangesNothing()
    {
        var nest = SpacedNest();

        Assert.False(MachineCuttingDefaults.ApplySpacingTo(nest, Machine(LineArc)));
        Assert.False(MachineCuttingDefaults.ApplySpacingTo(nest, null));
        Assert.False(MachineCuttingDefaults.ApplySpacingTo(SpacedNest(0.5), SpacedMachine()));

        Assert.Equal(0.1, nest.PlateDefaults.PartSpacing);
    }

    [Fact]
    public void ApplySpacingAfter_FollowsAChangedThicknessOnly()
    {
        var nest = SpacedNest(0.5);

        MachineCuttingDefaults.ApplySpacingAfter(nest, SpacedMachine(), () => nest.Customer = "Acme");
        Assert.Equal(0.1, nest.PlateDefaults.PartSpacing);

        MachineCuttingDefaults.ApplySpacingAfter(nest, SpacedMachine(), () => nest.Thickness = 0.25);
        Assert.Equal(0.5, nest.PlateDefaults.PartSpacing);
    }

    [Fact]
    public void ApplySpacingAfter_KeepsSpacingTheEditChanged()
    {
        var nest = SpacedNest(0.5);

        MachineCuttingDefaults.ApplySpacingAfter(nest, SpacedMachine(), () =>
        {
            nest.Thickness = 0.25;
            nest.PlateDefaults.PartSpacing = 0.3;
        });

        Assert.Equal(0.3, nest.PlateDefaults.PartSpacing);
        Assert.Equal(new Spacing(1, 2, 3, 4), nest.PlateDefaults.EdgeSpacing);
    }

    [Fact]
    public void Spacing_SurvivesTheMachineFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var provider = new LocalJsonProvider(directory);
            var machine = SpacedMachine();
            provider.SaveMachine(machine);

            var thickness = provider.GetMachine(machine.Id)!.Materials[0].Thicknesses[0];

            Assert.Equal(0.5, thickness.PartSpacing);
            Assert.Equal(4, thickness.EdgeSpacing!.Top);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void IsRecordedFor_KeepsLeadsEditedAfterTheMachineSettingsWereTaken()
    {
        var parameters = new CuttingParameters();
        Assert.True(MachineCuttingDefaults.ApplyTo(parameters, Machine(LineArc), NestOf(0.25, "A36")));
        parameters.ExternalLeadOut = new NoLeadOut();

        Assert.True(MachineCuttingDefaults.IsRecordedFor(parameters, Machine(LineArc), NestOf(0.2505, "A36")));
        Assert.False(MachineCuttingDefaults.IsRecordedFor(parameters, Machine(LineArc), NestOf(0.25, "A572")));
        Assert.False(MachineCuttingDefaults.IsRecordedFor(parameters, Machine(LineArc), NestOf(0.5, "A36")));
        Assert.False(MachineCuttingDefaults.IsRecordedFor(new CuttingParameters(), Machine(LineArc), NestOf(0.25, "A36")));
    }

    [Fact]
    public void ApplyTo_GivesEveryContourTheMachineLeadsAndStampsTheSource()
    {
        var parameters = new CuttingParameters { PierceClearance = 0.1 };

        Assert.True(MachineCuttingDefaults.ApplyTo(parameters, Machine(LineArc), NestOf(0.25)));

        foreach (var leadIn in new[] { parameters.ExternalLeadIn, parameters.InternalLeadIn })
        {
            var lineArc = Assert.IsType<LineArcLeadIn>(leadIn);
            Assert.Equal(0.125, lineArc.LineLength);
            Assert.Equal(0.0625, lineArc.ArcRadius);
            Assert.Equal(90, lineArc.ApproachAngle);
        }
        foreach (var leadOut in new[] { parameters.ExternalLeadOut, parameters.InternalLeadOut })
        {
            var line = Assert.IsType<LineLeadOut>(leadOut);
            Assert.Equal(0.0625, line.Length);
            Assert.Equal(80, line.ApproachAngle);
        }
        Assert.NotSame(parameters.ExternalLeadIn, parameters.InternalLeadIn);
        Assert.Equal(0.012, parameters.Kerf);
        Assert.Equal("Fiber 1", parameters.MachineName);
        Assert.Equal("Mild Steel", parameters.MaterialName);
        Assert.Equal(0.25, parameters.Thickness);
        Assert.Equal(0.1, parameters.PierceClearance); // Settings the machine does not hold are kept.
    }

    [Fact]
    public void ApplyTo_UsesTheNestsGradeWhenSet()
    {
        var parameters = new CuttingParameters();

        Assert.True(MachineCuttingDefaults.ApplyTo(parameters, Machine(LineArc), NestOf(0.25, "A572")));

        Assert.IsType<NoLeadIn>(parameters.ExternalLeadIn);
        Assert.Equal(0.02, parameters.Kerf);
    }

    [Fact]
    public void ApplyTo_ConvertsBetweenMachineAndNestUnits()
    {
        var parameters = new CuttingParameters();

        Assert.True(MachineCuttingDefaults.ApplyTo(parameters, Machine(LineArc), NestOf(6.35, units: Units.Millimeters)));

        var lineArc = Assert.IsType<LineArcLeadIn>(parameters.ExternalLeadIn);
        Assert.Equal(3.175, lineArc.LineLength, 6);
        Assert.Equal(1.5875, lineArc.ArcRadius, 6);
        Assert.Equal(0.3048, parameters.Kerf, 6);
    }

    [Theory]
    [InlineData(0.2505)] // Off by three-decimal rounding, as PEP's 0.188 is from 3/16 (0.1875).
    [InlineData(0.2495)]
    public void ApplyTo_MatchesAThicknessRoundedToThreeDecimals(double thickness)
    {
        var parameters = new CuttingParameters();

        Assert.True(MachineCuttingDefaults.ApplyTo(parameters, Machine(LineArc), NestOf(thickness)));

        Assert.Equal(0.012, parameters.Kerf);
    }

    [Theory]
    [InlineData(0.1875, "A36")]
    [InlineData(0.252, "A36")]
    [InlineData(0.25, "304")]
    public void ApplyTo_WithoutMachineSettings_ChangesNothing(double thickness, string grade)
    {
        var parameters = new CuttingParameters();
        var internalLeadIn = parameters.InternalLeadIn;

        Assert.False(MachineCuttingDefaults.ApplyTo(parameters, Machine(LineArc), NestOf(thickness, grade)));
        Assert.False(MachineCuttingDefaults.ApplyTo(parameters, null, NestOf(0.25)));

        Assert.Same(internalLeadIn, parameters.InternalLeadIn);
        Assert.Null(parameters.MachineName);
    }

    [Fact]
    public void CleanHoleLeadIn_IsForHolesOnly()
    {
        var parameters = new CuttingParameters { ExternalLeadIn = new LineLeadIn { Length = 0.3 } };
        var cleanHole = new LeadConfig { Type = "Clean Hole", Length = 0.1, Radius = 0.05 };

        Assert.True(MachineCuttingDefaults.ApplyTo(parameters, Machine(cleanHole), NestOf(0.25)));

        Assert.Equal(0.3, Assert.IsType<LineLeadIn>(parameters.ExternalLeadIn).Length);
        Assert.Equal(0.012, Assert.IsType<CleanHoleLeadIn>(parameters.InternalLeadIn).Kerf);
    }

    [Fact]
    public void InsideLeads_GoToInternalContoursOnly()
    {
        var parameters = new CuttingParameters();
        var machine = Machine(LineArc);
        var thickness = machine.Materials[0].Thicknesses[0];
        thickness.InsideLeadIn = new LeadConfig { Type = "Clean Hole", Length = 0.1, Radius = 0.05 };
        thickness.InsideLeadOut = new LeadConfig { Type = "Arc", Radius = 0.04 };

        Assert.True(MachineCuttingDefaults.ApplyTo(parameters, machine, NestOf(0.25)));

        Assert.IsType<LineArcLeadIn>(parameters.ExternalLeadIn);
        Assert.IsType<LineLeadOut>(parameters.ExternalLeadOut);
        Assert.Equal(0.05, Assert.IsType<CleanHoleLeadIn>(parameters.InternalLeadIn).ArcRadius);
        Assert.Equal(0.04, Assert.IsType<ArcLeadOut>(parameters.InternalLeadOut).Radius);
    }

    [Fact]
    public void LineLineLeadIn_KeepsTheLeadInsButStillSetsLeadOuts()
    {
        var parameters = new CuttingParameters();
        var internalLeadIn = parameters.InternalLeadIn;

        Assert.True(MachineCuttingDefaults.ApplyTo(parameters, Machine(new LeadConfig { Type = "Line + Line", Length = 0.1 }), NestOf(0.25)));

        Assert.Same(internalLeadIn, parameters.InternalLeadIn);
        Assert.IsType<LineLeadOut>(parameters.ExternalLeadOut);
    }

    [Fact]
    public void ApplyTo_CutOffSettings_TakesTheMachinesAndKeepsTheHeadLock()
    {
        var settings = new CutOffSettings { LockHeadHeight = true, HeadLockOvercut = 2 };

        Assert.True(MachineCuttingDefaults.ApplyTo(settings, Machine(LineArc), NestOf(6.35, units: Units.Millimeters)));

        Assert.Equal(6.35, settings.PartClearance, 6);
        Assert.Equal(3.175, settings.Overtravel, 6);
        Assert.Equal(12.7, settings.MinSegmentLength, 6);
        Assert.Equal(CutDirection.TowardOrigin, settings.CutDirection);
        Assert.True(settings.LockHeadHeight);
        Assert.Equal(2, settings.HeadLockOvercut);
    }

    [Fact]
    public void ApplyTo_CutOffSettings_WithoutMachineSettings_ChangesNothing()
    {
        var settings = new CutOffSettings();

        Assert.False(MachineCuttingDefaults.ApplyTo(settings, Machine(LineArc), NestOf(0.5)));

        Assert.Equal(new CutOffSettings().PartClearance, settings.PartClearance);
    }
}
