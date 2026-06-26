using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Turnly.Api;
using Turnly.Api.Auth;
using Turnly.Api.Endpoints;
using Turnly.Core;
using Turnly.Core.Auth;
using Turnly.Core.Data;
using Turnly.Core.Enums;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurnlyCore(builder.Configuration);

// Serialize enums (e.g. UserRole) as strings in JSON for clearer API contracts.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.Configure<RefreshCookieOptions>(builder.Configuration.GetSection("Auth:RefreshCookie"));
builder.Services.AddSingleton<RefreshCookieManager>();

// Background scheduler that fires due chore notifications (idle until VAPID keys are configured).
builder.Services.AddHostedService<NotificationSchedulerService>();
// Auto-advances multi-completion chores whose window has expired; always runs.
builder.Services.AddHostedService<ChoreAutoAdvanceService>();

// Keep JWT claim types verbatim ("sub", "role") instead of the legacy SOAP mappings.
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = TokenService.RoleClaimType,
            NameClaimType = "username"
        };
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy("Admin", policy => policy.RequireRole(nameof(UserRole.Admin))));

// Cross-origin clients that aren't served same-origin from this host. Two cases:
//   - the Vite dev server (when it isn't proxying /api), e.g. http://localhost:5173;
//   - the native Android app (Capacitor), whose WebView origin is https://localhost.
// Self-hosters opt in by listing the origins in Cors:Origins. AllowAnyHeader covers the
// X-Turnly-Client marker the app sends; AllowCredentials is required for the auth flows.
var corsOrigins = (builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .Where(o => !string.IsNullOrWhiteSpace(o))
    .ToArray();
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("app", policy =>
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
}

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<TurnlyDbContext>().Database.Migrate();
}

app.UseDefaultFiles();
app.UseStaticFiles();

if (corsOrigins.Length > 0)
    app.UseCors("app");

app.UseAuthentication();
app.UseAuthorization();

app.MapSetupEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapChoreEndpoints();
app.MapTagEndpoints();
app.MapHistoryEndpoints();
app.MapAwardEndpoints();
app.MapNotificationEndpoints();
app.MapSettingsEndpoints();
app.MapAchievementEndpoints();
app.MapGachaEndpoints();

// SPA fallback: any non-API route serves the built frontend.
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
