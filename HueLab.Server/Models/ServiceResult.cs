namespace HueLab.Server.Models;

public sealed record ServiceResult<T>(T? Value, string? Error, int StatusCode)
{
    public bool IsSuccess => Error is null;

    public static ServiceResult<T> Success(T value) => new(value, null, StatusCodes.Status200OK);
    public static ServiceResult<T> Failure(string error, int statusCode) => new(default, error, statusCode);
}
