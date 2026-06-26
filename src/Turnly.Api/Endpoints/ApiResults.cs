using System.Security.Claims;
using Turnly.Api.Auth;
using Turnly.Core.Common;
using Turnly.Core.Dtos;

namespace Turnly.Api.Endpoints;

/// <summary>
/// Response body for a successful authentication. For web clients the refresh token rides in the
/// httpOnly cookie and <see cref="RefreshToken"/> is null; for the native app (which can't rely on
/// cross-origin cookies) it's returned here for secure on-device storage.
/// </summary>
public record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, UserDto User, string? RefreshToken = null);

public static class ApiResults
{
    /// <summary>Header the native shell sends on every request (see web/src/lib/api.ts).</summary>
    public const string ClientHeader = "X-Turnly-Client";

    /// <summary>Header the native shell uses to present its stored refresh token (no cookie).</summary>
    public const string RefreshHeader = "X-Refresh-Token";

    /// <summary>True when the request comes from the native app rather than a browser.</summary>
    public static bool IsNativeClient(this HttpContext http) =>
        http.Request.Headers.TryGetValue(ClientHeader, out var v) &&
        string.Equals(v.ToString(), "android", StringComparison.OrdinalIgnoreCase);

    /// <summary>The refresh token a native client presents via header, or null.</summary>
    public static string? ReadRefreshHeader(this HttpContext http) =>
        http.Request.Headers.TryGetValue(RefreshHeader, out var v) && !string.IsNullOrEmpty(v)
            ? v.ToString()
            : null;

    public static IResult ToProblem(this Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };
        return Results.Problem(detail: error.Message, statusCode: status);
    }

    /// <summary>
    /// Returns the access token + user. Web clients also get the refresh token set as an httpOnly
    /// cookie; native clients get it in the body instead (they store it in secure device storage,
    /// since cross-origin cookies aren't reliable in the WebView).
    /// </summary>
    public static IResult WriteAuth(this AuthResult result, RefreshCookieManager cookies, HttpContext http)
    {
        if (http.IsNativeClient())
            return Results.Ok(new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User, result.RefreshToken));

        cookies.Set(http, result.RefreshToken, result.RefreshTokenExpiresAt);
        return Results.Ok(new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User));
    }

    public static Guid? GetUserId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;
}
