using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Turnly.Core.Data;

namespace Turnly.Tests.Integration;

/// <summary>
/// Boots the real API against an isolated in-memory SQLite database. The connection is held
/// open for the factory's lifetime so the schema (migrated on startup) persists across requests.
/// </summary>
public class TurnlyApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public TurnlyApiFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Secret", "integration-test-secret-key-long-enough-1234567890");
        builder.UseSetting("Auth:RefreshCookie:Secure", "false");

        builder.ConfigureServices(services =>
        {
            var descriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<TurnlyDbContext>)
                            || d.ServiceType == typeof(TurnlyDbContext))
                .ToList();
            foreach (var descriptor in descriptors)
                services.Remove(descriptor);

            services.AddDbContext<TurnlyDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
