using HueLab.Server.Configurations;
using Microsoft.EntityFrameworkCore;

namespace HueLab.Server.Models.DAO;

[EntityTypeConfiguration(typeof(ImageColorResultDAOConfiguration))]
public sealed class ImageColorResultDAO
{
    public Guid Id { get; set; }
    public Guid ImageId { get; set; }
    public Guid UserId { get; set; }
    public required string Color1 { get; set; }
    public required string Color2 { get; set; }
    public required string Color3 { get; set; }
    public required string Color4 { get; set; }
    public DateTime CreatedAt { get; set; }

    public ImageDAO Image { get; set; } = null!;
    public UserDAO User { get; set; } = null!;
}
