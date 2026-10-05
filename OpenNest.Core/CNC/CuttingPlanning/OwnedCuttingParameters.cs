using System;
using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Copies only supported, exact setting types; never slices custom subclasses.</summary>
internal static class OwnedCuttingParameters
{
    internal static CuttingParameters Copy(CuttingParameters source)
    {
        Exact<CuttingParameters>(source);
        Exact<SequenceParameters>(source.Sequencing);
        Exact<AssignmentParameters>(source.Assignment);
        var sequence = source.Sequencing;
        var assignment = source.Assignment;
        if (!Enum.IsDefined(sequence.Method) || !Enum.IsDefined(assignment.Method))
            throw new ArgumentException("Unsupported sequence method.");
        if (assignment.Preference == null)
            throw new ArgumentException("Missing assignment preference.");
        if (!double.IsFinite(source.LeadInAngleIncrement) || source.LeadInAngleIncrement <= 0
            || source.LeadInAngleIncrement > 360)
            throw new ArgumentException("Angle increment must be in (0, 360] degrees.");
        return new CuttingParameters
        {
            Id = source.Id,
            MachineName = source.MachineName,
            MaterialName = source.MaterialName,
            Grade = source.Grade,
            Thickness = Dimension(source.Thickness),
            Kerf = Dimension(source.Kerf),
            PartSpacing = Dimension(source.PartSpacing),
            PierceClearance = Dimension(source.PierceClearance),
            ExternalLeadIn = CopyLeadIn(source.ExternalLeadIn),
            InternalLeadIn = CopyLeadIn(source.InternalLeadIn),
            ArcCircleLeadIn = CopyLeadIn(source.ArcCircleLeadIn),
            ExternalLeadOut = CopyLeadOut(source.ExternalLeadOut),
            InternalLeadOut = CopyLeadOut(source.InternalLeadOut),
            ArcCircleLeadOut = CopyLeadOut(source.ArcCircleLeadOut),
            RoundLeadInAngles = source.RoundLeadInAngles,
            LeadInAngleIncrement = source.LeadInAngleIncrement,
            AutoTabMinSize = Dimension(source.AutoTabMinSize),
            AutoTabMaxSize = Dimension(source.AutoTabMaxSize),
            TabConfig = source.TabConfig == null
                ? source.TabsEnabled ? throw new ArgumentException("Enabled tabs require a configuration.") : null
                : CopyTab(source.TabConfig),
            TabsEnabled = source.TabsEnabled,
            Sequencing = new SequenceParameters
            {
                Method = sequence.Method,
                SmallCutoutWidth = Dimension(sequence.SmallCutoutWidth),
                SmallCutoutHeight = Dimension(sequence.SmallCutoutHeight),
                MediumCutoutWidth = Dimension(sequence.MediumCutoutWidth),
                MediumCutoutHeight = Dimension(sequence.MediumCutoutHeight),
                DistanceMediumSmall = Dimension(sequence.DistanceMediumSmall),
                AlternateRowsColumns = sequence.AlternateRowsColumns,
                AlternateCutoutsWithinRowColumn = sequence.AlternateCutoutsWithinRowColumn,
                MinDistanceBetweenRowsColumns = Dimension(sequence.MinDistanceBetweenRowsColumns)
            },
            Assignment = new AssignmentParameters
            {
                Method = assignment.Method,
                Preference = assignment.Preference,
                MinGeometryLength = Dimension(assignment.MinGeometryLength)
            }
        };
    }

    private static LeadIn CopyLeadIn(LeadIn source) => source switch
    {
        NoLeadIn n when n.GetType() == typeof(NoLeadIn) => new NoLeadIn(),
        LineLeadIn n when n.GetType() == typeof(LineLeadIn) => new LineLeadIn
        { Length = Dimension(n.Length), ApproachAngle = Angle(n.ApproachAngle) },
        ArcLeadIn n when n.GetType() == typeof(ArcLeadIn) => new ArcLeadIn { Radius = Dimension(n.Radius) },
        LineArcLeadIn n when n.GetType() == typeof(LineArcLeadIn) => new LineArcLeadIn
        { LineLength = Dimension(n.LineLength), ArcRadius = Dimension(n.ArcRadius), ApproachAngle = Angle(n.ApproachAngle) },
        LineLineLeadIn n when n.GetType() == typeof(LineLineLeadIn) => new LineLineLeadIn
        {
            Length1 = Dimension(n.Length1),
            Length2 = Dimension(n.Length2),
            ApproachAngle1 = Angle(n.ApproachAngle1),
            ApproachAngle2 = Angle(n.ApproachAngle2)
        },
        CleanHoleLeadIn n when n.GetType() == typeof(CleanHoleLeadIn) => new CleanHoleLeadIn
        { LineLength = Dimension(n.LineLength), ArcRadius = Dimension(n.ArcRadius), Kerf = Dimension(n.Kerf) },
        null => throw new ArgumentException("Missing lead-in settings."),
        _ => throw new NotSupportedException("Unsupported lead-in runtime type.")
    };

    private static LeadOut CopyLeadOut(LeadOut source) => source switch
    {
        NoLeadOut n when n.GetType() == typeof(NoLeadOut) => new NoLeadOut(),
        LineLeadOut n when n.GetType() == typeof(LineLeadOut) => new LineLeadOut
        { Length = Dimension(n.Length), ApproachAngle = Angle(n.ApproachAngle) },
        ArcLeadOut n when n.GetType() == typeof(ArcLeadOut) => new ArcLeadOut { Radius = Dimension(n.Radius) },
        null => throw new ArgumentException("Missing lead-out settings."),
        _ => throw new NotSupportedException("Unsupported lead-out runtime type.")
    };

    private static Tab CopyTab(Tab source)
    {
        Tab copy = source switch
        {
            NormalTab n when n.GetType() == typeof(NormalTab) => new NormalTab
            {
                CutoutMinWidth = Dimension(n.CutoutMinWidth),
                CutoutMinHeight = Dimension(n.CutoutMinHeight),
                CutoutMaxWidth = Dimension(n.CutoutMaxWidth),
                CutoutMaxHeight = Dimension(n.CutoutMaxHeight)
            },
            BreakerTab n when n.GetType() == typeof(BreakerTab) => new BreakerTab
            {
                BreakerDepth = Dimension(n.BreakerDepth),
                BreakerLeadInLength = Dimension(n.BreakerLeadInLength),
                BreakerAngle = Angle(n.BreakerAngle)
            },
            MachineTab n when n.GetType() == typeof(MachineTab) => new MachineTab { MachineTabId = n.MachineTabId },
            _ => throw new NotSupportedException("Unsupported tab runtime type.")
        };
        copy.Size = Dimension(source.Size);
        copy.TabLeadIn = source.TabLeadIn == null ? null : CopyLeadIn(source.TabLeadIn);
        copy.TabLeadOut = source.TabLeadOut == null ? null : CopyLeadOut(source.TabLeadOut);
        return copy;
    }

    private static void Exact<T>(T value) where T : class
    {
        if (value == null)
            throw new ArgumentException("Missing cutting settings.");
        if (value.GetType() != typeof(T))
            throw new NotSupportedException("Unsupported settings runtime type.");
    }

    private static double Dimension(double value) => double.IsFinite(value) && value >= 0
        ? value : throw new ArgumentException("Dimensions must be finite and nonnegative.");

    // Approach angles are degrees, not lengths. Signed and periodic angles are valid.
    private static double Angle(double value) => double.IsFinite(value)
        ? value : throw new ArgumentException("Angles must be finite.");
}
