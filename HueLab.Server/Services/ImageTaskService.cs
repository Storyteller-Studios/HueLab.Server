using System.Text.RegularExpressions;
using HueLab.Server.Models;
using HueLab.Server.Models.DAO;
using HueLab.Server.Models.DTO.Requests;
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
    public async Task<ServiceResult<(
        Guid ImageId,
        string ImageName,
        int ExpireSeconds,
        int MarkedImageCount,
        int TotalImageCount,
        int CurrentUserMarkedCount)>> AcquireTaskAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var renewedImageId = await lockService.TryRenewAsync(userId);
        if (renewedImageId is { } imageId)
        {
            var renewedImage = await database.Images
                .AsNoTracking()
                .Where(image => image.Id == imageId && image.Status == ImageStatus.Pending)
                .Select(image => new { image.Id, image.Name })
                .SingleOrDefaultAsync(cancellationToken);
            if (renewedImage is not null)
            {
                return await CreateTaskResultAsync(
                    renewedImage.Id,
                    renewedImage.Name,
                    userId,
                    cancellationToken);
            }

            await lockService.ReleaseAsync(imageId, userId);
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidates = await database.Images
                .AsNoTracking()
                .Where(image => image.Status == ImageStatus.Pending)
                .OrderBy(_ => EF.Functions.Random())
                .Select(image => new { image.Id, image.Name })
                .Take(50)
                .ToListAsync(cancellationToken);
            if (candidates.Count == 0)
            {
                return ServiceResult<(Guid, string, int, int, int, int)>.Failure(
                    "当前没有待标注图片。",
                    StatusCodes.Status404NotFound);
            }

            foreach (var image in candidates)
            {
                if (await lockService.TryAcquireAsync(image.Id, userId))
                {
                    return await CreateTaskResultAsync(
                        image.Id,
                        image.Name,
                        userId,
                        cancellationToken);
                }
            }
        }

        return ServiceResult<(Guid, string, int, int, int, int)>.Failure(
            "待标注图片均已被领取，请稍后重试。",
            StatusCodes.Status409Conflict);
    }

    private async Task<ServiceResult<(
        Guid ImageId,
        string ImageName,
        int ExpireSeconds,
        int MarkedImageCount,
        int TotalImageCount,
        int CurrentUserMarkedCount)>> CreateTaskResultAsync(
        Guid imageId,
        string imageName,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var statistics = await database.Images
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(images => new
            {
                MarkedImageCount = images.Count(image => image.Status == ImageStatus.Finished),
                TotalImageCount = images.Count(),
                CurrentUserMarkedCount = database.ImageColorResults.Count(result => result.UserId == userId)
            })
            .SingleAsync(cancellationToken);

        return ServiceResult<(Guid, string, int, int, int, int)>.Success((
            imageId,
            imageName,
            lockService.LockSeconds,
            statistics.MarkedImageCount,
            statistics.TotalImageCount,
            statistics.CurrentUserMarkedCount));
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

        var color1 = colors[0].ToUpperInvariant();
        var color2 = colors[1].ToUpperInvariant();
        var color3 = colors[2].ToUpperInvariant();
        var color4 = colors[3].ToUpperInvariant();
        var submittedAt = DateTime.UtcNow;
        var ownsLock = await lockService.IsOwnedByAsync(imageId, userId);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var replaced = await database.ImageColorResults
            .Where(result => result.ImageId == imageId && result.UserId == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(result => result.Color1, color1)
                    .SetProperty(result => result.Color2, color2)
                    .SetProperty(result => result.Color3, color3)
                    .SetProperty(result => result.Color4, color4)
                    .SetProperty(result => result.CreatedAt, submittedAt),
                cancellationToken);
        if (replaced == 0)
        {
            if (!ownsLock)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ServiceResult<SubmitColorResponse>.Failure(
                    "图片任务未由当前用户领取或已过期。",
                    StatusCodes.Status409Conflict);
            }

            var updated = await database.Images
                .Where(image => image.Id == imageId && image.Status == ImageStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(image => image.Status, ImageStatus.Finished),
                    cancellationToken);
            if (updated == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ServiceResult<SubmitColorResponse>.Failure(
                    "图片不存在或已完成。",
                    StatusCodes.Status409Conflict);
            }

            database.ImageColorResults.Add(new ImageColorResultDAO
            {
                ImageId = imageId,
                UserId = userId,
                Color1 = color1,
                Color2 = color2,
                Color3 = color3,
                Color4 = color4,
                CreatedAt = submittedAt
            });
            await database.SaveChangesAsync(cancellationToken);
        }

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

    public Task<PagedResponse<UserResultResponse>> GetUserResultsAsync(
        Guid userId,
        PaginationRequest pagination,
        CancellationToken cancellationToken) =>
        GetResultsAsync(userId, pagination, cancellationToken);

    public Task<PagedResponse<UserResultResponse>> GetAllResultsAsync(
        PaginationRequest pagination,
        CancellationToken cancellationToken) =>
        GetResultsAsync(null, pagination, cancellationToken);

    private async Task<PagedResponse<UserResultResponse>> GetResultsAsync(
        Guid? userId,
        PaginationRequest pagination,
        CancellationToken cancellationToken)
    {
        var query = database.ImageColorResults.AsNoTracking();
        if (userId is { } id)
        {
            query = query.Where(result => result.UserId == id);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var skip = (pagination.Page - 1L) * pagination.PageSize;
        IReadOnlyList<UserResultResponse> items = skip >= totalCount
            ? []
            : await query
                .OrderByDescending(result => result.CreatedAt)
                .ThenByDescending(result => result.Id)
                .Skip((int)skip)
                .Take(pagination.PageSize)
                .Select(result => new UserResultResponse(
                    result.ImageId,
                    result.Image.Name,
                    new[] { result.Color1, result.Color2, result.Color3, result.Color4 }))
                .ToListAsync(cancellationToken);

        return new PagedResponse<UserResultResponse>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalCount);
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();
}
