using System.Text;
using HueLab.Server.Configurations;
using HueLab.Server.Models.DAO;
using HueLab.Server.Services;
using HueLab.Server.Services.Database;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using StackExchange.Redis;

namespace HueLab.Server;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers();
        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();

        builder.Services.AddOptions<JwtConfiguration>()
            .Bind(builder.Configuration.GetRequiredSection(JwtConfiguration.SectionName))
            .Validate(configuration => !string.IsNullOrWhiteSpace(configuration.Issuer), "Jwt:Issuer 未配置")
            .Validate(configuration => !string.IsNullOrWhiteSpace(configuration.Audience), "Jwt:Audience 未配置")
            .Validate(configuration => Encoding.UTF8.GetByteCount(configuration.Key) >= 32, "Jwt:Key 至少需要 32 字节")
            .Validate(configuration => configuration.AccessTokenMinutes > 0, "Jwt:AccessTokenMinutes 必须大于 0")
            .Validate(configuration => configuration.RefreshTokenDays is >= 7 and <= 30, "Jwt:RefreshTokenDays 必须介于 7 和 30 之间")
            .ValidateOnStart();
        builder.Services.AddOptions<RedisConfiguration>()
            .Bind(builder.Configuration.GetRequiredSection(RedisConfiguration.SectionName))
            .Validate(configuration => !string.IsNullOrWhiteSpace(configuration.Connection), "Redis:Connection 未配置")
            .Validate(configuration => configuration.TaskLockSeconds > 0, "Redis:TaskLockSeconds 必须大于 0")
            .ValidateOnStart();

        var jwt = builder.Configuration.GetRequiredSection(JwtConfiguration.SectionName).Get<JwtConfiguration>()
            ?? throw new InvalidOperationException("Jwt 配置无效。");
        var servers = builder.Configuration.GetSection("Servers").Get<string[]>()
            ?? throw new InvalidOperationException("Servers 未配置。");
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        builder.Services.AddAuthorization();

        builder.Services.AddDbContext<HueLabDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default 未配置。")));
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var redis = builder.Configuration.GetRequiredSection(RedisConfiguration.SectionName).Get<RedisConfiguration>()
                ?? throw new InvalidOperationException("Redis 配置无效。");
            var connection = ConfigurationOptions.Parse(redis.Connection);
            connection.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(connection);
        });
        builder.Services.AddScoped<IPasswordHasher<UserDAO>, PasswordHasher<UserDAO>>();
        builder.Services.AddScoped<JwtTokenService>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<IImageLockService, ImageLockService>();
        builder.Services.AddScoped<ImageTaskService>();

        var app = builder.Build();

        app.UseExceptionHandler();
        app.MapOpenApi().AllowAnonymous();
        app.MapScalarApiReference(options =>
        {
            foreach (var server in servers)
            {
                options.AddServer(server);
            }
        }).AllowAnonymous();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<HueLabDbContext>();
            await database.Database.MigrateAsync();
        }

        await app.RunAsync();
    }
}
