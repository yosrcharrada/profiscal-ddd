namespace Profiscal.Application.Exceptions;

public class ValidationException(IEnumerable<string> errors)
    : Exception("One or more validation errors occurred.")
{
    public IEnumerable<string> Errors { get; } = errors;
}
