using Microsoft.AspNetCore.Identity;
namespace HelpDesk.Api.Models;
public sealed class ApplicationUser : IdentityUser {
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
