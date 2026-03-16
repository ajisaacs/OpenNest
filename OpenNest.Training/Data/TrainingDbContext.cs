using Microsoft.EntityFrameworkCore;

namespace OpenNest.Training.Data
{
    public class TrainingDbContext : DbContext
    {
        public DbSet<TrainingPart> Parts { get; set; }
        public DbSet<TrainingRun> Runs { get; set; }
        public DbSet<TrainingAngleResult> AngleResults { get; set; }

        private readonly string _dbPath;

        public TrainingDbContext(string dbPath)
        {
            _dbPath = dbPath;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlite($"Data Source={_dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TrainingPart>(e =>
            {
                e.HasIndex(p => p.FileName).HasDatabaseName("idx_parts_filename");
            });

            modelBuilder.Entity<TrainingRun>(e =>
            {
                e.HasIndex(r => r.PartId).HasDatabaseName("idx_runs_partid");
                e.HasOne(r => r.Part)
                    .WithMany(p => p.Runs)
                    .HasForeignKey(r => r.PartId);
            });

            modelBuilder.Entity<TrainingAngleResult>(e =>
            {
                e.HasIndex(a => a.RunId).HasDatabaseName("idx_angleresults_runid");
                e.HasOne(a => a.Run)
                    .WithMany(r => r.AngleResults)
                    .HasForeignKey(a => a.RunId);
            });
        }
    }
}
