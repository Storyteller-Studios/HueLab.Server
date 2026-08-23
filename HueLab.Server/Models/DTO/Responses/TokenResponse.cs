namespace HueLab.Server.Models.DTO.Responses;

public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);
