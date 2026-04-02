using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.Engine.BestFit;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using static OpenNest.IO.NestFormat;

namespace OpenNest.IO
{
    public sealed class NestWriter
    {
        private const int OutputPrecision = 10;
        private const string CoordinateFormat = "0.##########";

        private readonly Nest nest;
        private Dictionary<int, Drawing> drawingDict;

        public NestWriter(Nest nest)
        {
            this.drawingDict = new Dictionary<int, Drawing>();
            this.nest = nest;
        }

        public bool Write(string file)
        {
            using var fileStream = new FileStream(file, FileMode.Create);
            return Write(fileStream);
        }

        public bool Write(Stream stream)
        {
            nest.DateLastModified = DateTime.Now;
            SetDrawingIds();

            using var zipArchive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

            WriteNestJson(zipArchive);
            WritePrograms(zipArchive);
            WriteBestFits(zipArchive);

            return true;
        }

        private void SetDrawingIds()
        {
            var id = 1;
            foreach (var drawing in nest.Drawings)
            {
                drawingDict.Add(id, drawing);
                id++;
            }
        }

        private void WriteNestJson(ZipArchive zipArchive)
        {
            var dto = BuildNestDto();
            var json = JsonSerializer.Serialize(dto, JsonOptions);

            var entry = zipArchive.CreateEntry("nest.json");
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(json);
        }

        private NestDto BuildNestDto()
        {
            return new NestDto
            {
                Version = 2,
                Name = nest.Name ?? "",
                Units = nest.Units.ToString(),
                Customer = nest.Customer ?? "",
                DateCreated = nest.DateCreated.ToString("o"),
                DateLastModified = nest.DateLastModified.ToString("o"),
                Notes = nest.Notes ?? "",
                AssistGas = nest.AssistGas ?? "",
                Thickness = nest.Thickness,
                Material = new MaterialDto
                {
                    Name = nest.Material.Name ?? "",
                    Grade = nest.Material.Grade ?? "",
                    Density = nest.Material.Density
                },
                PlateDefaults = BuildPlateDefaultsDto(),
                Drawings = BuildDrawingDtos(),
                Plates = BuildPlateDtos()
            };
        }

        private PlateDefaultsDto BuildPlateDefaultsDto()
        {
            var pd = nest.PlateDefaults;
            return new PlateDefaultsDto
            {
                Size = new SizeDto { Width = pd.Size.Width, Length = pd.Size.Length },
                Thickness = nest.Thickness,
                Quadrant = pd.Quadrant,
                PartSpacing = pd.PartSpacing,
                Material = new MaterialDto
                {
                    Name = nest.Material.Name ?? "",
                    Grade = nest.Material.Grade ?? "",
                    Density = nest.Material.Density
                },
                EdgeSpacing = new SpacingDto
                {
                    Left = pd.EdgeSpacing.Left,
                    Top = pd.EdgeSpacing.Top,
                    Right = pd.EdgeSpacing.Right,
                    Bottom = pd.EdgeSpacing.Bottom
                }
            };
        }

        private List<DrawingDto> BuildDrawingDtos()
        {
            var list = new List<DrawingDto>();
            foreach (var kvp in drawingDict.OrderBy(k => k.Key))
            {
                var d = kvp.Value;
                list.Add(new DrawingDto
                {
                    Id = kvp.Key,
                    Name = d.Name ?? "",
                    Customer = d.Customer ?? "",
                    Color = new ColorDto { A = d.Color.A, R = d.Color.R, G = d.Color.G, B = d.Color.B },
                    Quantity = new QuantityDto { Required = d.Quantity.Required },
                    Priority = d.Priority,
                    Constraints = new ConstraintsDto
                    {
                        StepAngle = d.Constraints.StepAngle,
                        StartAngle = d.Constraints.StartAngle,
                        EndAngle = d.Constraints.EndAngle,
                        Allow180Equivalent = d.Constraints.Allow180Equivalent
                    },
                    Material = new MaterialDto
                    {
                        Name = d.Material.Name ?? "",
                        Grade = d.Material.Grade ?? "",
                        Density = d.Material.Density
                    },
                    Source = new SourceDto
                    {
                        Path = d.Source.Path ?? "",
                        Offset = new OffsetDto { X = d.Source.Offset.X, Y = d.Source.Offset.Y }
                    },
                    Bends = d.Bends?.Select(b => new BendDto
                    {
                        StartX = b.StartPoint.X,
                        StartY = b.StartPoint.Y,
                        EndX = b.EndPoint.X,
                        EndY = b.EndPoint.Y,
                        Direction = b.Direction.ToString(),
                        Angle = b.Angle,
                        Radius = b.Radius,
                        NoteText = b.NoteText ?? ""
                    }).ToList() ?? new List<BendDto>()
                });
            }
            return list;
        }

