using HueLab.Server.Models.DTO.Requests;
using HueLab.Server.Models.DTO.Responses;
using HueLab.Server.Services;
using HueLab.Server.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HueLab.Server.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [EndpointDescription("注册新用户并签发令牌对")]
    public async Task<ActionResult<TokenResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await authService.RegisterAsync(request, cancellationToken));

    [HttpPost("login")]
    [EndpointDescription("使用用户名和密码登录")]
    public async Task<ActionResult<TokenResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await authService.LoginAsync(request, cancellationToken));

    [HttpPost("refresh")]
    [EndpointDescription("轮换 Refresh Token 并签发新的令牌对")]
    public async Task<ActionResult<TokenResponse>> RefreshAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await authService.RefreshAsync(request, cancellationToken));

    [HttpPost("logout")]
    [EndpointDescription("撤销 Refresh Token")]
    public async Task<ActionResult<bool>> LogoutAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken) =>
        this.ToActionResult(await authService.LogoutAsync(request, cancellationToken));
}
