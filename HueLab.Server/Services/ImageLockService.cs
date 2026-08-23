using HueLab.Server.Configurations;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace HueLab.Server.Services;

public sealed class ImageLockService(
    IConnectionMultiplexer connection,
    IOptions<RedisConfiguration> options) : IImageLockService
{
    private const string RenewScript = """
        if redis.call('get', KEYS[1]) == ARGV[1]
            and redis.call('get', KEYS[2]) == ARGV[2] then
            redis.call('expire', KEYS[1], ARGV[3])
            redis.call('expire', KEYS[2], ARGV[3])
            return 1
        end
        if redis.call('get', KEYS[1]) == ARGV[1] then
            redis.call('del', KEYS[1])
        end
        return 0
        """;
    private const string AcquireScript = """
        if redis.call('exists', KEYS[2]) == 1 then
            return 0
        end
        if redis.call('set', KEYS[1], ARGV[1], 'EX', ARGV[3], 'NX') then
            redis.call('set', KEYS[2], ARGV[2], 'EX', ARGV[3])
            return 1
        end
        return 0
        """;
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            redis.call('del', KEYS[1])
            if redis.call('get', KEYS[2]) == ARGV[2] then
                redis.call('del', KEYS[2])
            end
            return 1
        end
        return 0
        """;
    private readonly IDatabase cache = connection.GetDatabase();
    private readonly RedisConfiguration configuration = options.Value;

    public int LockSeconds => configuration.TaskLockSeconds;

    public async Task<Guid?> TryRenewAsync(Guid userId)
    {
        var taskKey = CreateUserTaskKey(userId);
        var imageIdValue = await cache.StringGetAsync(taskKey);
        if (!imageIdValue.HasValue || !Guid.TryParse(imageIdValue.ToString(), out var imageId))
        {
            return null;
        }

        var renewed = (int)await cache.ScriptEvaluateAsync(
            RenewScript,
            [taskKey, CreateImageLockKey(imageId)],
            [imageIdValue, userId.ToString(), configuration.TaskLockSeconds]);
        return renewed == 1 ? imageId : null;
    }

    public async Task<bool> TryAcquireAsync(Guid imageId, Guid userId) =>
        (int)await cache.ScriptEvaluateAsync(
            AcquireScript,
            [CreateImageLockKey(imageId), CreateUserTaskKey(userId)],
            [userId.ToString(), imageId.ToString(), configuration.TaskLockSeconds]) == 1;

    public async Task<bool> IsOwnedByAsync(Guid imageId, Guid userId)
    {
        var owner = await cache.StringGetAsync(CreateImageLockKey(imageId));
        return owner.HasValue && owner == userId.ToString();
    }

    public async Task ReleaseAsync(Guid imageId, Guid userId)
    {
        await cache.ScriptEvaluateAsync(
            ReleaseScript,
            [CreateImageLockKey(imageId), CreateUserTaskKey(userId)],
            [userId.ToString(), imageId.ToString()]);
    }

    private static RedisKey CreateImageLockKey(Guid imageId) => $"image:lock:{imageId}";
    private static RedisKey CreateUserTaskKey(Guid userId) => $"user:task:{userId}";
}
