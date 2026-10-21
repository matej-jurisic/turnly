using System.Net;
using Microsoft.AspNetCore.Hosting;
using Turnly.Core.Dtos;

namespace Turnly.Tests.Integration;

public class VersionTests : IDisposable
{
    private readonly TurnlyApiFactory _factory = new();
    private readonly string _releases = Directory.CreateTempSubdirectory("turnly-releases-").FullName;

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_releases, recursive: true);
    }

    private HttpClient Client() =>
        _factory.WithWebHostBuilder(b => b.UseSetting("Releases:Path", _releases)).CreateClient();

    [Fact]
    public async Task Version_reports_newest_published_apk()
    {
        File.WriteAllText(Path.Combine(_releases, "turnly-v1.9-10.apk"), "old");
        File.WriteAllText(Path.Combine(_releases, "turnly-v1.28-29.apk"), "new");
        File.WriteAllText(Path.Combine(_releases, "turnly.apk"), "copy");

        var client = Client();
        var version = await (await client.GetAsync("/api/version")).ReadAsync<AppVersionDto>();
        Assert.Equal(29, version.Android);

        var apk = await client.GetAsync("/api/version/android.apk");
        Assert.Equal(HttpStatusCode.OK, apk.StatusCode);
        Assert.Equal("new", await apk.Content.ReadAsStringAsync());
        Assert.Equal("turnly-v1.28-29.apk", apk.Content.Headers.ContentDisposition?.FileName);
    }

    [Fact]
    public async Task Version_without_releases_has_no_android_build()
    {
        var client = _factory.CreateClient();
        var version = await (await client.GetAsync("/api/version")).ReadAsync<AppVersionDto>();
        Assert.Null(version.Android);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/version/android.apk")).StatusCode);
    }
}
