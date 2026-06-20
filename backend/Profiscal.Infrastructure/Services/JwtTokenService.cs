using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Profiscal.Application.Common.Interfaces;
using Profiscal.Domain.Entities;

namespace Profiscal.Infrastructure.Services;

public class JwtTokenService(IConfiguration config) : IJwtTokenService
{
    private readonly string _key = Environment.GetEnvironmentVariable("JWT__KEY")
        ?? (config["Jwt:Key"] is { Length: > 0 } k ? k
            : throw new InvalidOperationException(
                "JWT signing key is not set. Use: " +
                "(1) dotnet user-secrets set \"Jwt:Key\" \"<32+ chars>\", " +
                "(2) env var JWT__KEY, or " +
                "(3) Jwt:Key in appsettings.Development.json (dev only)."));
    private readonly string _issuer   = config["Jwt:Issuer"]!;
    private readonly string _audience = config["Jwt:Audience"]!;
    private readonly int _accessMinutes = int.TryParse(config["Jwt:AccessTokenMinutes"], out var m) ? m : 15;

    public (string Token, DateTime ExpiresAt) GenerateAccessToken(AppUser user, IEnumerable<string> roles)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(_accessMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,        user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email,      user.Email!),
            new(JwtRegisteredClaimNames.GivenName,  user.FirstName),
            new(JwtRegisteredClaimNames.FamilyName, user.LastName),
            new(JwtRegisteredClaimNames.Jti,        Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(
            issuer:   _issuer,
            audience: _audience,
            claims:   claims,
            expires:  expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public ClaimsPrincipal? ValidateExpiredToken(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime         = false, // allow expired — refresh flow
            ValidIssuer              = _issuer,
            ValidAudience            = _audience,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key))
        };

        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);
        }
        catch
        {
            return null;
        }
    }
}
