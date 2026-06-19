using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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

// Dev-only CORS: the Vite dev server is expected to proxy /api (same-origin), but allow
// direct cross-origin calls with credentials as a fallback when origins are configured.
var devOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
if (devOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("dev", policy =>
        policy.WithOrigins(devOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
}

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<TurnlyDbContext>().Database.Migrate();
}

app.UseDefaultFiles();
app.UseStaticFiles();

if (devOrigins.Length > 0)
    app.UseCors("dev");

app.UseAuthentication();
app.UseAuthorization();

app.MapSetupEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapChoreEndpoints();
app.MapTagEndpoints();
app.MapHistoryEndpoints();

// SPA fallback: any non-API route serves the built frontend.
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
