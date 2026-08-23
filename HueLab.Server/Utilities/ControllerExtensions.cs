using System.IdentityModel.Tokens.Jwt;
using HueLab.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace HueLab.Server.Utilities;

public static class ControllerExtensions
{
    public static ActionResult<T> ToActionResult<T>(this ControllerBase controller, ServiceResult<T> result) =>
        result.IsSuccess
            ? result.Value!
            : controller.Problem(statusCode: result.StatusCode, detail: result.Error);

    public static bool TryGetUserId(this ControllerBase controller, out Guid userId)
    {
        var subject = controller.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(subject, out userId);
    }
}
