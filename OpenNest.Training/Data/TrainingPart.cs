using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenNest.Training.Data
{
    [Table("Parts")]
    public class TrainingPart
    {
        [Key]
        public long Id { get; set; }

        [MaxLength(260)]
        public string FileName { get; set; }

        public double Area { get; set; }
        public double Convexity { get; set; }
        public double AspectRatio { get; set; }
        public double BBFill { get; set; }
        public double Circularity { get; set; }
        public double PerimeterToAreaRatio { get; set; }
        public int VertexCount { get; set; }
        public byte[] Bitmask { get; set; }
        public string GeometryData { get; set; }

        public List<TrainingRun> Runs { get; set; } = new();
    }
}
