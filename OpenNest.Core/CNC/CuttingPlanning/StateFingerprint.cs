using System;
using System.Globalization;
using System.Text;
using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Exact text record of cutting settings for detecting in-place edits after capture. It is a
/// freshness fingerprint, not a serializer.
/// Only the exact built-in settings types are supported — the same set
/// <see cref="OwnedCuttingParameters"/> copies: <see cref="CuttingParameters"/>,
/// <see cref="SequenceParameters"/>, <see cref="AssignmentParameters"/> and the built-in
/// lead-in, lead-out and tab types. Every member is written explicitly by type, so nothing is
/// discovered reflectively and no code of another type ever runs: each object's runtime type
/// is checked before any of its members is read, and any other runtime type (a subclass
/// included) throws <see cref="NotSupportedException"/>. Doubles are written by bit pattern
/// and every number is formatted invariantly, so the text is exact and culture-free.
/// </summary>
internal static class StateFingerprint
{
    /// <summary>Fingerprint of <paramref name="settings"/>; null renders as a marker.</summary>
    /// <exception cref="NotSupportedException">A settings object is not an exact built-in type.</exception>
    internal static string Of(CuttingParameters settings)
    {
        var text = new StringBuilder();
        Settings(settings);
        return text.ToString();

        void Settings(CuttingParameters p)
        {
            if (!Begin(p, typeof(CuttingParameters)))
                return;
            Int(p.Id);
            Text(p.MachineName);
            Text(p.MaterialName);
            Text(p.Grade);
            Number(p.Thickness);
            Number(p.Kerf);
            Number(p.PartSpacing);
            Lead(p.ExternalLeadIn);
            Out(p.ExternalLeadOut);
            Lead(p.InternalLeadIn);
            Out(p.InternalLeadOut);
            Lead(p.ArcCircleLeadIn);
            Out(p.ArcCircleLeadOut);
            Number(p.PierceClearance);
            Flag(p.RoundLeadInAngles);
            Number(p.LeadInAngleIncrement);
            Number(p.AutoTabMinSize);
            Number(p.AutoTabMaxSize);
            TabSettings(p.TabConfig);
            Flag(p.TabsEnabled);
            Sequence(p.Sequencing);
            Assignment(p.Assignment);
            text.Append('}');
        }

        void Sequence(SequenceParameters s)
        {
            if (!Begin(s, typeof(SequenceParameters)))
                return;
            Int((int)s.Method);
            Number(s.SmallCutoutWidth);
            Number(s.SmallCutoutHeight);
            Number(s.MediumCutoutWidth);
            Number(s.MediumCutoutHeight);
            Number(s.DistanceMediumSmall);
            Flag(s.AlternateRowsColumns);
            Flag(s.AlternateCutoutsWithinRowColumn);
            Number(s.MinDistanceBetweenRowsColumns);
            text.Append('}');
        }

        void Assignment(AssignmentParameters a)
        {
            if (!Begin(a, typeof(AssignmentParameters)))
                return;
            Int((int)a.Method);
            Text(a.Preference);
            Number(a.MinGeometryLength);
            text.Append('}');
        }

        void Lead(LeadIn lead)
        {
            if (lead == null)
            {
                text.Append("null;");
                return;
            }
            switch (lead)
            {
                case NoLeadIn when Begin(lead, typeof(NoLeadIn)):
                    break;
                case LineLeadIn l when Begin(lead, typeof(LineLeadIn)):
                    Number(l.Length);
                    Number(l.ApproachAngle);
                    break;
                case ArcLeadIn l when Begin(lead, typeof(ArcLeadIn)):
                    Number(l.Radius);
                    break;
                case LineArcLeadIn l when Begin(lead, typeof(LineArcLeadIn)):
                    Number(l.LineLength);
                    Number(l.ApproachAngle);
                    Number(l.ArcRadius);
                    break;
                case LineLineLeadIn l when Begin(lead, typeof(LineLineLeadIn)):
                    Number(l.Length1);
                    Number(l.ApproachAngle1);
                    Number(l.Length2);
                    Number(l.ApproachAngle2);
                    break;
                case CleanHoleLeadIn l when Begin(lead, typeof(CleanHoleLeadIn)):
                    Number(l.LineLength);
                    Number(l.ArcRadius);
                    Number(l.Kerf);
                    break;
                default:
                    throw Unsupported(lead);
            }
            text.Append('}');
        }

        void Out(LeadOut lead)
        {
            if (lead == null)
            {
                text.Append("null;");
                return;
            }
            switch (lead)
            {
                case NoLeadOut when Begin(lead, typeof(NoLeadOut)):
                    break;
                case LineLeadOut l when Begin(lead, typeof(LineLeadOut)):
                    Number(l.Length);
                    Number(l.ApproachAngle);
                    break;
                case ArcLeadOut l when Begin(lead, typeof(ArcLeadOut)):
                    Number(l.Radius);
                    break;
                default:
                    throw Unsupported(lead);
            }
            text.Append('}');
        }

        void TabSettings(Tab tab)
        {
            if (tab == null)
            {
                text.Append("null;");
                return;
            }
            switch (tab)
            {
                case NormalTab t when Begin(tab, typeof(NormalTab)):
                    Number(t.CutoutMinWidth);
                    Number(t.CutoutMinHeight);
                    Number(t.CutoutMaxWidth);
                    Number(t.CutoutMaxHeight);
                    break;
                case BreakerTab t when Begin(tab, typeof(BreakerTab)):
                    Number(t.BreakerDepth);
                    Number(t.BreakerLeadInLength);
                    Number(t.BreakerAngle);
                    break;
                case MachineTab t when Begin(tab, typeof(MachineTab)):
                    Int(t.MachineTabId);
                    break;
                default:
                    throw Unsupported(tab);
            }
            Number(tab.Size);
            Lead(tab.TabLeadIn);
            Out(tab.TabLeadOut);
            text.Append('}');
        }

        // Writes the exact type tag and returns true; null writes a marker and returns false.
        // A runtime type other than the expected exact type throws before any member is read.
        bool Begin(object value, Type exact)
        {
            if (value == null)
            {
                text.Append("null;");
                return false;
            }
            if (value.GetType() != exact)
                throw Unsupported(value);
            text.Append(exact.Name).Append('{');
            return true;
        }

        void Number(double value) =>
            text.Append(BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture)).Append(';');

        void Int(int value) => text.Append(value.ToString(CultureInfo.InvariantCulture)).Append(';');

        void Flag(bool value) => text.Append(value ? "1;" : "0;");

        // Length-prefixed so a delimiter inside the text cannot shift fields; null differs from "".
        void Text(string value)
        {
            if (value == null)
                text.Append("~;");
            else
                text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
        }
    }

    private static NotSupportedException Unsupported(object value) =>
        new($"Cutting settings of type {value.GetType().FullName} cannot be captured exactly; " +
            "only the built-in settings types are supported.");
}
