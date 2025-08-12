using System.ComponentModel.DataAnnotations;

namespace Image_Compress.Models
{
    public class UploadedImage
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(255)]
        public string OriginalFileName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(255)]
        public string StoredFileName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(10)]
        public string FileExtension { get; set; } = string.Empty;
        
        public long FileSizeBytes { get; set; }
        
        [StringLength(50)]
        public string ContentType { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string? FilePath { get; set; }
        
        [StringLength(1000)]
        public string? BlobUrl { get; set; }
        
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        
        [StringLength(100)]
        public string? UploadedBy { get; set; }
        
        public bool IsCompressed { get; set; } = false;
        
        public long? CompressedSizeBytes { get; set; }
        
        [StringLength(1000)]
        public string? CompressedBlobUrl { get; set; }
        
        public DateTime? CompressedAt { get; set; }
    }
}