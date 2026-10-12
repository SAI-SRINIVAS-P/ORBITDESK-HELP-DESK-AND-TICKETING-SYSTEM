using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HelpDesk.Api.Data;

public sealed class HelpDeskDesignTimeDbContextFactory : IDesignTimeDbContextFactory<HelpDeskDbContext>
{
    public HelpDeskDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=localhost,1433;Database=OrbitDesk;User Id=sa;Password=LocalOnly_StrongPassword_ChangeMe_123!;TrustServerCertificate=True;Encrypt=False";
        var options = new DbContextOptionsBuilder<HelpDeskDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new HelpDeskDbContext(options);
    }
}
