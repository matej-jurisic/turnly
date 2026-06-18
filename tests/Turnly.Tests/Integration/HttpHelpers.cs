using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Turnly.Api.Endpoints;
using Turnly.Core.Dtos;

namespace Turnly.Tests.Integration;

internal static class HttpHelpers
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body)
        => client.PostAsJsonAsync(url, body, Json);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body)
        => client.PutAsJsonAsync(url, body, Json);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static void UseBearer(this HttpClient client, string accessToken)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    /// <summary>Completes first-run setup and returns the resulting auth (with admin token).</summary>
    public static async Task<AuthResponse> SetupAdminAsync(
        this HttpClient client, string username = "admin", string password = "password123")
    {
        var response = await client.PostJsonAsync("/api/setup",
            new SetupRequest(username, "Admin", password, null));
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<AuthResponse>();
    }

    /// <summary>Logs in and returns a client whose requests carry that user's bearer token.</summary>
    public static async Task<AuthResponse> LoginAsync(
        this HttpClient client, string username, string password)
    {
        var login = await (await client.PostJsonAsync("/api/auth/login",
            new LoginRequest(username, password))).ReadAsync<AuthResponse>();
        client.UseBearer(login.AccessToken);
        return login;
    }
}
