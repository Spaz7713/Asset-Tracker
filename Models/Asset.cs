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

    public class Asset : IValidatableObject
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
        public string? SerialNumber { get; set; }

        [StringLength(200)]
        public string? ProductKey { get; set; }

        [StringLength(100)]
        public string Location { get; set; } = string.Empty;

        [Required]
        public int Quantity { get; set; }

        [Required]
        public AssetStatus Status { get; set; }

        [DataType(DataType.Date)]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(this.SerialNumber) && string.IsNullOrWhiteSpace(this.ProductKey))
            {
                yield return new ValidationResult("Either a Serial Number or Product Key is required.", new[] { nameof(SerialNumber), nameof(ProductKey) });
            }
        }
    }
}
