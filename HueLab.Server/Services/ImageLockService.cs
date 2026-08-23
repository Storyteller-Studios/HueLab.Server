using HueLab.Server.Configurations;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace HueLab.Server.Services;

public sealed class ImageLockService(
    IConnectionMultiplexer connection,
    IOptions<RedisConfiguration> options) : IImageLockService
{
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        end
        return 0
        """;
    private readonly IDatabase cache = connection.GetDatabase();
    private readonly RedisConfiguration configuration = options.Value;

    public int LockSeconds => configuration.TaskLockSeconds;

    public Task<bool> TryAcquireAsync(Guid imageId, Guid userId) =>
        cache.StringSetAsync(
            CreateKey(imageId),
            userId.ToString(),
            TimeSpan.FromSeconds(configuration.TaskLockSeconds),
            When.NotExists);

    public async Task<bool> IsOwnedByAsync(Guid imageId, Guid userId)
    {
        var owner = await cache.StringGetAsync(CreateKey(imageId));
        return owner.HasValue && owner == userId.ToString();
    }

    public async Task ReleaseAsync(Guid imageId, Guid userId)
    {
        await cache.ScriptEvaluateAsync(
            ReleaseScript,
            [CreateKey(imageId)],
            [userId.ToString()]);
    }

    private static RedisKey CreateKey(Guid imageId) => $"image:lock:{imageId}";
}
