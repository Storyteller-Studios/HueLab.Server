namespace HueLab.Server.Models.DTO.Responses;

public sealed record ImageTaskResponse(
    Guid ImageId,
    string ImageName,
    string Url,
    int ExpireSeconds,
    int MarkedImageCount,
    int TotalImageCount,
    int CurrentUserMarkedCount);

public sealed record SubmitColorResponse(bool Success);

public sealed record UserResultResponse(Guid ImageId, string ImageName, IReadOnlyList<string> Colors);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
}
