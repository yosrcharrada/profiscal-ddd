namespace Profiscal.Contracts.Common;

public record ApiResponse<T>(bool Success, T? Data, string? Message = null, IEnumerable<string>? Errors = null)
{
    public static ApiResponse<T> Ok(T data) => new(true, data);
    public static ApiResponse<T> Fail(string message, IEnumerable<string>? errors = null)
        => new(false, default, message, errors);
}
