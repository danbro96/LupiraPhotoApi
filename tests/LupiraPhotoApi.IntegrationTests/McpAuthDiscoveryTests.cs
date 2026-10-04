using Lupira.Testing.Mcp;
using Lupira.Testing.Postgres;
using Xunit;

namespace LupiraPhotoApi.IntegrationTests;

[Collection("integration")]
public sealed class McpAuthDiscoveryTests(PhotoApiTestFactory factory) : McpResourceMetadataTests
{
    protected override string Issuer => factory.Authority!;

    public override Task InitializeAsync() => factory.ResetAsync();

    protected override HttpClient CreateAnonymousClient() => factory.AnonymousClient();
}
