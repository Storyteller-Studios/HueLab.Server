using System.Text;
using HueLab.DataExporter;
using HueLab.Server.Models.DAO;
using HueLab.Server.Models.Enums;
using HueLab.Server.Services.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace HueLab.Server.Tests;

public sealed class DataExportServiceTests
{
    [Test]
    public async Task ExportsImageNamesAndColorsAsUtf8Csv()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"huelab-export-{Guid.NewGuid():N}.csv");
        try
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<HueLabDbContext>()
                .UseSqlite(connection)
                .Options;
            await using var database = new HueLabDbContext(options);
            await database.Database.EnsureCreatedAsync();

            var user = new UserDAO
            {
                Username = "exporter-test",
                PasswordHash = "unused",
                CreatedAt = DateTime.UtcNow
            };
            var image = new ImageDAO
            {
                Name = "色卡,\"暖色\"",
                Data = "RIFFxxxxWEBP"u8.ToArray(),
                Status = ImageStatus.Finished,
                CreatedAt = DateTime.UtcNow
            };
            database.AddRange(user, image);
            database.ImageColorResults.Add(new ImageColorResultDAO
            {
                Image = image,
                User = user,
                Color1 = "#AABBCC",
                Color2 = "#112233",
                Color3 = "#445566",
                Color4 = "#778899",
                CreatedAt = DateTime.UtcNow
            });
            await database.SaveChangesAsync();

            var exporter = new DataExportService(database);
            var report = await exporter.ExportCsvAsync(outputPath);

            if (report.Exported != 1 || report.OutputPath != Path.GetFullPath(outputPath))
            {
                throw new InvalidOperationException($"导出统计不正确：{report}");
            }

            var bytes = await File.ReadAllBytesAsync(outputPath);
            if (!bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
            {
                throw new InvalidOperationException("CSV 文件不是带 BOM 的 UTF-8 编码。");
            }

            var csv = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
            const string expected =
                "图片名称,颜色1,颜色2,颜色3,颜色4\r\n" +
                "\"色卡,\"\"暖色\"\"\",#AABBCC,#112233,#445566,#778899\r\n";
            if (csv != expected)
            {
                throw new InvalidOperationException($"CSV 内容不正确：{csv}");
            }
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
}