        private List<PlateDto> BuildPlateDtos()
        {
            var list = new List<PlateDto>();
            for (var i = 0; i < nest.Plates.Count; i++)
            {
                var plate = nest.Plates[i];
                var parts = new List<PartDto>();
                foreach (var part in plate.Parts.Where(p => !p.BaseDrawing.IsCutOff))
                {
                    var match = drawingDict.Where(dwg => dwg.Value == part.BaseDrawing).FirstOrDefault();
                    parts.Add(new PartDto
                    {
                        DrawingId = match.Key,
                        X = part.Location.X,
                        Y = part.Location.Y,
                        Rotation = part.Rotation,
                        HasManualLeadIns = part.HasManualLeadIns,
                        LeadInsLocked = part.LeadInsLocked
                    });
                }

                var cutoffs = new List<CutOffDto>();
                foreach (var cutoff in plate.CutOffs)
                {
                    cutoffs.Add(new CutOffDto
                    {
                        X = cutoff.Position.X,
                        Y = cutoff.Position.Y,
                        Axis = cutoff.Axis == CutOffAxis.Vertical ? "vertical" : "horizontal",
                        StartLimit = cutoff.StartLimit,
                        EndLimit = cutoff.EndLimit
                    });
                }

                list.Add(new PlateDto
                {
                    Id = i + 1,
                    Size = new SizeDto { Width = plate.Size.Width, Length = plate.Size.Length },
                    Quadrant = plate.Quadrant,
                    Quantity = plate.Quantity,
                    PartSpacing = plate.PartSpacing,
                    EdgeSpacing = new SpacingDto
                    {
                        Left = plate.EdgeSpacing.Left,
                        Top = plate.EdgeSpacing.Top,
                        Right = plate.EdgeSpacing.Right,
                        Bottom = plate.EdgeSpacing.Bottom
                    },
                    Parts = parts,
                    CutOffs = cutoffs,
                    GrainAngle = plate.GrainAngle
                });
            }
            return list;
        }

        private List<BestFitSetDto> BuildBestFitDtos(Drawing drawing)
        {
            var allBestFits = BestFitCache.GetAllForDrawing(drawing);
            var sets = new List<BestFitSetDto>();

            // Only save best-fit sets for plate sizes actually used in this nest.
            var plateSizes = new HashSet<(double, double, double)>();
            foreach (var plate in nest.Plates)
                plateSizes.Add((plate.Size.Width, plate.Size.Length, plate.PartSpacing));

            foreach (var kvp in allBestFits)
            {
                if (!plateSizes.Contains((kvp.Key.PlateWidth, kvp.Key.PlateHeight, kvp.Key.Spacing)))
                    continue;

                var results = kvp.Value
                    .Where(r => r.Keep)
                    .Select(r => new BestFitResultDto
                    {
                        Part1Rotation = r.Candidate.Part1Rotation,
                        Part2Rotation = r.Candidate.Part2Rotation,
                        Part2OffsetX = r.Candidate.Part2Offset.X,
                        Part2OffsetY = r.Candidate.Part2Offset.Y,
                        StrategyType = r.Candidate.StrategyIndex,
                        TestNumber = r.Candidate.TestNumber,
                        CandidateSpacing = r.Candidate.Spacing,
                        RotatedArea = r.RotatedArea,
                        BoundingWidth = r.BoundingWidth,
                        BoundingHeight = r.BoundingHeight,
                        OptimalRotation = r.OptimalRotation,
                        Keep = r.Keep,
                        Reason = r.Reason ?? "",
                        TrueArea = r.TrueArea,
                        HullAngles = r.HullAngles ?? new List<double>()
                    }).ToList();

                sets.Add(new BestFitSetDto
                {
                    PlateWidth = kvp.Key.PlateWidth,
                    PlateHeight = kvp.Key.PlateHeight,
                    Spacing = kvp.Key.Spacing,
                    Results = results
                });
            }

            return sets;
        }

        private void WriteBestFits(ZipArchive zipArchive)
        {
            foreach (var kvp in drawingDict.OrderBy(k => k.Key))
            {
                var sets = BuildBestFitDtos(kvp.Value);
                if (sets.Count == 0)
                    continue;

                var json = JsonSerializer.Serialize(sets, JsonOptions);
                var entry = zipArchive.CreateEntry($"bestfits/bestfit-{kvp.Key}");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(json);
            }
        }

