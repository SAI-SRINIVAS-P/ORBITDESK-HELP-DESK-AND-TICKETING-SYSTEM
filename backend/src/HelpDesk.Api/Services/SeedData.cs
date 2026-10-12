using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Identity;
namespace HelpDesk.Api.Services;
public static class SeedData {
    private static readonly string[] Roles = ["User", "Agent", "Admin"];
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration) {
        await using var scope = services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var role in Roles) if (!await roles.RoleExistsAsync(role)) Ensure(await roles.CreateAsync(new IdentityRole(role)), $"create role {role}");
        await SeedUser(users, configuration["Seed:AdminEmail"], configuration["Seed:AdminPassword"], "OrbitDesk Admin", "Admin");
        await SeedUser(users, configuration["Seed:AgentEmail"], configuration["Seed:AgentPassword"], "Support Agent", "Agent");
    }
    private static async Task SeedUser(UserManager<ApplicationUser> manager, string? email, string? password, string name, string role) {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        var user = await manager.FindByEmailAsync(email);
        if (user is null) { user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name }; Ensure(await manager.CreateAsync(user, password), $"create seed account {email}"); }
        if (!await manager.IsInRoleAsync(user, role)) Ensure(await manager.AddToRoleAsync(user, role), $"assign {role} to {email}");
    }
    private static void Ensure(IdentityResult result, string operation) { if (!result.Succeeded) throw new InvalidOperationException($"Could not {operation}: {string.Join("; ", result.Errors.Select(x => x.Description))}"); }
}
