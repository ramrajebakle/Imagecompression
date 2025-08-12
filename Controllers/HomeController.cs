using System.Diagnostics;
using Image_Compress.Models;
using Image_Compress.Data;
using Image_Compress.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModelUploadedImage = Image_Compress.Models.UploadedImage;

namespace Image_Compress.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ApplicationDbContext _context;
        private readonly IImageCompressionService _imageCompressionService;

        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment webHostEnvironment, ApplicationDbContext context, IImageCompressionService imageCompressionService)
        {
            _logger = logger;
            _webHostEnvironment = webHostEnvironment;
            _context = context;
            _imageCompressionService = imageCompressionService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                // Get recent uploaded images from database
                var recentImages = await _context.UploadedImages
                    .OrderByDescending(x => x.UploadedAt)
                    .Take(10)
                    .ToListAsync();

                ViewBag.RecentImages = recentImages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to database");
                ViewBag.RecentImages = new List<Image_Compress.Models.UploadedImage>();
                TempData["Warning"] = "Unable to connect to database. Please check your connection.";
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UploadImage(IFormFile imageFile)
        {
            if (imageFile == null || imageFile.Length == 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return RedirectToAction("Index");
            }

            // Get file extension (allow any extension) with length validation
            var fileExtension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(fileExtension))
            {
                fileExtension = ".bin"; // Default extension for files without extension
            }

            // Truncate file extension if it's too long for database constraint
            if (fileExtension.Length > 10)
            {
                fileExtension = fileExtension.Substring(0, 10);
            }

            try
            {
                // Create uploads directory if it doesn't exist
                var uploadsPath = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsPath))
                {
                    Directory.CreateDirectory(uploadsPath);
                }

                // Generate unique filename
                var uniqueFileName = Guid.NewGuid().ToString() + fileExtension;
                var filePath = Path.Combine(uploadsPath, uniqueFileName);

                // Save the file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }

                // Truncate other fields that might exceed database constraints
                var originalFileName = imageFile.FileName;
                if (originalFileName.Length > 255)
                {
                    originalFileName = originalFileName.Substring(0, 255);
                }

                var contentType = imageFile.ContentType ?? "application/octet-stream";
                if (contentType.Length > 50)
                {
                    contentType = contentType.Substring(0, 50);
                }

                var storedFileName = uniqueFileName;
                if (storedFileName.Length > 255)
                {
                    storedFileName = storedFileName.Substring(0, 255);
                }

                // Create database record with validated lengths
                var uploadedImage = new Image_Compress.Models.UploadedImage
                {
                    OriginalFileName = originalFileName,
                    StoredFileName = storedFileName,
                    FileExtension = fileExtension,
                    FileSizeBytes = imageFile.Length,
                    ContentType = contentType,
                    FilePath = "/uploads/" + uniqueFileName,
                    UploadedAt = DateTime.UtcNow,
                    UploadedBy = "Anonymous" // You can replace this with actual user info
                };

                _context.UploadedImages.Add(uploadedImage);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"File '{imageFile.FileName}' uploaded successfully and saved to database!";
                TempData["UploadedImagePath"] = "/uploads/" + uniqueFileName;
                TempData["UploadedImageId"] = uploadedImage.Id;
                TempData["IsImageFormat"] = _imageCompressionService.IsValidImageFormat(contentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file: {ErrorMessage}", ex.Message);
                TempData["Error"] = $"An error occurred while uploading the file: {ex.Message}";
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> ImageGallery()
        {
            var images = await _context.UploadedImages
                .OrderByDescending(x => x.UploadedAt)
                .ToListAsync();

            return View(images);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteImage(int id)
        {
            try
            {
                var image = await _context.UploadedImages.FindAsync(id);
                if (image != null)
                {
                    // Delete physical file
                    if (!string.IsNullOrEmpty(image.FilePath))
                    {
                        var physicalPath = Path.Combine(_webHostEnvironment.WebRootPath, image.FilePath.TrimStart('/'));
                        if (System.IO.File.Exists(physicalPath))
                        {
                            System.IO.File.Delete(physicalPath);
                        }
                    }

                    // Delete database record
                    _context.UploadedImages.Remove(image);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "Image deleted successfully!";
                }
                else
                {
                    TempData["Error"] = "Image not found.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting image");
                TempData["Error"] = "An error occurred while deleting the image.";
            }

            return RedirectToAction("ImageGallery");
        }

        [HttpGet]
        public IActionResult DownloadImage(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return NotFound();

            var physicalPath = Path.Combine(_webHostEnvironment.WebRootPath, filePath.TrimStart('/'));
            if (!System.IO.File.Exists(physicalPath))
                return NotFound();

            var contentType = "application/octet-stream";
            return PhysicalFile(physicalPath, contentType, Path.GetFileName(physicalPath));
        }

        [HttpGet]
        public async Task<IActionResult> DownloadCompressed(int id, int quality = 75)
        {
            try
            {
                var image = await _context.UploadedImages.FindAsync(id);
                if (image == null)
                    return NotFound("Image not found in database");

                if (!_imageCompressionService.IsValidImageFormat(image.ContentType))
                    return BadRequest("This file format cannot be compressed");

                // If already compressed and file exists, serve it directly
                if (image.IsCompressed && !string.IsNullOrEmpty(image.CompressedBlobUrl) && System.IO.File.Exists(image.CompressedBlobUrl))
                {
                    var fileName = $"compressed_{quality}_{image.OriginalFileName}";
                    var fileBytes = await System.IO.File.ReadAllBytesAsync(image.CompressedBlobUrl);
                    return File(fileBytes, image.ContentType, fileName);
                }

                // Verify physical file exists before processing
                var physicalPath = Path.Combine(_webHostEnvironment.WebRootPath, image.FilePath.TrimStart('/'));
                if (!System.IO.File.Exists(physicalPath))
                {
                    _logger.LogError("Physical file missing for image ID {ImageId}. Expected path: {PhysicalPath}", id, physicalPath);
                    return NotFound("The original image file was not found on the server");
                }

                var compressedBytes = await _imageCompressionService.GetCompressedImageBytesAsync(id, quality);
                var fileNameNew = $"compressed_{quality}_{image.OriginalFileName}";
                return File(compressedBytes, image.ContentType, fileNameNew);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "File not found when downloading compressed image {ImageId}", id);
                return NotFound("The requested image file could not be found");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading compressed image {ImageId}", id);
                return StatusCode(500, "An error occurred while processing the image");
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadOriginal(int id)
        {
            try
            {
                var image = await _context.UploadedImages.FindAsync(id);
                if (image == null)
                    return NotFound("Image not found");

                if (string.IsNullOrEmpty(image.FilePath))
                    return NotFound("File path not found");

                var physicalPath = Path.Combine(_webHostEnvironment.WebRootPath, image.FilePath.TrimStart('/'));
                if (!System.IO.File.Exists(physicalPath))
                    return NotFound("Physical file not found");

                var fileBytes = await System.IO.File.ReadAllBytesAsync(physicalPath);
                return File(fileBytes, image.ContentType, image.OriginalFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading original image");
                return StatusCode(500, "Error accessing file");
            }
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpGet]
        public async Task<IActionResult> GetCompressedSize(int id, int quality = 75)
        {
            try
            {
                var image = await _context.UploadedImages.FindAsync(id);
                if (image == null)
                    return Json(new { success = false, error = "Image not found" });

                if (!_imageCompressionService.IsValidImageFormat(image.ContentType))
                    return Json(new { success = false, error = "Format not compressible" });

                // If already compressed and file exists, return its size
                if (image.IsCompressed && !string.IsNullOrEmpty(image.CompressedBlobUrl) && System.IO.File.Exists(image.CompressedBlobUrl))
                {
                    var compressedSize = new System.IO.FileInfo(image.CompressedBlobUrl).Length;
                    var compressedSizeKB = Math.Round(compressedSize / 1024.0, 1);
                    var originalSizeKB = Math.Round(image.FileSizeBytes / 1024.0, 1);
                    var compressionRatio = Math.Round((1 - (double)compressedSize / image.FileSizeBytes) * 100, 1);
                    return Json(new {
                        success = true,
                        compressedSize = compressedSizeKB,
                        originalSize = originalSizeKB,
                        compressionRatio = compressionRatio
                    });
                }

                var compressedBytes = await _imageCompressionService.GetCompressedImageBytesAsync(id, quality);
                var compressedSizeKBNew = Math.Round(compressedBytes.Length / 1024.0, 1);
                var originalSizeKBNew = Math.Round(image.FileSizeBytes / 1024.0, 1);
                var compressionRatioNew = Math.Round((1 - (double)compressedBytes.Length / image.FileSizeBytes) * 100, 1);

                return Json(new {
                    success = true,
                    compressedSize = compressedSizeKBNew,
                    originalSize = originalSizeKBNew,
                    compressionRatio = compressionRatioNew
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating compressed size for image {ImageId}", id);
                return Json(new { success = false, error = "Error calculating compressed size" });
            }
        }
    }
}
