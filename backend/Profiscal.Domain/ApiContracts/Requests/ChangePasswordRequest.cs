namespace Profiscal.Domain.Contracts.Requests;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
