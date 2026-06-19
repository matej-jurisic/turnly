using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Turnly.Core.Auth;
using Turnly.Core.Data;
using Turnly.Core.Services;

namespace Turnly.Tests.Unit;

/// <summary>
/// Spins up an isolated in-memory SQLite database (kept alive by an open connection) plus
/// real Core services, so business logic is exercised against the actual EF model.
/// </summary>
public sealed class TestContext : IDisposable
{
    private readonly SqliteConnection _connection;

    public TurnlyDbContext Db { get; }
    public IPasswordHasher Hasher { get; }
    public ITokenService Tokens { get; }
    public UserService Users { get; }
    public AuthService Auth { get; }
    public SetupService Setup { get; }
    public TagService Tags { get; }
    public ChoreService Chores { get; }
    public AwardService Awards { get; }
    public RedemptionService Redemptions { get; }

    public TestContext()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TurnlyDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new TurnlyDbContext(options);
        Db.Database.Migrate();

        Hasher = new PasswordHasher();
        Tokens = new TokenService(Options.Create(new JwtOptions
        {
            Secret = "unit-test-secret-key-long-enough-1234567890",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 180
        }));

        Users = new UserService(Db, Hasher);
        Auth = new AuthService(Db, Hasher, Tokens);
        Setup = new SetupService(Db, Hasher, Auth);
        Tags = new TagService(Db);
        Chores = new ChoreService(Db, Tags);
        Awards = new AwardService(Db);
        Redemptions = new RedemptionService(Db);
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
