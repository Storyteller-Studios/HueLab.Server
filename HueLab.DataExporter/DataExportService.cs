using System.Text;
using HueLab.Server.Services.Database;
using Microsoft.EntityFrameworkCore;

namespace HueLab.DataExporter;

public sealed record DataExportReport(int Exported, string OutputPath);

public sealed class DataExportService(HueLabDbContext database)
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public async Task<DataExportReport> ExportCsvAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var exported = 0;
        await using var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream, Utf8WithBom)
        {
            NewLine = "\r\n"
        };

        await writer.WriteLineAsync("图片名称,用户名,颜色1,颜色2,颜色3,颜色4".AsMemory(), cancellationToken);

        var results = database.ImageColorResults
            .AsNoTracking()
            .OrderBy(result => result.Image.Name)
            .ThenBy(result => result.ImageId)
            .Select(result => new
            {
                ImageName = result.Image.Name,
                UserName = result.User.Username,
                result.Color1,
                result.Color2,
                result.Color3,
                result.Color4
            })
            .AsAsyncEnumerable();

        await foreach (var result in results.WithCancellation(cancellationToken))
        {
            var row = string.Join(',',
                Escape(result.ImageName),
                Escape(result.UserName),
                Escape(result.Color1),
                Escape(result.Color2),
                Escape(result.Color3),
                Escape(result.Color4));
            await writer.WriteLineAsync(row.AsMemory(), cancellationToken);
            exported++;
        }

        await writer.FlushAsync(cancellationToken);
        return new DataExportReport(exported, fullPath);
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