        private void WritePrograms(ZipArchive zipArchive)
        {
            foreach (var kvp in drawingDict.OrderBy(k => k.Key))
            {
                var name = $"programs/program-{kvp.Key}";
                var stream = new MemoryStream();
                WriteDrawing(stream, kvp.Value);

                var entry = zipArchive.CreateEntry(name);
                using var entryStream = entry.Open();
                stream.CopyTo(entryStream);
            }
        }

        private void WriteDrawing(Stream stream, Drawing drawing)
        {
            var program = drawing.Program;
            var writer = new StreamWriter(stream);
            writer.AutoFlush = true;

            // Emit variable definitions before G-code
            foreach (var v in program.Variables.Values)
            {
                var line = $"{v.Name} = {v.Expression}";
                if (v.Inline) line += " inline";
                if (v.Global) line += " global";
                writer.WriteLine(line);
            }

            writer.WriteLine(program.Mode == Mode.Absolute ? "G90" : "G91");

            for (var i = 0; i < drawing.Program.Length; ++i)
            {
                var code = drawing.Program[i];
                writer.WriteLine(GetCodeString(code));
            }

            stream.Position = 0;
        }

        private string FormatCoord(double value, string axis, Dictionary<string, string> variableRefs)
        {
            if (variableRefs != null && variableRefs.TryGetValue(axis, out var varName))
                return $"${varName}";
            return System.Math.Round(value, OutputPrecision).ToString(CoordinateFormat);
        }

        private string GetCodeString(ICode code)
        {
            switch (code.Type)
            {
                case CodeType.ArcMove:
                    {
                        var sb = new StringBuilder();
                        var arcMove = (ArcMove)code;
                        var refs = arcMove.VariableRefs;

                        var x = FormatCoord(arcMove.EndPoint.X, "X", refs);
                        var y = FormatCoord(arcMove.EndPoint.Y, "Y", refs);
                        var i = FormatCoord(arcMove.CenterPoint.X, "I", refs);
                        var j = FormatCoord(arcMove.CenterPoint.Y, "J", refs);

                        sb.Append(arcMove.Rotation == RotationType.CW
                            ? $"G02X{x}Y{y}I{i}J{j}"
                            : $"G03X{x}Y{y}I{i}J{j}");

                        if (arcMove.Layer != LayerType.Cut)
                            sb.Append(GetLayerString(arcMove.Layer));

                        if (arcMove.Suppressed)
                            sb.Append(":SUPPRESSED");

                        return sb.ToString();
                    }

                case CodeType.Comment:
                    {
                        var comment = (Comment)code;
                        return ":" + comment.Value;
                    }

                case CodeType.LinearMove:
                    {
                        var sb = new StringBuilder();
                        var linearMove = (LinearMove)code;
                        var refs = linearMove.VariableRefs;

                        sb.Append($"G01X{FormatCoord(linearMove.EndPoint.X, "X", refs)}Y{FormatCoord(linearMove.EndPoint.Y, "Y", refs)}");

                        if (linearMove.Layer != LayerType.Cut)
                            sb.Append(GetLayerString(linearMove.Layer));

                        if (linearMove.Suppressed)
                            sb.Append(":SUPPRESSED");

                        return sb.ToString();
                    }

                case CodeType.RapidMove:
                    {
                        var rapidMove = (RapidMove)code;
                        var refs = rapidMove.VariableRefs;

                        return $"G00X{FormatCoord(rapidMove.EndPoint.X, "X", refs)}Y{FormatCoord(rapidMove.EndPoint.Y, "Y", refs)}";
                    }

                case CodeType.SetFeedrate:
                    {
                        var setFeedrate = (Feedrate)code;
                        if (setFeedrate.VariableRef != null)
                            return $"F${setFeedrate.VariableRef}";
                        return "F" + setFeedrate.Value;
                    }

                case CodeType.SetKerf:
                    {
                        var setKerf = (Kerf)code;

                        switch (setKerf.Value)
                        {
                            case KerfType.None: return "G40";
                            case KerfType.Left: return "G41";
                            case KerfType.Right: return "G42";
                        }

                        break;
                    }

                case CodeType.SubProgramCall:
                    {
                        var subProgramCall = (SubProgramCall)code;
                        break;
                    }
            }

            return string.Empty;
        }

        private string GetLayerString(LayerType layer)
        {
            switch (layer)
            {
                case LayerType.Display:
                    return ":DISPLAY";

                case LayerType.Leadin:
                    return ":LEADIN";

                case LayerType.Leadout:
                    return ":LEADOUT";

                case LayerType.Scribe:
                    return ":SCRIBE";

                default:
                    return string.Empty;
            }
        }
    }
}
