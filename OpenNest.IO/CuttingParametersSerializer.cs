using System;
using System.Text.Json;
using OpenNest.CNC.CuttingStrategy;
using static OpenNest.IO.CuttingParametersDto;

namespace OpenNest.IO;

public static class CuttingParametersSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Writes desktop settings, retaining the legacy null-tab width fallback.</summary>
    public static string Serialize(CuttingParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var dto = ToDto(parameters);
        if (parameters.TabConfig == null)
            dto.TabConfig = null;
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    /// <summary>Reads desktop settings with their historical defaults and angle fallback.</summary>
    public static CuttingParameters Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<CuttingParametersDto>(json, JsonOptions);
        var parameters = FromDto(dto) ?? new CuttingParameters();
        if (parameters.LeadInAngleIncrement <= 0)
            parameters.LeadInAngleIncrement = 5.0;
        return parameters;
    }

    /// <summary>Captures an owned snapshot. Null stays null for absent plate parameters.</summary>
    public static CuttingParametersDto ToDto(CuttingParameters parameters)
    {
        if (parameters == null)
            return null;

        return new CuttingParametersDto
        {
            Id = parameters.Id,
            MachineName = parameters.MachineName,
            MaterialName = parameters.MaterialName,
            Grade = parameters.Grade,
            Thickness = parameters.Thickness,
            Kerf = parameters.Kerf,
            PartSpacing = parameters.PartSpacing,
            ExternalLeadIn = ToLeadInDto(parameters.ExternalLeadIn),
            ExternalLeadOut = ToLeadOutDto(parameters.ExternalLeadOut),
            InternalLeadIn = ToLeadInDto(parameters.InternalLeadIn),
            InternalLeadOut = ToLeadOutDto(parameters.InternalLeadOut),
            ArcCircleLeadIn = ToLeadInDto(parameters.ArcCircleLeadIn),
            ArcCircleLeadOut = ToLeadOutDto(parameters.ArcCircleLeadOut),
            TabsEnabled = parameters.TabsEnabled,
            TabWidth = parameters.TabConfig?.Size ?? 0.25,
            TabConfig = ToTabDto(parameters.TabConfig),
            PierceClearance = parameters.PierceClearance,
            RoundLeadInAngles = parameters.RoundLeadInAngles,
            LeadInAngleIncrement = parameters.LeadInAngleIncrement,
            AutoTabMinSize = parameters.AutoTabMinSize,
            AutoTabMaxSize = parameters.AutoTabMaxSize,
            Sequencing = ToSequenceDto(parameters.Sequencing),
            Assignment = ToAssignmentDto(parameters.Assignment),
        };
    }

    /// <summary>Restores an owned snapshot without the desktop settings' value normalization.</summary>
    public static CuttingParameters FromDto(CuttingParametersDto dto)
    {
        if (dto == null)
            return null;

        return new CuttingParameters
        {
            Id = dto.Id,
            MachineName = dto.MachineName,
            MaterialName = dto.MaterialName,
            Grade = dto.Grade,
            Thickness = dto.Thickness,
            Kerf = dto.Kerf,
            PartSpacing = dto.PartSpacing,
            ExternalLeadIn = FromLeadInDto(dto.ExternalLeadIn),
            ExternalLeadOut = FromLeadOutDto(dto.ExternalLeadOut),
            InternalLeadIn = FromLeadInDto(dto.InternalLeadIn),
            InternalLeadOut = FromLeadOutDto(dto.InternalLeadOut),
            ArcCircleLeadIn = FromLeadInDto(dto.ArcCircleLeadIn),
            ArcCircleLeadOut = FromLeadOutDto(dto.ArcCircleLeadOut),
            TabsEnabled = dto.TabsEnabled,
            TabConfig = dto.TabConfig == null
                ? new NormalTab { Size = dto.TabWidth }
                : FromTabDto(dto.TabConfig),
            PierceClearance = dto.PierceClearance,
            RoundLeadInAngles = dto.RoundLeadInAngles,
            LeadInAngleIncrement = dto.LeadInAngleIncrement,
            AutoTabMinSize = dto.AutoTabMinSize,
            AutoTabMaxSize = dto.AutoTabMaxSize,
            Sequencing = FromSequenceDto(dto.Sequencing),
            Assignment = FromAssignmentDto(dto.Assignment),
        };
    }

    private static LeadInDto ToLeadInDto(LeadIn leadIn) => leadIn switch
    {
        LineLeadIn line => new LeadInDto
        {
            Type = "Line",
            Length = line.Length,
            ApproachAngle = line.ApproachAngle,
        },
        ArcLeadIn arc => new LeadInDto { Type = "Arc", Radius = arc.Radius },
        LineArcLeadIn lineArc => new LeadInDto
        {
            Type = "LineArc",
            LineLength = lineArc.LineLength,
            ArcRadius = lineArc.ArcRadius,
            ApproachAngle = lineArc.ApproachAngle,
        },
        CleanHoleLeadIn cleanHole => new LeadInDto
        {
            Type = "CleanHole",
            LineLength = cleanHole.LineLength,
            ArcRadius = cleanHole.ArcRadius,
            Kerf = cleanHole.Kerf,
        },
        LineLineLeadIn lineLine => new LeadInDto
        {
            Type = "LineLine",
            Length1 = lineLine.Length1,
            Angle1 = lineLine.ApproachAngle1,
            Length2 = lineLine.Length2,
            Angle2 = lineLine.ApproachAngle2,
        },
        _ => new LeadInDto { Type = "None" },
    };

    private static LeadIn FromLeadInDto(LeadInDto dto) => dto?.Type switch
    {
        "Line" => new LineLeadIn { Length = dto.Length, ApproachAngle = dto.ApproachAngle },
        "Arc" => new ArcLeadIn { Radius = dto.Radius },
        "LineArc" => new LineArcLeadIn
        {
            LineLength = dto.LineLength,
            ArcRadius = dto.ArcRadius,
            ApproachAngle = dto.ApproachAngle,
        },
        "CleanHole" => new CleanHoleLeadIn
        {
            LineLength = dto.LineLength,
            ArcRadius = dto.ArcRadius,
            Kerf = dto.Kerf,
        },
        "LineLine" => new LineLineLeadIn
        {
            Length1 = dto.Length1,
            ApproachAngle1 = dto.Angle1,
            Length2 = dto.Length2,
            ApproachAngle2 = dto.Angle2,
        },
        _ => new NoLeadIn(),
    };

    private static LeadOutDto ToLeadOutDto(LeadOut leadOut) => leadOut switch
    {
        LineLeadOut line => new LeadOutDto
        {
            Type = "Line",
            Length = line.Length,
            ApproachAngle = line.ApproachAngle,
        },
        ArcLeadOut arc => new LeadOutDto { Type = "Arc", Radius = arc.Radius },
        _ => new LeadOutDto { Type = "None" },
    };

    private static LeadOut FromLeadOutDto(LeadOutDto dto) => dto?.Type switch
    {
        "Line" => new LineLeadOut { Length = dto.Length, ApproachAngle = dto.ApproachAngle },
        "Arc" => new ArcLeadOut { Radius = dto.Radius },
        _ => new NoLeadOut(),
    };

    private static TabDto ToTabDto(Tab tab)
    {
        var dto = tab switch
        {
            NormalTab normal => new TabDto
            {
                Type = "Normal",
                CutoutMinWidth = normal.CutoutMinWidth,
                CutoutMinHeight = normal.CutoutMinHeight,
                CutoutMaxWidth = normal.CutoutMaxWidth,
                CutoutMaxHeight = normal.CutoutMaxHeight,
            },
            MachineTab machine => new TabDto { Type = "Machine", MachineTabId = machine.MachineTabId },
            BreakerTab breaker => new TabDto
            {
                Type = "Breaker",
                BreakerDepth = breaker.BreakerDepth,
                BreakerLeadInLength = breaker.BreakerLeadInLength,
                BreakerAngle = breaker.BreakerAngle,
            },
            _ => new TabDto { Type = "None" },
        };
        if (tab != null)
        {
            dto.Size = tab.Size;
            dto.TabLeadIn = tab.TabLeadIn == null ? null : ToLeadInDto(tab.TabLeadIn);
            dto.TabLeadOut = tab.TabLeadOut == null ? null : ToLeadOutDto(tab.TabLeadOut);
        }
        return dto;
    }

    private static Tab FromTabDto(TabDto dto)
    {
        var tab = dto.Type switch
        {
            "Normal" => (Tab)new NormalTab
            {
                CutoutMinWidth = dto.CutoutMinWidth,
                CutoutMinHeight = dto.CutoutMinHeight,
                CutoutMaxWidth = dto.CutoutMaxWidth,
                CutoutMaxHeight = dto.CutoutMaxHeight,
            },
            "Machine" => new MachineTab { MachineTabId = dto.MachineTabId },
            "Breaker" => new BreakerTab
            {
                BreakerDepth = dto.BreakerDepth,
                BreakerLeadInLength = dto.BreakerLeadInLength,
                BreakerAngle = dto.BreakerAngle,
            },
            _ => null,
        };
        if (tab != null)
        {
            tab.Size = dto.Size;
            tab.TabLeadIn = dto.TabLeadIn == null ? null : FromLeadInDto(dto.TabLeadIn);
            tab.TabLeadOut = dto.TabLeadOut == null ? null : FromLeadOutDto(dto.TabLeadOut);
        }
        return tab;
    }

    private static SequenceDto ToSequenceDto(SequenceParameters parameters) => parameters == null
        ? null
        : new SequenceDto
        {
            Method = parameters.Method,
            SmallCutoutWidth = parameters.SmallCutoutWidth,
            SmallCutoutHeight = parameters.SmallCutoutHeight,
            MediumCutoutWidth = parameters.MediumCutoutWidth,
            MediumCutoutHeight = parameters.MediumCutoutHeight,
            DistanceMediumSmall = parameters.DistanceMediumSmall,
            AlternateRowsColumns = parameters.AlternateRowsColumns,
            AlternateCutoutsWithinRowColumn = parameters.AlternateCutoutsWithinRowColumn,
            MinDistanceBetweenRowsColumns = parameters.MinDistanceBetweenRowsColumns,
        };

    private static SequenceParameters FromSequenceDto(SequenceDto dto) => dto == null
        ? null
        : new SequenceParameters
        {
            Method = dto.Method,
            SmallCutoutWidth = dto.SmallCutoutWidth,
            SmallCutoutHeight = dto.SmallCutoutHeight,
            MediumCutoutWidth = dto.MediumCutoutWidth,
            MediumCutoutHeight = dto.MediumCutoutHeight,
            DistanceMediumSmall = dto.DistanceMediumSmall,
            AlternateRowsColumns = dto.AlternateRowsColumns,
            AlternateCutoutsWithinRowColumn = dto.AlternateCutoutsWithinRowColumn,
            MinDistanceBetweenRowsColumns = dto.MinDistanceBetweenRowsColumns,
        };

    private static AssignmentDto ToAssignmentDto(AssignmentParameters parameters) => parameters == null
        ? null
        : new AssignmentDto
        {
            Method = parameters.Method,
            Preference = parameters.Preference,
            MinGeometryLength = parameters.MinGeometryLength,
        };

    private static AssignmentParameters FromAssignmentDto(AssignmentDto dto) => dto == null
        ? null
        : new AssignmentParameters
        {
            Method = dto.Method,
            Preference = dto.Preference,
            MinGeometryLength = dto.MinGeometryLength,
        };
}
