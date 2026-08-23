using HueLab.Server.Models.DAO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HueLab.Server.Configurations;

public sealed class UserDAOConfiguration : IEntityTypeConfiguration<UserDAO>
{
    public void Configure(EntityTypeBuilder<UserDAO> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedOnAdd();
        builder.Property(user => user.Username).HasMaxLength(64).IsRequired();
        builder.HasIndex(user => user.Username).IsUnique();
        builder.Property(user => user.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(user => user.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd();
    }
}
