using HueLab.Server.Configurations;
using Microsoft.EntityFrameworkCore;

namespace HueLab.Server.Models.DAO;

[EntityTypeConfiguration(typeof(UserDAOConfiguration))]
public sealed class UserDAO
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<RefreshTokenDAO> RefreshTokens { get; set; } = [];
    public List<ImageColorResultDAO> ColorResults { get; set; } = [];
}
