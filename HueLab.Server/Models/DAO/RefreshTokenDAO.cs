using HueLab.Server.Configurations;
using Microsoft.EntityFrameworkCore;

namespace HueLab.Server.Models.DAO;

[EntityTypeConfiguration(typeof(RefreshTokenDAOConfiguration))]
public sealed class RefreshTokenDAO
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Token { get; set; }
    public DateTime ExpireAt { get; set; }
    public bool Revoked { get; set; }
    public DateTime CreatedAt { get; set; }

    public UserDAO User { get; set; } = null!;
}
