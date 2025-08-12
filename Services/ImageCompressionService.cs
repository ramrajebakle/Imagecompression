using Image_Compress.Models;
using Image_Compress.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using Microsoft.EntityFrameworkCore;

namespace Image_Compress.Services
{
    public class ImageCompressionService : IImageCompressionService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ImageCompressionService> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly string[] _allowedImageTypes = { "image/jpeg", "image/jpg", "image/png", "image/webp", "image/tiff", "image/bmp" };

        public ImageCompressionService(ApplicationDbContext context, ILogger<ImageCompressionService> logger, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<byte[]> CompressImageAsync(byte[] imageData, string contentType, int quality = 40)
        {
            try
            {
                _logger.LogInformation("Starting compression for image of size: {Size:N0} bytes", imageData.Length);

                using var image = Image.Load(imageData);

                // Resize very large images to prevent memory issues
                const int maxDimension = 8000;
                if (image.Width > maxDimension || image.Height > maxDimension)
                {
                    _logger.LogInformation("Large image detected ({Width}x{Height}). Resizing for memory optimization.", image.Width, image.Height);

                    var largestDimension = Math.Max(image.Width, image.Height);
                    var scaleFactor = (double)maxDimension / largestDimension;
                    var newWidth = (int)(image.Width * scaleFactor);
                    var newHeight = (int)(image.Height * scaleFactor);

                    image.Mutate(x => x.Resize(newWidth, newHeight));
                    _logger.LogInformation("Image resized to {NewWidth}x{NewHeight}", newWidth, newHeight);
                }

                using var outputStream = new MemoryStream();

                switch (contentType.ToLower())
                {
                    case "image/jpeg":
                    case "image/jpg":
                        var jpegEncoder = new JpegEncoder { Quality = 60 };
                        await image.SaveAsJpegAsync(outputStream, jpegEncoder);
                        break;
                    case "image/png":
                        image.Mutate(x => x.AutoOrient());
                        var pngEncoder = new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression };
                        await image.SaveAsPngAsync(outputStream, pngEncoder);
                        break;
                    case "image/webp":
                        var webpEncoder = new WebpEncoder { FileFormat = WebpFileFormatType.Lossless };
                        await image.SaveAsWebpAsync(outputStream, webpEncoder);
                        break;
                    default:
                        throw new ArgumentException($"Unsupported image format: {contentType}");
                }

                var compressedBytes = outputStream.ToArray();
                _logger.LogInformation("Compression completed. Original: {OriginalSize:N0} bytes, Compressed: {CompressedSize:N0} bytes",
                    imageData.Length, compressedBytes.Length);

                return compressedBytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error compressing image of size {Size:N0} bytes", imageData?.Length ?? 0);
                throw;
            }
        }

        public async Task<UploadedImage> SaveImageAsync(IFormFile file, string uploadPath)
        {
            try
            {
                var fileName = GenerateUniqueFileName(file.FileName);
                var filePath = Path.Combine(uploadPath, fileName);

                Directory.CreateDirectory(uploadPath);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var uploadedImage = new UploadedImage
                {
                    OriginalFileName = file.FileName,
                    StoredFileName = fileName,
                    FileExtension = Path.GetExtension(file.FileName).ToLower(),
                    FileSizeBytes = file.Length,
                    ContentType = file.ContentType,
                    FilePath = filePath,
                    UploadedAt = DateTime.UtcNow
                };

                _context.UploadedImages.Add(uploadedImage);
                await _context.SaveChangesAsync();

                return uploadedImage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving image");
                throw;
            }
        }

