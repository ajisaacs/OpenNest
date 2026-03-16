using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OpenNest.Training.Data
{
    [Table("Runs")]
    public class TrainingRun
    {
        [Key]
        public long Id { get; set; }

        public long PartId { get; set; }
        public double SheetWidth { get; set; }
        public double SheetHeight { get; set; }
        public double Spacing { get; set; }
        public int PartCount { get; set; }
        public double Utilization { get; set; }
        public long TimeMs { get; set; }
        public string LayoutData { get; set; }
        public string FilePath { get; set; }
        public string WinnerEngine { get; set; } = "";
        public long WinnerTimeMs { get; set; }
        public string RunnerUpEngine { get; set; } = "";
        public int RunnerUpPartCount { get; set; }
        public long RunnerUpTimeMs { get; set; }
        public string ThirdPlaceEngine { get; set; } = "";
        public int ThirdPlacePartCount { get; set; }
        public long ThirdPlaceTimeMs { get; set; }

        [ForeignKey(nameof(PartId))]
        public TrainingPart Part { get; set; }

        public List<TrainingAngleResult> AngleResults { get; set; } = new();
    }
}
