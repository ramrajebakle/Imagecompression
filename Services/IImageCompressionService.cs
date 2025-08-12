using Image_Compress.Models;

namespace Image_Compress.Services
{
    public interface IImageCompressionService
    {
        Task<byte[]> CompressImageAsync(byte[] imageData, string contentType, int quality = 75);
        Task<UploadedImage> SaveImageAsync(IFormFile file, string uploadPath);
        Task<UploadedImage> CompressAndSaveImageAsync(UploadedImage originalImage, string compressedPath, int quality = 75);
        bool IsValidImageFormat(string contentType);
        string GenerateUniqueFileName(string originalFileName);
        Task<byte[]> GetImageBytesAsync(string filePath);
        Task<byte[]> GetCompressedImageBytesAsync(int imageId, int quality = 75);
    }
}