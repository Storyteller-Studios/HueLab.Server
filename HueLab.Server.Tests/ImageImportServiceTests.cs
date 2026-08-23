using HueLab.ImageImporter;
using HueLab.Server.Models.DAO;
using HueLab.Server.Services.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using TUnit.Core;

namespace HueLab.Server.Tests;

public sealed class ImageImportServiceTests
{
    [Test]
    public async Task ImportsNestedImagesAsWebPAndIgnoresOtherFiles()
    {
        var sourceDirectory = Path.Combine(Path.GetTempPath(), $"huelab-import-{Guid.NewGuid():N}");
        var nestedDirectory = Directory.CreateDirectory(Path.Combine(sourceDirectory, "nested")).FullName;
        try
        {
            using (var bitmap = new SKBitmap(2, 2))
            {
                bitmap.Erase(SKColors.Red);
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, quality: 100);
                await File.WriteAllBytesAsync(Path.Combine(nestedDirectory, "sample.png"), png.ToArray());
            }
            await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "ignored.txt"), "not an image");

            await using var factory = new HueLabApplicationFactory();
            using var client = factory.CreateClient();
            await using var scope = factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<HueLabDbContext>();
            var importer = new ImageImportService(database, NullLogger<ImageImportService>.Instance);

            var report = await importer.ImportDirectoryAsync(sourceDirectory);
            if (report != new ImageImportReport(1, 1, 0))
            {
                throw new InvalidOperationException($"导入统计不正确：{report}");
            }

            var storedImage = await database.Images.AsNoTracking().SingleAsync();
            if (!ImageDAO.IsWebP(storedImage.Data))
            {
                throw new InvalidOperationException("数据库中的图片不是 WebP 格式。");
            }
            if (storedImage.Name != "sample")
            {
                throw new InvalidOperationException($"数据库中的图片名不正确：{storedImage.Name}");
            }
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
        }
    }
}
