using HueLab.Server.Models.DAO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HueLab.Server.Configurations;

public sealed class RefreshTokenDAOConfiguration : IEntityTypeConfiguration<RefreshTokenDAO>
{
    public void Configure(EntityTypeBuilder<RefreshTokenDAO> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedOnAdd();
        builder.Property(token => token.Token).HasMaxLength(128).IsRequired();
        builder.HasIndex(token => token.Token).IsUnique();
        builder.Property(token => token.ExpireAt).HasColumnType("timestamp with time zone");
        builder.Property(token => token.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd();
        builder.HasOne(token => token.User)
            .WithMany(user => user.RefreshTokens)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
