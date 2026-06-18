using System.Security.Claims;
using Turnly.Api.Auth;
using Turnly.Core.Common;
using Turnly.Core.Dtos;

namespace Turnly.Api.Endpoints;

/// <summary>Response body for a successful authentication; refresh token is in the cookie, not here.</summary>
public record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, UserDto User);

public static class ApiResults
{
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

    /// <summary>Sets the refresh cookie and returns the access token + user as the response body.</summary>
    public static IResult WriteAuth(this AuthResult result, RefreshCookieManager cookies, HttpContext http)
    {
        cookies.Set(http, result.RefreshToken, result.RefreshTokenExpiresAt);
        return Results.Ok(new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User));
    }

    public static Guid? GetUserId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;
}
