using System.Collections.Concurrent;
using HueLab.Server.Services;
using HueLab.Server.Services.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HueLab.Server.Tests;

public sealed class HueLabApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<HueLabDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<HueLabDbContext>>();
            services.AddDbContext<HueLabDbContext>(options => options.UseSqlite(connection));
            services.RemoveAll<IImageLockService>();
            services.AddSingleton<IImageLockService, InMemoryImageLockService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }

    private sealed class InMemoryImageLockService : IImageLockService
    {
        private readonly ConcurrentDictionary<Guid, Guid> owners = new();
        public int LockSeconds => 600;

        public Task<bool> TryAcquireAsync(Guid imageId, Guid userId) =>
            Task.FromResult(owners.TryAdd(imageId, userId));

        public Task<bool> IsOwnedByAsync(Guid imageId, Guid userId) =>
            Task.FromResult(owners.TryGetValue(imageId, out var owner) && owner == userId);

        public Task ReleaseAsync(Guid imageId, Guid userId)
        {
            owners.TryRemove(new KeyValuePair<Guid, Guid>(imageId, userId));
            return Task.CompletedTask;
        }
    }
}
