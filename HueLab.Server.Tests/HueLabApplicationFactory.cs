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
        private readonly Lock gate = new();
        private readonly Dictionary<Guid, Guid> owners = [];
        private readonly Dictionary<Guid, Guid> userTasks = [];
        public int LockSeconds => 600;

        public Task<Guid?> TryRenewAsync(Guid userId)
        {
            lock (gate)
            {
                return Task.FromResult<Guid?>(
                    userTasks.TryGetValue(userId, out var imageId)
                    && owners.TryGetValue(imageId, out var owner)
                    && owner == userId
                        ? imageId
                        : null);
            }
        }

        public Task<bool> TryAcquireAsync(Guid imageId, Guid userId)
        {
            lock (gate)
            {
                if (userTasks.ContainsKey(userId) || owners.ContainsKey(imageId))
                {
                    return Task.FromResult(false);
                }

                owners.Add(imageId, userId);
                userTasks.Add(userId, imageId);
                return Task.FromResult(true);
            }
        }

        public Task<bool> IsOwnedByAsync(Guid imageId, Guid userId)
        {
            lock (gate)
            {
                return Task.FromResult(owners.TryGetValue(imageId, out var owner) && owner == userId);
            }
        }

        public Task ReleaseAsync(Guid imageId, Guid userId)
        {
            lock (gate)
            {
                if (owners.TryGetValue(imageId, out var owner) && owner == userId)
                {
                    owners.Remove(imageId);
                    userTasks.Remove(userId);
                }
            }

            return Task.CompletedTask;
        }
    }
}
