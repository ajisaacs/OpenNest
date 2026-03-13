using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenNest.CNC;
using OpenNest.Math;
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
            nest.DateLastModified = DateTime.Now;
            SetDrawingIds();

            using var fileStream = new FileStream(file, FileMode.Create);
            using var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create);

            WriteNestJson(zipArchive);
            WritePrograms(zipArchive);

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
                Size = new SizeDto { Width = pd.Size.Width, Length = pd.Size.Height },
                Thickness = pd.Thickness,
                Quadrant = pd.Quadrant,
                PartSpacing = pd.PartSpacing,
                Material = new MaterialDto
                {
                    Name = pd.Material.Name ?? "",
                    Grade = pd.Material.Grade ?? "",
                    Density = pd.Material.Density
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
                    }
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
                foreach (var part in plate.Parts)
                {
                    var match = drawingDict.Where(dwg => dwg.Value == part.BaseDrawing).FirstOrDefault();
                    parts.Add(new PartDto
                    {
                        DrawingId = match.Key,
                        X = part.Location.X,
                        Y = part.Location.Y,
                        Rotation = part.Rotation
                    });
                }

                list.Add(new PlateDto
                {
                    Id = i + 1,
                    Size = new SizeDto { Width = plate.Size.Width, Length = plate.Size.Height },
                    Thickness = plate.Thickness,
                    Quadrant = plate.Quadrant,
                    Quantity = plate.Quantity,
                    PartSpacing = plate.PartSpacing,
                    Material = new MaterialDto
                    {
                        Name = plate.Material.Name ?? "",
                        Grade = plate.Material.Grade ?? "",
                        Density = plate.Material.Density
                    },
                    EdgeSpacing = new SpacingDto
                    {
                        Left = plate.EdgeSpacing.Left,
                        Top = plate.EdgeSpacing.Top,
                        Right = plate.EdgeSpacing.Right,
                        Bottom = plate.EdgeSpacing.Bottom
                    },
                    Parts = parts
                });
            }
            return list;
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

            writer.WriteLine(program.Mode == Mode.Absolute ? "G90" : "G91");

            for (var i = 0; i < drawing.Program.Length; ++i)
            {
                var code = drawing.Program[i];
                writer.WriteLine(GetCodeString(code));
            }

            stream.Position = 0;
        }

        private string GetCodeString(ICode code)
        {
            switch (code.Type)
            {
                case CodeType.ArcMove:
                    {
                        var sb = new StringBuilder();
                        var arcMove = (ArcMove)code;

                        var x = System.Math.Round(arcMove.EndPoint.X, OutputPrecision).ToString(CoordinateFormat);
                        var y = System.Math.Round(arcMove.EndPoint.Y, OutputPrecision).ToString(CoordinateFormat);
                        var i = System.Math.Round(arcMove.CenterPoint.X, OutputPrecision).ToString(CoordinateFormat);
                        var j = System.Math.Round(arcMove.CenterPoint.Y, OutputPrecision).ToString(CoordinateFormat);

                        if (arcMove.Rotation == RotationType.CW)
                            sb.Append(string.Format("G02X{0}Y{1}I{2}J{3}", x, y, i, j));
                        else
                            sb.Append(string.Format("G03X{0}Y{1}I{2}J{3}", x, y, i, j));

                        if (arcMove.Layer != LayerType.Cut)
                            sb.Append(GetLayerString(arcMove.Layer));

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

                        sb.Append(string.Format("G01X{0}Y{1}",
                            System.Math.Round(linearMove.EndPoint.X, OutputPrecision).ToString(CoordinateFormat),
                            System.Math.Round(linearMove.EndPoint.Y, OutputPrecision).ToString(CoordinateFormat)));

                        if (linearMove.Layer != LayerType.Cut)
                            sb.Append(GetLayerString(linearMove.Layer));

                        return sb.ToString();
                    }

                case CodeType.RapidMove:
                    {
                        var rapidMove = (RapidMove)code;

                        return string.Format("G00X{0}Y{1}",
                            System.Math.Round(rapidMove.EndPoint.X, OutputPrecision).ToString(CoordinateFormat),
                            System.Math.Round(rapidMove.EndPoint.Y, OutputPrecision).ToString(CoordinateFormat));
                    }

                case CodeType.SetFeedrate:
                    {
                        var setFeedrate = (Feedrate)code;
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
