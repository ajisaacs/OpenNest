using System;
using System.Collections.Generic;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using static OpenNest.IO.CuttingParametersDto;
using static OpenNest.IO.NestFormat;

namespace OpenNest.IO;

/// <summary>
/// Maps drawing cut pins to and from the nest file. Reading is strict per pin: unlike desktop
/// settings, an unknown lead or tab type is not replaced by a default, because a pin that cut
/// differently from what was saved would be worse than no pin.
/// </summary>
internal static class CutPinFormat
{
    private static readonly HashSet<string> LeadInTypes = new(StringComparer.Ordinal)
    { "None", "Line", "Arc", "LineArc", "CleanHole", "LineLine" };

    private static readonly HashSet<string> LeadOutTypes = new(StringComparer.Ordinal) { "None", "Line", "Arc" };

    private static readonly HashSet<string> TabTypes = new(StringComparer.Ordinal) { "Normal", "Machine", "Breaker" };

    internal static CutPinDto ToDto(ContourCutPin pin) => new()
    {
        X = pin.Point.X,
        Y = pin.Point.Y,
        LeadIn = CuttingParametersSerializer.ToLeadInDto(pin.LeadIn),
        LeadOut = CuttingParametersSerializer.ToLeadOutDto(pin.LeadOut),
        Tab = pin.HasTab ? CuttingParametersSerializer.ToTabDto(pin.Tab) : null,
    };

    /// <summary>Builds the pin, or returns null with the reason it cannot be trusted.</summary>
    internal static ContourCutPin Read(CutPinDto dto, out string problem)
    {
        problem = null;
        if (dto == null || dto.Malformed)
            return Fail("it is not a readable pin", out problem);
        if (!IsLeadIn(dto.LeadIn))
            return Fail("its lead-in is missing or of an unknown type", out problem);
        if (!IsLeadOut(dto.LeadOut))
            return Fail("its lead-out is missing or of an unknown type", out problem);
        if (dto.Tab != null && (!TabTypes.Contains(dto.Tab.Type ?? "")
            || dto.Tab.TabLeadIn != null && !IsLeadIn(dto.Tab.TabLeadIn)
            || dto.Tab.TabLeadOut != null && !IsLeadOut(dto.Tab.TabLeadOut)))
            return Fail("its tab is of an unknown type", out problem);

        try
        {
            return new ContourCutPin(
                new Vector(dto.X, dto.Y),
                CuttingParametersSerializer.FromLeadInDto(dto.LeadIn),
                CuttingParametersSerializer.FromLeadOutDto(dto.LeadOut),
                dto.Tab == null ? null : CuttingParametersSerializer.FromTabDto(dto.Tab));
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
        {
            return Fail(ex.Message, out problem);
        }
    }

    private static bool IsLeadIn(LeadInDto dto) => dto != null && LeadInTypes.Contains(dto.Type ?? "");

    private static bool IsLeadOut(LeadOutDto dto) => dto != null && LeadOutTypes.Contains(dto.Type ?? "");

    private static ContourCutPin Fail(string reason, out string problem)
    {
        problem = reason;
        return null;
    }
}
