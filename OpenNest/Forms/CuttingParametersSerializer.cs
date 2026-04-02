using OpenNest.CNC.CuttingStrategy;
using System.Text.Json;

namespace OpenNest.Forms
{
    public static class CuttingParametersSerializer
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static string Serialize(CuttingParameters p)
        {
            var dto = new CuttingParametersDto
            {
                ExternalLeadIn = ToDto(p.ExternalLeadIn),
                ExternalLeadOut = ToLeadOutDto(p.ExternalLeadOut),
                InternalLeadIn = ToDto(p.InternalLeadIn),
                InternalLeadOut = ToLeadOutDto(p.InternalLeadOut),
                ArcCircleLeadIn = ToDto(p.ArcCircleLeadIn),
                ArcCircleLeadOut = ToLeadOutDto(p.ArcCircleLeadOut),
                TabsEnabled = p.TabsEnabled,
                TabWidth = p.TabConfig?.Size ?? 0.25,
                PierceClearance = p.PierceClearance,
                AutoTabMinSize = p.AutoTabMinSize,
                AutoTabMaxSize = p.AutoTabMaxSize
            };
            return JsonSerializer.Serialize(dto, JsonOptions);
        }

        public static CuttingParameters Deserialize(string json)
        {
            var dto = JsonSerializer.Deserialize<CuttingParametersDto>(json, JsonOptions);
            if (dto == null)
                return new CuttingParameters();

            return new CuttingParameters
            {
                ExternalLeadIn = FromDto(dto.ExternalLeadIn),
                ExternalLeadOut = FromLeadOutDto(dto.ExternalLeadOut),
                InternalLeadIn = FromDto(dto.InternalLeadIn),
                InternalLeadOut = FromLeadOutDto(dto.InternalLeadOut),
                ArcCircleLeadIn = FromDto(dto.ArcCircleLeadIn),
                ArcCircleLeadOut = FromLeadOutDto(dto.ArcCircleLeadOut),
                TabsEnabled = dto.TabsEnabled,
                TabConfig = new NormalTab { Size = dto.TabWidth },
                PierceClearance = dto.PierceClearance,
                AutoTabMinSize = dto.AutoTabMinSize,
                AutoTabMaxSize = dto.AutoTabMaxSize
            };
        }

        private static LeadInDto ToDto(LeadIn leadIn)
        {
            return leadIn switch
            {
                LineLeadIn line => new LeadInDto { Type = "Line", Length = line.Length, ApproachAngle = line.ApproachAngle },
                ArcLeadIn arc => new LeadInDto { Type = "Arc", Radius = arc.Radius },
                LineArcLeadIn la => new LeadInDto { Type = "LineArc", LineLength = la.LineLength, ArcRadius = la.ArcRadius, ApproachAngle = la.ApproachAngle },
                CleanHoleLeadIn ch => new LeadInDto { Type = "CleanHole", LineLength = ch.LineLength, ArcRadius = ch.ArcRadius, Kerf = ch.Kerf },
                LineLineLeadIn ll => new LeadInDto { Type = "LineLine", Length1 = ll.Length1, Angle1 = ll.ApproachAngle1, Length2 = ll.Length2, Angle2 = ll.ApproachAngle2 },
                _ => new LeadInDto { Type = "None" }
            };
        }

        private static LeadIn FromDto(LeadInDto dto)
        {
            if (dto == null) return new NoLeadIn();
            return dto.Type switch
            {
                "Line" => new LineLeadIn { Length = dto.Length, ApproachAngle = dto.ApproachAngle },
                "Arc" => new ArcLeadIn { Radius = dto.Radius },
                "LineArc" => new LineArcLeadIn { LineLength = dto.LineLength, ArcRadius = dto.ArcRadius, ApproachAngle = dto.ApproachAngle },
                "CleanHole" => new CleanHoleLeadIn { LineLength = dto.LineLength, ArcRadius = dto.ArcRadius, Kerf = dto.Kerf },
                "LineLine" => new LineLineLeadIn { Length1 = dto.Length1, ApproachAngle1 = dto.Angle1, Length2 = dto.Length2, ApproachAngle2 = dto.Angle2 },
                _ => new NoLeadIn()
            };
        }

        private static LeadOutDto ToLeadOutDto(LeadOut leadOut)
        {
            return leadOut switch
            {
                LineLeadOut line => new LeadOutDto { Type = "Line", Length = line.Length, ApproachAngle = line.ApproachAngle },
                ArcLeadOut arc => new LeadOutDto { Type = "Arc", Radius = arc.Radius },
                MicrotabLeadOut mt => new LeadOutDto { Type = "Microtab", GapSize = mt.GapSize },
                _ => new LeadOutDto { Type = "None" }
            };
        }

        private static LeadOut FromLeadOutDto(LeadOutDto dto)
        {
            if (dto == null) return new NoLeadOut();
            return dto.Type switch
            {
                "Line" => new LineLeadOut { Length = dto.Length, ApproachAngle = dto.ApproachAngle },
                "Arc" => new ArcLeadOut { Radius = dto.Radius },
                "Microtab" => new MicrotabLeadOut { GapSize = dto.GapSize },
                _ => new NoLeadOut()
            };
        }

        private class CuttingParametersDto
        {
            public LeadInDto ExternalLeadIn { get; set; }
            public LeadOutDto ExternalLeadOut { get; set; }
            public LeadInDto InternalLeadIn { get; set; }
            public LeadOutDto InternalLeadOut { get; set; }
            public LeadInDto ArcCircleLeadIn { get; set; }
            public LeadOutDto ArcCircleLeadOut { get; set; }
            public bool TabsEnabled { get; set; }
            public double TabWidth { get; set; }
            public double PierceClearance { get; set; }
            public double AutoTabMinSize { get; set; }
            public double AutoTabMaxSize { get; set; }
        }

        private class LeadInDto
        {
            public string Type { get; set; } = "None";
            public double Length { get; set; }
            public double ApproachAngle { get; set; }
            public double Radius { get; set; }
            public double LineLength { get; set; }
            public double ArcRadius { get; set; }
            public double Kerf { get; set; }
            public double Length1 { get; set; }
            public double Angle1 { get; set; }
            public double Length2 { get; set; }
            public double Angle2 { get; set; }
        }

        private class LeadOutDto
        {
            public string Type { get; set; } = "None";
            public double Length { get; set; }
            public double ApproachAngle { get; set; }
            public double Radius { get; set; }
            public double GapSize { get; set; }
        }
    }
}
