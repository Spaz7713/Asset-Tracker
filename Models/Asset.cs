// Models/Asset.cs
using System.ComponentModel.DataAnnotations;

namespace MediaTracker.Models
{
    public enum AssetType
    {
        Equipment, // Cameras, lighting, mics
        Software,  // Adobe licenses, plugins
        Supplies   // Hard drives, seamless paper, gaffer tape
    }

    public enum AssetStatus
    {
        Available,
        CheckedOut,
        Maintenance,
        Depleted
    }

    public class Asset
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public AssetType Type { get; set; }

        [StringLength(50)]
        public string SKU { get; set; } = string.Empty;

        [StringLength(100)]
        public string Location { get; set; } = string.Empty;

        [Required]
        public int Quantity { get; set; }

        [Required]
        public AssetStatus Status { get; set; }

        [DataType(DataType.Date)]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
