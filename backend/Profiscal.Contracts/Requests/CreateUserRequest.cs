namespace Profiscal.Contracts.Requests;

/// <summary>Admin provisioning: creates the account with a generated password emailed to the user.</summary>
public record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string Role,
    Guid? ManagerId
);

public record AssignManagerRequest(Guid? ManagerId);
