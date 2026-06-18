using System.Security.Claims;
using Turnly.Api.Auth;
using Turnly.Core.Dtos;
using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (LoginRequest req, AuthService auth, RefreshCookieManager cookies, HttpContext http, CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(req.Username, req.Password, ct);
            return result.Succeeded
                ? result.Value!.WriteAuth(cookies, http)
                : result.Error!.ToProblem();
        });

        group.MapPost("/refresh", async (AuthService auth, RefreshCookieManager cookies, HttpContext http, CancellationToken ct) =>
        {
            var result = await auth.RefreshAsync(cookies.Read(http), ct);
            if (!result.Succeeded)
            {
                cookies.Clear(http);
                return result.Error!.ToProblem();
            }
            return result.Value!.WriteAuth(cookies, http);
        });

        group.MapPost("/logout", async (AuthService auth, RefreshCookieManager cookies, HttpContext http, CancellationToken ct) =>
        {
            await auth.LogoutAsync(cookies.Read(http), ct);
            cookies.Clear(http);
            return Results.NoContent();
        });

        group.MapGet("/me", async (ClaimsPrincipal principal, UserService users, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } id)
                return Results.Unauthorized();
            var result = await users.GetAsync(id, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        }).RequireAuthorization();

        group.MapPost("/change-password", async (ChangePasswordRequest req, ClaimsPrincipal principal, AuthService auth, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } id)
                return Results.Unauthorized();
            var result = await auth.ChangePasswordAsync(id, req.CurrentPassword, req.NewPassword, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        }).RequireAuthorization();
    }
}
