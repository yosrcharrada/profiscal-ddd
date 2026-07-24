using System.Security.Claims;
using Profiscal.Domain.Entities;

namespace Profiscal.Domain.Abstractions;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(AppUser user, IEnumerable<string> roles);
    ClaimsPrincipal? ValidateExpiredToken(string token);
}
