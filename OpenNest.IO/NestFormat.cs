using System.Collections.Generic;
using System.Text.Json;

namespace OpenNest.IO
{
    public static class NestFormat
    {
        public const string FileExtension = ".nest";
        public const string FileFilter = "Nest Files (*.nest)|*.nest";

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public record NestDto
        {
            public int Version { get; init; } = 2;
            public string Name { get; init; } = "";
            public string Units { get; init; } = "Inches";
            public string Customer { get; init; } = "";
            public string DateCreated { get; init; } = "";
            public string DateLastModified { get; init; } = "";
            public string Notes { get; init; } = "";
            public PlateDefaultsDto PlateDefaults { get; init; } = new();
            public List<DrawingDto> Drawings { get; init; } = new();
            public List<PlateDto> Plates { get; init; } = new();
        }

        public record PlateDefaultsDto
        {
            public SizeDto Size { get; init; } = new();
            public double Thickness { get; init; }
            public int Quadrant { get; init; } = 1;
            public double PartSpacing { get; init; }
            public MaterialDto Material { get; init; } = new();
            public SpacingDto EdgeSpacing { get; init; } = new();
        }

        public record DrawingDto
        {
            public int Id { get; init; }
            public string Name { get; init; } = "";
            public string Customer { get; init; } = "";
            public ColorDto Color { get; init; } = new();
            public QuantityDto Quantity { get; init; } = new();
            public int Priority { get; init; }
            public ConstraintsDto Constraints { get; init; } = new();
            public MaterialDto Material { get; init; } = new();
            public SourceDto Source { get; init; } = new();
        }

        public record PlateDto
        {
            public int Id { get; init; }
            public SizeDto Size { get; init; } = new();
            public double Thickness { get; init; }
            public int Quadrant { get; init; } = 1;
            public int Quantity { get; init; } = 1;
            public double PartSpacing { get; init; }
            public MaterialDto Material { get; init; } = new();
            public SpacingDto EdgeSpacing { get; init; } = new();
            public List<PartDto> Parts { get; init; } = new();
        }

        public record PartDto
        {
            public int DrawingId { get; init; }
            public double X { get; init; }
            public double Y { get; init; }
            public double Rotation { get; init; }
        }

        public record SizeDto
        {
            public double Width { get; init; }
            public double Length { get; init; }
        }

        public record MaterialDto
        {
            public string Name { get; init; } = "";
            public string Grade { get; init; } = "";
            public double Density { get; init; }
        }

        public record SpacingDto
        {
            public double Left { get; init; }
            public double Top { get; init; }
            public double Right { get; init; }
            public double Bottom { get; init; }
        }

        public record ColorDto
        {
            public int A { get; init; } = 255;
            public int R { get; init; }
            public int G { get; init; }
            public int B { get; init; }
        }

        public record QuantityDto
        {
            public int Required { get; init; }
        }

        public record ConstraintsDto
        {
            public double StepAngle { get; init; }
            public double StartAngle { get; init; }
            public double EndAngle { get; init; }
            public bool Allow180Equivalent { get; init; }
        }

        public record SourceDto
        {
            public string Path { get; init; } = "";
            public OffsetDto Offset { get; init; } = new();
        }

        public record OffsetDto
        {
            public double X { get; init; }
            public double Y { get; init; }
        }

        public record BestFitSetDto
        {
            public double PlateWidth { get; init; }
            public double PlateHeight { get; init; }
            public double Spacing { get; init; }
            public List<BestFitResultDto> Results { get; init; } = new();
        }

        public record BestFitResultDto
        {
            public double Part1Rotation { get; init; }
            public double Part2Rotation { get; init; }
            public double Part2OffsetX { get; init; }
            public double Part2OffsetY { get; init; }
            public int StrategyType { get; init; }
            public int TestNumber { get; init; }
            public double CandidateSpacing { get; init; }
            public double RotatedArea { get; init; }
            public double BoundingWidth { get; init; }
            public double BoundingHeight { get; init; }
            public double OptimalRotation { get; init; }
            public bool Keep { get; init; }
            public string Reason { get; init; } = "";
            public double TrueArea { get; init; }
            public List<double> HullAngles { get; init; } = new();
        }
    }
}
