using System.Security.Claims;
using FluentValidation;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using HelpDesk.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    JwtTokenService tokenService,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    HelpDeskDbContext db) : ValidationControllerBase
{
    [AllowAnonymous, HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken token)
    {
        var validation = await ValidateRequestAsync(request, registerValidator, token);
        if (validation is not null) return validation;

        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            return Conflict(new { message = "An account with this email already exists." });

        // Registration always creates a regular User. There is deliberately no role field.
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            EmailConfirmed = true
        };

        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            return BadRequest(new { errors = created.Errors.Select(x => new { field = x.Code, message = x.Description }) });

        var roleResult = await userManager.AddToRoleAsync(user, "User");
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return StatusCode(500, new { message = "Could not initialize user permissions." });
        }

        db.EmailOutbox.Add(new EmailOutboxMessage
        {
            ToEmail = email,
            Subject = "Welcome to OrbitDesk",
            Body = $"Hi {user.DisplayName},\n\nYour OrbitDesk user account has been created successfully. You can now sign in, raise support tickets, and follow their progress.\n\nIf you did not create this account, please contact your support administrator.\n\nOrbitDesk Support"
        });
        await db.SaveChangesAsync(token);

        return Ok(await tokenService.CreateAsync(user));
    }

    [AllowAnonymous, HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken token)
    {
        var validation = await ValidateRequestAsync(request, loginValidator, token);
        if (validation is not null) return validation;

        var requestedMode = string.IsNullOrWhiteSpace(request.LoginAs) ? "User" : request.LoginAs.Trim();
        if (requestedMode is not ("User" or "Admin"))
            return BadRequest(new { message = "Choose either User or Admin sign-in." });

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return Unauthorized(new { message = "Email or password is incorrect." });

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized(new { message = result.IsLockedOut ? "Account temporarily locked. Try again later." : "Email or password is incorrect." });

        var roles = await userManager.GetRolesAsync(user);
        var isAdmin = roles.Contains("Admin", StringComparer.Ordinal);
        if ((requestedMode == "Admin") != isAdmin)
            return Unauthorized(new { message = "These credentials do not match the selected sign-in option." });

        // Non-admin accounts, including support Agents, use the User sign-in choice.
        return Ok(await tokenService.CreateAsync(user));
    }

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var user = id is null ? null : await userManager.FindByIdAsync(id);
        if (user is null) return Unauthorized();
        return Ok(new { user.Id, user.DisplayName, user.Email, Roles = await userManager.GetRolesAsync(user) });
    }

    [Authorize, HttpDelete("me")]
    public async Task<IActionResult> DeleteMyAccount(DeleteAccountRequest request, CancellationToken token)
    {
        if (!string.Equals(request.Confirmation?.Trim(), "DELETE", StringComparison.Ordinal))
            return BadRequest(new { message = "Type DELETE to confirm account deletion." });

        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = id is null ? null : await userManager.FindByIdAsync(id);
        if (user is null) return Unauthorized();

        var roles = await userManager.GetRolesAsync(user);
        // Self-service deletion is only for signed-up requester accounts, not staff/admin accounts.
        if (!roles.Contains("User", StringComparer.Ordinal) || roles.Contains("Agent", StringComparer.Ordinal) || roles.Contains("Admin", StringComparer.Ordinal))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Password) || !await userManager.CheckPasswordAsync(user, request.Password))
            return BadRequest(new { message = "Your current password is incorrect." });

        var originalEmail = user.Email;
        if (string.IsNullOrWhiteSpace(originalEmail))
            return BadRequest(new { message = "No email address is available for the deletion confirmation." });

        // Keep ticket/audit foreign keys valid; remove access and anonymize the identity profile.
        // The outgoing confirmation is written to the durable outbox inside this transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(token);

        var removeRoles = await userManager.RemoveFromRolesAsync(user, roles);
        if (!removeRoles.Succeeded)
        {
            await transaction.RollbackAsync(token);
            return StatusCode(500, new { message = "Could not remove account permissions." });
        }

        var anonymizedEmail = $"deleted-{Guid.NewGuid():N}@deleted.invalid";
        user.DisplayName = "Deleted user";
        user.UserName = anonymizedEmail;
        user.Email = anonymizedEmail;
        user.NormalizedUserName = userManager.NormalizeName(anonymizedEmail);
        user.NormalizedEmail = userManager.NormalizeEmail(anonymizedEmail);
        user.EmailConfirmed = false;
        user.PhoneNumber = null;
        user.PasswordHash = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
        user.AccessFailedCount = 0;

        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            await transaction.RollbackAsync(token);
            return StatusCode(500, new { message = "Could not complete account deletion." });
        }

        // Cancel any unsent notifications for this address so stale ticket updates are not delivered after deletion.
        var pendingNotifications = await db.EmailOutbox
            .Where(message => message.ToEmail == originalEmail && message.SentAtUtc == null)
            .ToListAsync(token);
        db.EmailOutbox.RemoveRange(pendingNotifications);

        db.EmailOutbox.Add(new EmailOutboxMessage
        {
            ToEmail = originalEmail,
            Subject = "Your OrbitDesk account has been deleted",
            Body = $"Hi,\n\nYour OrbitDesk account has been deleted at your request. Your profile details have been anonymized and sign-in has been disabled. Historical ticket and audit records may remain for support integrity.\n\nIf you did not request this action, please contact your support administrator.\n\nOrbitDesk Support"
        });
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);

        return NoContent();
    }
}
