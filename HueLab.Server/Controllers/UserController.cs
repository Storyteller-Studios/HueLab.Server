using HueLab.Server.Models.DTO.Requests;
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
    [EndpointDescription("分页查询当前用户提交的颜色结果")]
    public async Task<ActionResult<PagedResponse<UserResultResponse>>> GetMyResultsAsync(
        [FromQuery] PaginationRequest pagination,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await imageTaskService.GetUserResultsAsync(userId, pagination, cancellationToken));
    }

    [HttpGet("results")]
    [EndpointDescription("分页查询全部用户提交的颜色结果")]
    public async Task<ActionResult<PagedResponse<UserResultResponse>>> GetAllResultsAsync(
        [FromQuery] PaginationRequest pagination,
        CancellationToken cancellationToken) =>
        Ok(await imageTaskService.GetAllResultsAsync(pagination, cancellationToken));
}
