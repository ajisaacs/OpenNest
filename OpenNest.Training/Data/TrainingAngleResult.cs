using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenNest.Training.Data
{
    [Table("AngleResults")]
    public class TrainingAngleResult
    {
        [Key]
        public long Id { get; set; }

        public long RunId { get; set; }
        public double AngleDeg { get; set; }
        public string Direction { get; set; }
        public int PartCount { get; set; }

        [ForeignKey(nameof(RunId))]
        public TrainingRun Run { get; set; }
    }
}