        public async Task<UploadedImage> CompressAndSaveImageAsync(UploadedImage originalImage, string compressedPath, int quality = 75)
        {
            try
            {
                // Prevent re-compressing if already compressed
                if (originalImage.IsCompressed && !string.IsNullOrEmpty(originalImage.CompressedBlobUrl))
                {
                    _logger.LogInformation("Image with ID {ImageId} is already compressed. Skipping compression.", originalImage.Id);
                    return originalImage;
                }

                if (string.IsNullOrEmpty(originalImage.FilePath) || !File.Exists(originalImage.FilePath))
                {
                    throw new FileNotFoundException("Original image file not found");
                }

                var originalBytes = await File.ReadAllBytesAsync(originalImage.FilePath);
                var compressedBytes = await CompressImageAsync(originalBytes, originalImage.ContentType, quality);

                var compressedFileName = $"compressed_{originalImage.StoredFileName}";
                var compressedFilePath = Path.Combine(compressedPath, compressedFileName);

                Directory.CreateDirectory(compressedPath);
                await File.WriteAllBytesAsync(compressedFilePath, compressedBytes);

                originalImage.IsCompressed = true;
                originalImage.CompressedSizeBytes = compressedBytes.Length;
                originalImage.CompressedBlobUrl = compressedFilePath;
                originalImage.CompressedAt = DateTime.UtcNow;

                _context.UploadedImages.Update(originalImage);
                await _context.SaveChangesAsync();

                return originalImage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error compressing and saving image");
                throw;
            }
        }

        public bool IsValidImageFormat(string contentType)
        {
            return _allowedImageTypes.Contains(contentType?.ToLower());
        }

        public string GenerateUniqueFileName(string originalFileName)
        {
            var extension = Path.GetExtension(originalFileName);
            var fileName = Path.GetFileNameWithoutExtension(originalFileName);
            var uniqueId = Guid.NewGuid().ToString("N")[..8];
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

            return $"{fileName}_{timestamp}_{uniqueId}{extension}";
        }

        public async Task<byte[]> GetImageBytesAsync(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    throw new FileNotFoundException("Image file not found");
                }

                return await File.ReadAllBytesAsync(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading image file");
                throw;
            }
        }

        public async Task<byte[]> GetCompressedImageBytesAsync(int imageId, int quality = 75)
        {
            try
            {
                var image = await _context.UploadedImages.FindAsync(imageId);
                if (image == null)
                {
                    _logger.LogWarning("Image record not found for ID: {ImageId}", imageId);
                    throw new FileNotFoundException($"Image record not found for ID: {imageId}");
                }

                if (string.IsNullOrEmpty(image.FilePath))
                {
                    _logger.LogWarning("Image file path is empty for ID: {ImageId}", imageId);
                    throw new FileNotFoundException($"Image file path not found in database for ID: {imageId}");
                }

                // Convert web path to physical path
                var physicalPath = Path.Combine(_webHostEnvironment.WebRootPath, image.FilePath.TrimStart('/'));

                _logger.LogInformation("Attempting to access file at: {PhysicalPath}", physicalPath);

                if (!File.Exists(physicalPath))
                {
                    _logger.LogError("Physical file not found. Web path: {WebPath}, Physical path: {PhysicalPath}", image.FilePath, physicalPath);
                    throw new FileNotFoundException($"Original image file not found. Expected location: {physicalPath}");
                }

                // Read original image in chunks for large files
                byte[] originalBytes;
                using (var fileStream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                {
                    originalBytes = new byte[fileStream.Length];
                    var totalBytesRead = 0;
                    while (totalBytesRead < fileStream.Length)
                    {
                        var bytesRead = await fileStream.ReadAsync(originalBytes, totalBytesRead, (int)(fileStream.Length - totalBytesRead));
                        if (bytesRead == 0)
                            break;
                        totalBytesRead += bytesRead;
                    }
                }

                _logger.LogInformation("Successfully read {ByteCount} bytes from file for compression", originalBytes.Length);

                // Force garbage collection for large images
                if (originalBytes.Length > 10 * 1024 * 1024) // 10MB
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }

                var compressedBytes = await CompressImageAsync(originalBytes, image.ContentType, quality);
                _logger.LogInformation("Successfully compressed image. Original: {OriginalSize} bytes, Compressed: {CompressedSize} bytes",
                    originalBytes.Length, compressedBytes.Length);

                return compressedBytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting compressed image for ID: {ImageId}. Details: {ErrorMessage}", imageId, ex.Message);
                throw;
            }
        }
    }
}