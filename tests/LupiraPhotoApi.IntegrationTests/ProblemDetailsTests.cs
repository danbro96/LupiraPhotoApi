using System.Net;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

/// <summary>The OpenAPI document promises every 4xx/5xx carries ProblemDetails; only a live response can
/// prove it, since the promise rests on UseStatusCodePages rather than on the endpoints' return types.</summary>
public class ProblemDetailsTests(PhotoApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Missing_asset_returns_problem_details()
    {
        var api = Factory.ApiClient("anna@example.com");

        var resp = await api.GetAsync($"/photos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        using var body = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Anonymous_rejection_returns_problem_details()
    {
        var resp = await Factory.AnonymousClient().GetAsync("/photos");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }

    /// /mcp speaks JSON-RPC; rewriting its empty error bodies would corrupt the transport.
    [Fact]
    public async Task Mcp_challenge_is_not_rewritten()
    {
        var resp = await Factory.AnonymousClient().GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }
}
