using HueLab.Server.Models.DTO.Requests;
using HueLab.Server.Models.DTO.Responses;
using HueLab.Server.Services;
using HueLab.Server.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HueLab.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/images")]
public sealed class ImageController(ImageTaskService imageTaskService) : ControllerBase
{
    [HttpGet("task")]
    [EndpointDescription("随机领取一张待标注图片，任务锁有效期默认为 10 分钟")]
    public async Task<ActionResult<ImageTaskResponse>> GetTaskAsync(CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId)) return Unauthorized();
        var result = await imageTaskService.AcquireTaskAsync(userId, cancellationToken);
        if (!result.IsSuccess) return Problem(statusCode: result.StatusCode, detail: result.Error);

        var task = result.Value;
        var url = Url.Link("GetImageContent", new { imageId = task.ImageId })
            ?? throw new InvalidOperationException("无法生成图片内容 URL。");
        return new ImageTaskResponse(task.ImageId, task.ImageName, url, task.ExpireSeconds);
    }

    [HttpGet("{imageId:guid}/content", Name = "GetImageContent")]
    [EndpointDescription("获取当前用户已领取的图片二进制内容")]
    public async Task<IActionResult> GetContentAsync(Guid imageId, CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId)) return Unauthorized();
        var result = await imageTaskService.GetImageDataAsync(imageId, userId, cancellationToken);
        if (!result.IsSuccess) return Problem(statusCode: result.StatusCode, detail: result.Error);
        return File(result.Value!, "image/webp");
    }

    [HttpPost("{imageId:guid}/colors")]
    [EndpointDescription("提交图片对应的四个偏好颜色")]
    public async Task<ActionResult<SubmitColorResponse>> SubmitColorsAsync(
        Guid imageId,
        SubmitColorRequest request,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId)) return Unauthorized();
        return this.ToActionResult(await imageTaskService.SubmitColorsAsync(
            imageId,
            userId,
            request.Colors,
            cancellationToken));
    }

}
