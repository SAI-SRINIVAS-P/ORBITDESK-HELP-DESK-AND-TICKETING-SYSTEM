using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using HelpDesk.Api.Services;
using HelpDesk.Api.Validators;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).Enrich.FromLogContext().WriteTo.Console().CreateLogger();
builder.Host.UseSerilog();
builder.Services.AddDbContext<HelpDeskDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddIdentityCore<ApplicationUser>(options => {
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<HelpDeskDbContext>().AddSignInManager();
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 bytes long.");
// Share the exact same key instance between token issuance and validation.
// The explicit kid also makes signing-key selection deterministic in IdentityModel.
var jwtSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
{
    KeyId = "orbitdesk-hs256-v1"
};
builder.Services.AddSingleton(jwtSigningKey);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => {
    options.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateIssuerSigningKey = true, IssuerSigningKey = jwtSigningKey,
        TryAllIssuerSigningKeys = true,
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = System.Security.Claims.ClaimTypes.Name, RoleClaimType = System.Security.Claims.ClaimTypes.Role
    };
    options.Events = new JwtBearerEvents {
        OnTokenValidated = async context => {
            var principal = context.Principal;
            var userId = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrWhiteSpace(userId) || principal?.Identity is not System.Security.Claims.ClaimsIdentity identity) {
                context.Fail("The access token has no valid user identity.");
                return;
            }
            var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            if (user is null || await userManager.IsLockedOutAsync(user)) {
                context.Fail("This account is unavailable.");
                return;
            }
            foreach (var claim in identity.FindAll(System.Security.Claims.ClaimTypes.Role).ToList()) identity.RemoveClaim(claim);
            foreach (var role in await userManager.GetRolesAsync(user)) identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role));
            foreach (var claim in identity.FindAll(System.Security.Claims.ClaimTypes.Name).ToList()) identity.RemoveClaim(claim);
            identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, user.DisplayName));
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddValidatorsFromAssemblyContaining<CreateTicketRequestValidator>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddHostedService<EmailNotificationBackgroundService>();
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy.WithOrigins("http://localhost:5173", "http://localhost:3000").AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
if (app.Configuration.GetValue<bool>("Database:EnsureCreated")) {
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
    await db.Database.EnsureCreatedAsync();
}
await SeedData.InitializeAsync(app.Services, app.Configuration);
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseSerilogRequestLogging();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
public partial class Program { }
