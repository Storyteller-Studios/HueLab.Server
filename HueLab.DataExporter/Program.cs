using System.Text;
using HueLab.DataExporter;
using HueLab.Server.Services.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length != 1)
{
    Console.Error.WriteLine("用法：dotnet run --project HueLab.DataExporter -- <输出 CSV 文件>");
    return 1;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddDbContext<HueLabDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default 未配置。")));
builder.Services.AddScoped<DataExportService>();

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var database = scope.ServiceProvider.GetRequiredService<HueLabDbContext>();
await database.Database.MigrateAsync();

var exporter = scope.ServiceProvider.GetRequiredService<DataExportService>();
var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DataExporter");
try
{
    var report = await exporter.ExportCsvAsync(args[0]);
    logger.LogInformation(
        "导出完成：共 {Exported} 条取色结果，文件位置 {OutputPath}。",
        report.Exported,
        report.OutputPath);
    return 0;
}
catch (Exception exception) when (
    exception is IOException
        or UnauthorizedAccessException
        or NotSupportedException
        or ArgumentException)
{
    logger.LogError(exception, "无法导出 CSV 文件：{Message}", exception.Message);
    return 1;
}
