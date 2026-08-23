using System.ComponentModel.DataAnnotations;

namespace HueLab.Server.Models.DTO.Requests;

public sealed class PaginationRequest
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
