using HueLab.Server.Models.DAO;
using HueLab.Server.Services.Database;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HueLab.ImageImporter;

public sealed record ImageImportReport(int Discovered, int Imported, int Failed);

public sealed class ImageImportService(
    HueLabDbContext database,
    ILogger<ImageImportService> logger)
{
    private const int BatchSize = 50;
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".gif", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp"
    };

    public async Task<ImageImportReport> ImportDirectoryAsync(
        string sourceDirectory,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"图片目录不存在：{fullPath}");
        }

        var discovered = 0;
        var imported = 0;
        var failed = 0;
        var pending = 0;

        foreach (var filePath in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
                     .Where(IsSupportedImage)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            discovered++;

            try
            {
                using var source = SKData.Create(filePath);
                using var image = SKImage.FromEncodedData(source)
                    ?? throw new InvalidDataException("无法解码图片。");
                using var bitmap = SKBitmap.FromImage(image);
                using var pixels = bitmap.PeekPixels();
                using var webp = pixels.Encode(new SKWebpEncoderOptions(
                    SKWebpEncoderCompression.Lossless,
                    quality: 100))
                    ?? throw new InvalidDataException("无法编码 WebP 图片。");

                database.Images.Add(new ImageDAO
                {
                    Name = Path.GetFileNameWithoutExtension(filePath),
                    Data = webp.ToArray(),
                    CreatedAt = DateTime.UtcNow
                });
                imported++;
                pending++;

                if (pending < BatchSize) continue;
                await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();
                pending = 0;
                logger.LogInformation("已导入 {Imported} 张图片。", imported);
            }
            catch (Exception exception) when (
                exception is InvalidDataException
                    or IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or ArgumentException)
            {
                failed++;
                logger.LogError(exception, "无法导入图片 {FilePath}。", filePath);
            }
        }

        if (pending > 0)
        {
            await database.SaveChangesAsync(cancellationToken);
            database.ChangeTracker.Clear();
        }

        return new ImageImportReport(discovered, imported, failed);
    }

    private static bool IsSupportedImage(string filePath) =>
        SupportedExtensions.Contains(Path.GetExtension(filePath));
}
