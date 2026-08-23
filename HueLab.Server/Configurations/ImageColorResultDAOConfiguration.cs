using HueLab.Server.Models.DAO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HueLab.Server.Configurations;

public sealed class ImageColorResultDAOConfiguration : IEntityTypeConfiguration<ImageColorResultDAO>
{
    public void Configure(EntityTypeBuilder<ImageColorResultDAO> builder)
    {
        builder.ToTable("ImageColorResults");
        builder.HasKey(result => result.Id);
        builder.Property(result => result.Id).ValueGeneratedOnAdd();
        builder.HasIndex(result => result.ImageId).IsUnique();
        builder.HasIndex(result => result.UserId);
        builder.Property(result => result.Color1).HasMaxLength(16).IsRequired();
        builder.Property(result => result.Color2).HasMaxLength(16).IsRequired();
        builder.Property(result => result.Color3).HasMaxLength(16).IsRequired();
        builder.Property(result => result.Color4).HasMaxLength(16).IsRequired();
        builder.Property(result => result.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd();
        builder.HasOne(result => result.Image)
            .WithOne(image => image.ColorResult)
            .HasForeignKey<ImageColorResultDAO>(result => result.ImageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(result => result.User)
            .WithMany(user => user.ColorResults)
            .HasForeignKey(result => result.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
