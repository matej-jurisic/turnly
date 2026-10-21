using System.Text.Json;
using System.Text.RegularExpressions;
using Turnly.Core.Dtos;

namespace Turnly.Api.Endpoints;

public static class VersionEndpoints
{
    // turnly-build.sh publishes each APK as turnly-v{versionName}-{versionCode}.apk.
    private static readonly Regex ApkName = new(@"^turnly-v[0-9.]+-(\d+)\.apk$", RegexOptions.Compiled);

    public static void MapVersionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/version");

        // Anonymous: the update banner should work before sign-in too, and reveals nothing private.
        group.MapGet("", (IWebHostEnvironment env, IConfiguration config) =>
            Results.Ok(new AppVersionDto(ReadWebBuild(env), LatestApk(config)?.Code)));

        group.MapGet("/android.apk", (IConfiguration config) =>
            LatestApk(config) is { } apk
                ? Results.File(apk.Path, "application/vnd.android.package-archive", Path.GetFileName(apk.Path))
                : Results.NotFound());
    }

    /// <summary>The build id Vite writes to <c>version.json</c> next to the served frontend. Read per
    /// request: the file only changes on a redeploy, and it's tiny.</summary>
    private static string? ReadWebBuild(IWebHostEnvironment env)
    {
        if (string.IsNullOrEmpty(env.WebRootPath)) return null;
        var path = Path.Combine(env.WebRootPath, "version.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("build", out var build) ? build.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The newest published APK in <c>Releases:Path</c>, or null when that isn't configured
    /// or holds none.</summary>
    private static (string Path, int Code)? LatestApk(IConfiguration config)
    {
        var dir = config["Releases:Path"];
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "turnly-v*.apk")
            .Select(path => (Path: path, Match: ApkName.Match(System.IO.Path.GetFileName(path))))
            .Where(f => f.Match.Success)
            .Select(f => ((string Path, int Code)?)(f.Path, int.Parse(f.Match.Groups[1].Value)))
            .MaxBy(f => f!.Value.Code);
    }
}
