namespace HueLab.Server.Configurations;

public sealed class RedisConfiguration
{
    public const string SectionName = "Redis";

    public required string Connection { get; init; }
    public int TaskLockSeconds { get; init; } = 600;
}
