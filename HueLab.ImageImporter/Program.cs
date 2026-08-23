using HueLab.ImageImporter;
using HueLab.Server.Services.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length != 1)
{
    Console.Error.WriteLine("用法：HueLab.ImageImporter.exe <图片目录>");
    return 1;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddDbContext<HueLabDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default 未配置。")));
builder.Services.AddScoped<ImageImportService>();

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var database = scope.ServiceProvider.GetRequiredService<HueLabDbContext>();
await database.Database.MigrateAsync();

var importer = scope.ServiceProvider.GetRequiredService<ImageImportService>();
var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ImageImporter");
try
{
    var report = await importer.ImportDirectoryAsync(args[0]);
    logger.LogInformation(
        "导入完成：发现 {Discovered} 张，成功 {Imported} 张，失败 {Failed} 张。",
        report.Discovered,
        report.Imported,
        report.Failed);
    return report.Failed == 0 ? 0 : 2;
}
catch (DirectoryNotFoundException exception)
{
    logger.LogError("{Message}", exception.Message);
    return 1;
}
