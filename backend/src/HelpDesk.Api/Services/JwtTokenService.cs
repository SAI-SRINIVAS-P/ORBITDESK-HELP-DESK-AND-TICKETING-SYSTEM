using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
namespace HelpDesk.Api.Services;
public sealed class JwtTokenService(IConfiguration configuration, UserManager<ApplicationUser> userManager, SymmetricSecurityKey jwtSigningKey) {
    public async Task<AuthResponse> CreateAsync(ApplicationUser user) {
        var expires = DateTimeOffset.UtcNow.AddMinutes(configuration.GetValue("Jwt:ExpiresMinutes", 90));
        var roles = await userManager.GetRolesAsync(user);
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, user.Id), new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Name, user.DisplayName), new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty), new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var token = new JwtSecurityToken(configuration["Jwt:Issuer"], configuration["Jwt:Audience"], claims, expires: expires.UtcDateTime, signingCredentials: new SigningCredentials(jwtSigningKey, SecurityAlgorithms.HmacSha256));
        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, user.Id, user.DisplayName, user.Email ?? string.Empty, roles.ToArray());
    }
}
