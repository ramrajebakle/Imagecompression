using Microsoft.EntityFrameworkCore;
using Image_Compress.Models;

namespace Image_Compress.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<UploadedImage> UploadedImages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<UploadedImage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UploadedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasIndex(e => e.StoredFileName).IsUnique();
            });
        }
    }
}