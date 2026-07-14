using Microsoft.AspNetCore.Identity;

namespace Profiscal.Domain.Entities;

public class AppUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Every consultant is attached to a manager who supervises their work.</summary>
    public Guid? ManagerId { get; set; }
    public AppUser? Manager { get; set; }
    public ICollection<AppUser> Consultants { get; set; } = [];

    /// <summary>True for admin-provisioned accounts until the generated password is replaced.</summary>
    public bool MustChangePassword { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
