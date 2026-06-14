// Data/ApplicationDbContext.cs
using Microsoft.EntityFrameworkCore;
using MediaTracker.Models;

namespace MediaTracker.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Asset> Assets { get; set; }
        public DbSet<MediaTracker.Models.Label> Labels { get; set; }
        public DbSet<MediaTracker.Models.Employee> Employees { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Seed initial digital media items
            modelBuilder.Entity<Asset>().HasData(
                new Asset { Id = 1, Name = "Sony FX3 Cinema Camera", Type = AssetType.Equipment, SKU = "CAM-FX3-01", Location = "Locker A", Quantity = 3, Status = AssetStatus.Available },
                new Asset { Id = 2, Name = "Adobe Creative Cloud License", Type = AssetType.Software, SKU = "SW-ACC-SUB", Location = "Cloud", Quantity = 15, Status = AssetStatus.Available },
                new Asset { Id = 3, Name = "SanDisk Professional 2TB SSD", Type = AssetType.Supplies, SKU = "SUP-SSD-2TB", Location = "Locker C", Quantity = 25, Status = AssetStatus.Available }
            );

            // Seed a sample approved employee for testing
            modelBuilder.Entity<Employee>().HasData(
                new Employee { Id = 1, Name = "Spencer", EmployeeId = "EMP-1001", IsApproved = true }
            );
        }
    }
}
