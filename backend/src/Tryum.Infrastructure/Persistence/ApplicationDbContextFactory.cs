using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tryum.Infrastructure.Persistence;

/// <summary>
/// Factory dùng cho `dotnet ef migrations` – không cần chạy app hay kết nối DB thật.
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TRYUM_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=tryum_db;Username=tryum;Password=tryum_pass";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .Options;

        return new ApplicationDbContext(options);
    }
}