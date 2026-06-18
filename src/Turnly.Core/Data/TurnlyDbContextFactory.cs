using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Turnly.Core.Data;

/// <summary>
/// Design-time factory used by the EF Core tools (e.g. `dotnet ef migrations add`).
/// Migrations are generated against SQLite, the Phase-1 primary provider. Postgres
/// support is wired at runtime in <c>AddTurnlyCore</c>; a Postgres-specific migration
/// set is generated separately when first deploying on Postgres.
/// </summary>
public class TurnlyDbContextFactory : IDesignTimeDbContextFactory<TurnlyDbContext>
{
    public TurnlyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TurnlyDbContext>()
            .UseSqlite("Data Source=turnly-design.db")
            .Options;
        return new TurnlyDbContext(options);
    }
}
