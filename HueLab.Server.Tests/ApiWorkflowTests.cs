using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HueLab.Server.Models.DAO;
using HueLab.Server.Models.DTO.Requests;
using HueLab.Server.Models.DTO.Responses;
using HueLab.Server.Services.Database;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace HueLab.Server.Tests;

public sealed class ApiWorkflowTests
{
    [Test]
    public async Task AuthenticationAndImageAnnotationWorkflowCompletesEndToEnd()
    {
        await using var factory = new HueLabApplicationFactory();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var imageId = await SeedImageAsync(factory.Services);

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("annotator", "Password!123"));
        Ensure(registerResponse.StatusCode == HttpStatusCode.OK, "注册用户失败。");
        var registrationTokens = await registerResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Ensure(registrationTokens is not null, "注册响应缺少令牌。");

        var duplicateResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("annotator", "Password!123"));
        Ensure(duplicateResponse.StatusCode == HttpStatusCode.Conflict, "重复用户名注册应返回 409。");

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("annotator", "Password!123"));
        Ensure(loginResponse.IsSuccessStatusCode, await loginResponse.Content.ReadAsStringAsync());
        var tokens = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>()
            ?? throw new InvalidOperationException("登录响应缺少令牌。");
        Ensure(tokens.ExpiresIn == 900, "Access Token 有效期应为 900 秒。");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var taskResponse = await client.GetAsync("/api/images/task");
        Ensure(taskResponse.StatusCode == HttpStatusCode.OK, "领取任务失败。");
        var task = await taskResponse.Content.ReadFromJsonAsync<ImageTaskResponse>()
            ?? throw new InvalidOperationException("任务响应为空。");
        Ensure(task.ImageId == imageId, "领取了错误的图片。");
        Ensure(task.ExpireSeconds == 600, "任务锁有效期不正确。");

        var contentResponse = await client.GetAsync(new Uri(task.Url));
        Ensure(contentResponse.StatusCode == HttpStatusCode.OK, "读取图片失败。");
        Ensure(contentResponse.Content.Headers.ContentType?.MediaType == "image/webp", "图片必须以 WebP 返回。");

        var colors = new[] { "#ff0000", "#00FF00", "#0000ff", "#FFFFFF" };
        var submitResponse = await client.PostAsJsonAsync($"/api/images/{imageId}/colors", new SubmitColorRequest(colors));
        Ensure(submitResponse.StatusCode == HttpStatusCode.OK, "提交颜色失败。");
        var submission = await submitResponse.Content.ReadFromJsonAsync<SubmitColorResponse>();
        Ensure(submission?.Success == true, "颜色提交响应未成功。");

        var results = await client.GetFromJsonAsync<IReadOnlyList<UserResultResponse>>("/api/users/me/results")
            ?? throw new InvalidOperationException("个人提交记录响应为空。");
        Ensure(results.Count == 1, "个人提交记录数量不正确。");
        Ensure(results[0].ImageId == imageId, "个人提交记录对应了错误的图片。");
        Ensure(results[0].Colors.SequenceEqual(["#FF0000", "#00FF00", "#0000FF", "#FFFFFF"]), "颜色没有按统一格式保存。");

        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(tokens.RefreshToken));
        Ensure(refreshResponse.StatusCode == HttpStatusCode.OK, "刷新令牌失败。");
        var rotatedTokens = await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>()
            ?? throw new InvalidOperationException("刷新响应缺少令牌。");

        var reusedResponse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(tokens.RefreshToken));
        Ensure(reusedResponse.StatusCode == HttpStatusCode.Unauthorized, "旧 Refresh Token 不应能重复使用。");

        var logoutResponse = await client.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest(rotatedTokens.RefreshToken));
        Ensure(logoutResponse.StatusCode == HttpStatusCode.OK, "登出失败。");
        var revokedResponse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(rotatedTokens.RefreshToken));
        Ensure(revokedResponse.StatusCode == HttpStatusCode.Unauthorized, "已撤销 Refresh Token 不应继续使用。");
    }

    [Test]
    public void ImageEntityRejectsNonWebPData()
    {
        try
        {
            _ = new ImageDAO { Data = "not-webp"u8.ToArray() };
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("非 WebP 图片数据应被拒绝。");
    }

    private static async Task<Guid> SeedImageAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<HueLabDbContext>();
        var image = new ImageDAO
        {
            Data = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50],
            CreatedAt = DateTime.UtcNow
        };
        database.Images.Add(image);
        await database.SaveChangesAsync();
        return image.Id;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
