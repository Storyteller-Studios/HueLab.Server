using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HueLab.Server.Configurations;
using HueLab.Server.Models.DAO;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HueLab.Server.Services;

public sealed class JwtTokenService(IOptions<JwtConfiguration> options)
{
    private readonly JwtConfiguration configuration = options.Value;

    public (string Token, int ExpiresIn) CreateAccessToken(UserDAO user)
    {
        var expires = DateTime.UtcNow.AddMinutes(configuration.AccessTokenMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration.Key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            configuration.Issuer,
            configuration.Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username)
            ],
            expires: expires,
            signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), configuration.AccessTokenMinutes * 60);
    }

    public (string RawToken, string TokenHash, DateTime ExpireAt) CreateRefreshToken()
    {
        var rawToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        return (rawToken, HashRefreshToken(rawToken), DateTime.UtcNow.AddDays(configuration.RefreshTokenDays));
    }

    public static string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
