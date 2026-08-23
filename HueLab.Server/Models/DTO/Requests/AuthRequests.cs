using System.ComponentModel.DataAnnotations;

namespace HueLab.Server.Models.DTO.Requests;

public sealed record RegisterRequest(
    [Required, StringLength(64, MinimumLength = 3)] string Username,
    [Required, StringLength(128, MinimumLength = 8)] string Password);

public sealed record LoginRequest(
    [Required, StringLength(64)] string Username,
    [Required] string Password);

public sealed record RefreshTokenRequest(
    [Required] string RefreshToken);
