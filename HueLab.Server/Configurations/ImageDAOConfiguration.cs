using HueLab.Server.Models.DAO;
using HueLab.Server.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HueLab.Server.Configurations;

public sealed class ImageDAOConfiguration : IEntityTypeConfiguration<ImageDAO>
{
    public void Configure(EntityTypeBuilder<ImageDAO> builder)
    {
        builder.ToTable("Images");
        builder.HasKey(image => image.Id);
        builder.Property(image => image.Id).ValueGeneratedOnAdd();
        builder.Property(image => image.Name).HasMaxLength(255).IsRequired();
        builder.Property(image => image.Data).HasColumnType("bytea").IsRequired();
        builder.Property(image => image.Status)
            .HasConversion<int>()
            .HasDefaultValue(ImageStatus.Pending);
        builder.HasIndex(image => image.Status);
        builder.Property(image => image.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd();
    }
}
