using Microsoft.Extensions.Options;

namespace Turnly.Api.Auth;

public class RefreshCookieOptions
{
    public bool Secure { get; set; } = true;
    public string Path { get; set; } = "/api/auth";
    public string Name { get; set; } = "turnly_refresh";
}

/// <summary>Reads/writes the httpOnly refresh-token cookie. The access token never touches cookies.</summary>
public class RefreshCookieManager
{
    private readonly RefreshCookieOptions _options;

    public RefreshCookieManager(IOptions<RefreshCookieOptions> options)
    {
        _options = options.Value;
    }

    public void Set(HttpContext ctx, string rawToken, DateTimeOffset expires) =>
        ctx.Response.Cookies.Append(_options.Name, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = _options.Secure,
            SameSite = SameSiteMode.Strict,
            Path = _options.Path,
            Expires = expires,
            IsEssential = true
        });

    public void Clear(HttpContext ctx) =>
        ctx.Response.Cookies.Delete(_options.Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = _options.Secure,
            SameSite = SameSiteMode.Strict,
            Path = _options.Path
        });

    public string? Read(HttpContext ctx) =>
        ctx.Request.Cookies.TryGetValue(_options.Name, out var value) ? value : null;
}
