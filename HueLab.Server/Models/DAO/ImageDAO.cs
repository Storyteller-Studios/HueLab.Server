using HueLab.Server.Configurations;
using HueLab.Server.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace HueLab.Server.Models.DAO;

[EntityTypeConfiguration(typeof(ImageDAOConfiguration))]
public sealed class ImageDAO
{
    private byte[] data = null!;

    public Guid Id { get; set; }
    public required byte[] Data
    {
        get => data;
        set
        {
            if (!IsWebP(value)) throw new ArgumentException("图片必须是有效的 WebP 数据。", nameof(value));
            data = value;
        }
    }
    public ImageStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    public ImageColorResultDAO? ColorResult { get; set; }

    public static bool IsWebP(ReadOnlySpan<byte> value) =>
        value.Length >= 12
        && value[..4].SequenceEqual("RIFF"u8)
        && value.Slice(8, 4).SequenceEqual("WEBP"u8);
}
