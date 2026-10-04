using Lupira.Testing.Mcp;
using Lupira.Testing.Postgres;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

[Collection("integration")]
public sealed class McpToolArgumentsTests(PhotoApiTestFactory factory) : McpStrictArgumentsTests
{
    protected override string DeclaredToolName => "photo_stats";

    public override Task InitializeAsync() => factory.ResetAsync();

    protected override HttpClient CreateAuthenticatedClient() => factory.ApiClient("alice@x.test");
}
