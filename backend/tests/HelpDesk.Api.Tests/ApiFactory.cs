using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;
namespace HelpDesk.Api.Tests;
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime {
    public string TestConnectionString { get; private set; } = string.Empty;
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").WithPassword("Testcontainers_Strong_Password_123!").Build();
    public async Task InitializeAsync() => await _sql.StartAsync();
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        var connection = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "OrbitDeskTests" }.ConnectionString;
        TestConnectionString = connection;
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = connection, ["Database:EnsureCreated"] = "true",
            ["Jwt:Issuer"] = "OrbitDesk.Tests", ["Jwt:Audience"] = "OrbitDesk.Tests.Client",
            ["Jwt:Key"] = "test-only-signing-key-that-is-definitely-longer-than-32-bytes",
            ["Seed:AdminEmail"] = "admin-integration@example.com", ["Seed:AdminPassword"] = "Admin-Test-Password-123!",
            ["Seed:AgentEmail"] = "agent-integration@example.com", ["Seed:AgentPassword"] = "Agent-Test-Password-123!", ["Smtp:Host"] = ""
        }));
        builder.UseEnvironment("Development");
    }
    async Task IAsyncLifetime.DisposeAsync() { await base.DisposeAsync(); await _sql.DisposeAsync(); }
}
