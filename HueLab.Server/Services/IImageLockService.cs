namespace HueLab.Server.Services;

public interface IImageLockService
{
    int LockSeconds { get; }
    Task<Guid?> TryRenewAsync(Guid userId);
    Task<bool> TryAcquireAsync(Guid imageId, Guid userId);
    Task<bool> IsOwnedByAsync(Guid imageId, Guid userId);
    Task ReleaseAsync(Guid imageId, Guid userId);
}
