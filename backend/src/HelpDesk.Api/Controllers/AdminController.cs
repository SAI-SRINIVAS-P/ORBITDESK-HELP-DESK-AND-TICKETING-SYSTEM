using System.Security.Claims;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace HelpDesk.Api.Controllers;
[ApiController, Authorize(Roles = "Admin"), Route("api/admin")]
public sealed class AdminController(UserManager<ApplicationUser> userManager) : ControllerBase {
    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken token) {
        var users = await userManager.Users.AsNoTracking().OrderBy(x => x.CreatedAtUtc).ToListAsync(token);
        var response = new List<UserAdminDto>();
        foreach (var user in users) response.Add(new UserAdminDto(user.Id, user.DisplayName, user.Email ?? string.Empty, (await userManager.GetRolesAsync(user)).ToList(), user.CreatedAtUtc));
        return Ok(response);
    }
    [HttpPut("users/{id}/role")]
    public async Task<IActionResult> UpdateRole(string id, UpdateUserRoleRequest request) {
        var allowed = new[] { "User", "Agent", "Admin" };
        if (!allowed.Contains(request.Role, StringComparer.Ordinal)) return BadRequest(new { message = "Role must be User, Agent, or Admin." });
        var user = await userManager.FindByIdAsync(id); if (user is null) return NotFound();
        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Contains("Admin") && request.Role != "Admin") {
            var administrators = await userManager.GetUsersInRoleAsync("Admin");
            if (administrators.Count <= 1) return Conflict(new { message = "The workspace must keep at least one administrator." });
        }
        if (id == User.FindFirstValue(ClaimTypes.NameIdentifier) && request.Role != "Admin")
            return Conflict(new { message = "You cannot remove your own administrator access from this screen." });
        var remove = await userManager.RemoveFromRolesAsync(user, currentRoles);
        if (!remove.Succeeded) return BadRequest(new { message = string.Join("; ", remove.Errors.Select(x => x.Description)) });
        var add = await userManager.AddToRoleAsync(user, request.Role);
        if (!add.Succeeded) return BadRequest(new { message = string.Join("; ", add.Errors.Select(x => x.Description)) });
        return Ok(new UserAdminDto(user.Id, user.DisplayName, user.Email ?? string.Empty, (await userManager.GetRolesAsync(user)).ToList(), user.CreatedAtUtc));
    }
}
