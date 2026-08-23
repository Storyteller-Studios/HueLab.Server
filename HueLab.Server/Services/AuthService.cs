using HueLab.Server.Models;
using HueLab.Server.Models.DAO;
using HueLab.Server.Models.DTO.Requests;
using HueLab.Server.Models.DTO.Responses;
using HueLab.Server.Services.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HueLab.Server.Services;

public sealed class AuthService(
    HueLabDbContext database,
    JwtTokenService tokenService,
    IPasswordHasher<UserDAO> passwordHasher)
{
    public async Task<ServiceResult<TokenResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        if (username.Length < 3)
        {
            return ServiceResult<TokenResponse>.Failure("用户名至少需要 3 个非空白字符。", StatusCodes.Status400BadRequest);
        }

        if (await database.Users.AnyAsync(user => user.Username == username, cancellationToken))
        {
            return ServiceResult<TokenResponse>.Failure("用户名已被注册。", StatusCodes.Status409Conflict);
        }

        var user = new UserDAO
        {
            Username = username,
            PasswordHash = string.Empty,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        database.Users.Add(user);

        try
        {
            return ServiceResult<TokenResponse>.Success(await IssueTokenPairAsync(user, cancellationToken));
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_Users_Username"
            })
        {
            return ServiceResult<TokenResponse>.Failure("用户名已被注册。", StatusCodes.Status409Conflict);
        }
    }

    public async Task<ServiceResult<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await database.Users.SingleOrDefaultAsync(
            candidate => candidate.Username == request.Username,
            cancellationToken);
        if (user is null)
        {
            return ServiceResult<TokenResponse>.Failure("用户名或密码错误。", StatusCodes.Status401Unauthorized);
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return ServiceResult<TokenResponse>.Failure("用户名或密码错误。", StatusCodes.Status401Unauthorized);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        }

        return ServiceResult<TokenResponse>.Success(await IssueTokenPairAsync(user, cancellationToken));
    }

    public async Task<ServiceResult<TokenResponse>> RefreshAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var tokenHash = JwtTokenService.HashRefreshToken(request.RefreshToken);
        var storedToken = await database.RefreshTokens
            .AsNoTracking()
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.Token == tokenHash, cancellationToken);
        if (storedToken is null || storedToken.Revoked || storedToken.ExpireAt <= DateTime.UtcNow)
        {
            return ServiceResult<TokenResponse>.Failure("Refresh Token 无效或已过期。", StatusCodes.Status401Unauthorized);
        }

        var revoked = await database.RefreshTokens
            .Where(token => token.Id == storedToken.Id && !token.Revoked && token.ExpireAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.Revoked, true),
                cancellationToken);
        if (revoked == 0)
        {
            return ServiceResult<TokenResponse>.Failure("Refresh Token 已被使用。", StatusCodes.Status401Unauthorized);
        }

        var response = await IssueTokenPairAsync(storedToken.User, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<TokenResponse>.Success(response);
    }

    public async Task<ServiceResult<bool>> LogoutAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var tokenHash = JwtTokenService.HashRefreshToken(request.RefreshToken);
        var revoked = await database.RefreshTokens
            .Where(token => token.Token == tokenHash && !token.Revoked)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.Revoked, true),
                cancellationToken);
        return revoked == 0
            ? ServiceResult<bool>.Failure("Refresh Token 不存在或已撤销。", StatusCodes.Status404NotFound)
            : ServiceResult<bool>.Success(true);
    }

    private async Task<TokenResponse> IssueTokenPairAsync(UserDAO user, CancellationToken cancellationToken)
    {
        var accessToken = tokenService.CreateAccessToken(user);
        var refreshToken = tokenService.CreateRefreshToken();
        database.RefreshTokens.Add(new RefreshTokenDAO
        {
            UserId = user.Id,
            Token = refreshToken.TokenHash,
            ExpireAt = refreshToken.ExpireAt,
            CreatedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync(cancellationToken);
        return new TokenResponse(accessToken.Token, refreshToken.RawToken, accessToken.ExpiresIn);
    }
}
