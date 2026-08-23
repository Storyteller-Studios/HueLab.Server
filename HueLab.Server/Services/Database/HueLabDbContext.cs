using HueLab.Server.Models.DAO;
using Microsoft.EntityFrameworkCore;

namespace HueLab.Server.Services.Database;

public sealed class HueLabDbContext(DbContextOptions<HueLabDbContext> options) : DbContext(options)
{
    public DbSet<UserDAO> Users { get; set; }
    public DbSet<RefreshTokenDAO> RefreshTokens { get; set; }
    public DbSet<ImageDAO> Images { get; set; }
    public DbSet<ImageColorResultDAO> ImageColorResults { get; set; }
}
