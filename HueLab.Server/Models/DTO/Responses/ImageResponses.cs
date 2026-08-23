namespace HueLab.Server.Models.DTO.Responses;

public sealed record ImageTaskResponse(Guid ImageId, string Url, int ExpireSeconds);

public sealed record SubmitColorResponse(bool Success);

public sealed record UserResultResponse(Guid ImageId, IReadOnlyList<string> Colors);
