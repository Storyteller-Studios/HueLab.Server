using HueLab.Server.Services.Database;
using Microsoft.EntityFrameworkCore;

namespace HueLab.Server.Services;

public sealed class RefreshTokenCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<RefreshTokenCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(CleanupInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CleanupSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<HueLabDbContext>();
        var deleted = await database.RefreshTokens
            .Where(token => token.Revoked || token.ExpireAt <= DateTime.UtcNow)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation("已清理 {Deleted} 个过期或已撤销的 Refresh Token。", deleted);
        }

        return deleted;
    }

    private async Task CleanupSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CleanupAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "清理过期 Refresh Token 失败，将在下个周期重试。");
        }
    }
}
