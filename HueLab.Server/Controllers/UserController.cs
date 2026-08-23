using HueLab.Server.Models.DTO.Responses;
using HueLab.Server.Services;
using HueLab.Server.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HueLab.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UserController(ImageTaskService imageTaskService) : ControllerBase
{
    [HttpGet("me/results")]
    [EndpointDescription("查询当前用户提交的颜色结果")]
    public async Task<ActionResult<IReadOnlyList<UserResultResponse>>> GetMyResultsAsync(
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await imageTaskService.GetUserResultsAsync(userId, cancellationToken));
    }
}
