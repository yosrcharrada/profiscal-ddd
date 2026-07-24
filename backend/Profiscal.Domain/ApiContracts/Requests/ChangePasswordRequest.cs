namespace Profiscal.Contracts.Requests;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
