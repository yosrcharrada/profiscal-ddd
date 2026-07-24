namespace Profiscal.Domain.Contracts.Requests;

public record RegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password
);
