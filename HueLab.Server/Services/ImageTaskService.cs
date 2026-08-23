using System.Text.RegularExpressions;
using HueLab.Server.Models;
using HueLab.Server.Models.DAO;
using HueLab.Server.Models.DTO.Responses;
using HueLab.Server.Models.Enums;
using HueLab.Server.Services.Database;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace HueLab.Server.Services;

public sealed partial class ImageTaskService(
    HueLabDbContext database,
    IImageLockService lockService,
    ILogger<ImageTaskService> logger)
{
    public async Task<ServiceResult<(Guid ImageId, int ExpireSeconds)>> AcquireTaskAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidates = await database.Images
                .AsNoTracking()
                .Where(image => image.Status == ImageStatus.Pending)
                .OrderBy(_ => EF.Functions.Random())
                .Select(image => image.Id)
                .Take(50)
                .ToListAsync(cancellationToken);
            if (candidates.Count == 0)
            {
                return ServiceResult<(Guid, int)>.Failure("当前没有待标注图片。", StatusCodes.Status404NotFound);
            }

            foreach (var imageId in candidates)
            {
                if (await lockService.TryAcquireAsync(imageId, userId))
                {
                    return ServiceResult<(Guid, int)>.Success((imageId, lockService.LockSeconds));
                }
            }
        }

        return ServiceResult<(Guid, int)>.Failure("待标注图片均已被领取，请稍后重试。", StatusCodes.Status409Conflict);
    }

    public async Task<ServiceResult<byte[]>> GetImageDataAsync(
        Guid imageId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await lockService.IsOwnedByAsync(imageId, userId))
        {
            return ServiceResult<byte[]>.Failure("图片任务未由当前用户领取或已过期。", StatusCodes.Status403Forbidden);
        }

        var data = await database.Images
            .AsNoTracking()
            .Where(image => image.Id == imageId && image.Status == ImageStatus.Pending)
            .Select(image => image.Data)
            .SingleOrDefaultAsync(cancellationToken);
        return data is null
            ? ServiceResult<byte[]>.Failure("图片不存在或已完成。", StatusCodes.Status404NotFound)
            : ServiceResult<byte[]>.Success(data);
    }

    public async Task<ServiceResult<SubmitColorResponse>> SubmitColorsAsync(
        Guid imageId,
        Guid userId,
        IReadOnlyList<string> colors,
        CancellationToken cancellationToken)
    {
        if (colors.Count != 4 || colors.Any(color => !HexColorRegex().IsMatch(color)))
        {
            return ServiceResult<SubmitColorResponse>.Failure(
                "必须提交 4 个 #RRGGBB 格式的颜色。",
                StatusCodes.Status400BadRequest);
        }

        if (!await lockService.IsOwnedByAsync(imageId, userId))
        {
            return ServiceResult<SubmitColorResponse>.Failure(
                "图片任务未由当前用户领取或已过期。",
                StatusCodes.Status409Conflict);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var updated = await database.Images
            .Where(image => image.Id == imageId && image.Status == ImageStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(image => image.Status, ImageStatus.Finished),
                cancellationToken);
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ServiceResult<SubmitColorResponse>.Failure("图片不存在或已完成。", StatusCodes.Status409Conflict);
        }

        database.ImageColorResults.Add(new ImageColorResultDAO
        {
            ImageId = imageId,
            UserId = userId,
            Color1 = colors[0].ToUpperInvariant(),
            Color2 = colors[1].ToUpperInvariant(),
            Color3 = colors[2].ToUpperInvariant(),
            Color4 = colors[3].ToUpperInvariant(),
            CreatedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        try
        {
            await lockService.ReleaseAsync(imageId, userId);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "已保存图片 {ImageId} 的结果，但未能删除任务锁；锁将按 TTL 自动过期。", imageId);
        }

        return ServiceResult<SubmitColorResponse>.Success(new SubmitColorResponse(true));
    }

    public async Task<IReadOnlyList<UserResultResponse>> GetUserResultsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await database.ImageColorResults
            .AsNoTracking()
            .Where(result => result.UserId == userId)
            .OrderByDescending(result => result.CreatedAt)
            .Select(result => new UserResultResponse(
                result.ImageId,
                new[] { result.Color1, result.Color2, result.Color3, result.Color4 }))
            .ToListAsync(cancellationToken);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();
}
