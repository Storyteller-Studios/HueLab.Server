namespace HueLab.Server.Models.DTO.Responses;

public sealed record ImageTaskResponse(Guid ImageId, string ImageName, string Url, int ExpireSeconds);

public sealed record SubmitColorResponse(bool Success);

public sealed record UserResultResponse(Guid ImageId, string ImageName, IReadOnlyList<string> Colors);
