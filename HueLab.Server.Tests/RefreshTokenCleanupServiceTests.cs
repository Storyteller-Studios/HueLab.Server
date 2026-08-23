using HueLab.Server.Models.DAO;
using HueLab.Server.Services;
using HueLab.Server.Services.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace HueLab.Server.Tests;

public sealed class RefreshTokenCleanupServiceTests
{
    [Test]
    public async Task CleanupDeletesExpiredAndRevokedTokensAndKeepsValidTokens()
    {
        await using var factory = new HueLabApplicationFactory();
        using var client = factory.CreateClient();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HueLabDbContext>();
            var user = new UserDAO
            {
                Username = $"cleanup-{Guid.NewGuid():N}",
                PasswordHash = "unused",
                CreatedAt = DateTime.UtcNow
            };
            database.Users.Add(user);
            database.RefreshTokens.AddRange(
                new RefreshTokenDAO
                {
                    User = user,
                    Token = "EXPIRED",
                    ExpireAt = DateTime.UtcNow.AddMinutes(-1),
                    CreatedAt = DateTime.UtcNow.AddDays(-1)
                },
                new RefreshTokenDAO
                {
                    User = user,
                    Token = "REVOKED",
                    ExpireAt = DateTime.UtcNow.AddDays(1),
                    Revoked = true,
                    CreatedAt = DateTime.UtcNow
                },
                new RefreshTokenDAO
                {
                    User = user,
                    Token = "VALID",
                    ExpireAt = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow
                });
            await database.SaveChangesAsync();
        }

        var cleanupService = factory.Services.GetRequiredService<RefreshTokenCleanupService>();
        await cleanupService.CleanupAsync();

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<HueLabDbContext>();
        var remainingTokens = await verificationDatabase.RefreshTokens
            .AsNoTracking()
            .Select(token => token.Token)
            .ToListAsync();
        if (!remainingTokens.SequenceEqual(["VALID"]))
        {
            throw new InvalidOperationException("清理服务删除了有效 Token，或保留了过期、已撤销 Token。");
        }
    }
}
