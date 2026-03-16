using Microsoft.EntityFrameworkCore;
using NazmEWasl.Web.Models.Domain;

namespace NazmEWasl.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Song> Songs => Set<Song>();
    public DbSet<Verse> Verses => Set<Verse>();
    public DbSet<PipelineJob> PipelineJobs => Set<PipelineJob>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<TranslationBatch> TranslationBatches => Set<TranslationBatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Song>(entity =>
        {
            entity.HasIndex(s => s.SongId).IsUnique();
            entity.Property(s => s.SongId).IsRequired();
            entity.Property(s => s.Title).IsRequired();
            entity.Property(s => s.Artist).IsRequired();
        });

        modelBuilder.Entity<Verse>(entity =>
        {
            entity.HasIndex(v => new { v.SongId, v.VerseNumber }).IsUnique();
            entity.Property(v => v.PersianText).IsRequired();
            entity.HasOne(v => v.Song)
                  .WithMany(s => s.Verses)
                  .HasForeignKey(v => v.SongId);
        });

        modelBuilder.Entity<PipelineJob>(entity =>
        {
            entity.HasOne(j => j.Song)
                  .WithMany(s => s.Jobs)
                  .HasForeignKey(j => j.SongId);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(s => s.Key);
        });

        modelBuilder.Entity<TranslationBatch>(entity =>
        {
            entity.HasOne(b => b.Song)
                  .WithMany()
                  .HasForeignKey(b => b.SongId);
        });
    }
}
