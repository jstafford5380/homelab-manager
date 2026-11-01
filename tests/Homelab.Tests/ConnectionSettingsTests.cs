using Homelab.Api;
namespace Homelab.Tests;
public sealed class ConnectionSettingsTests
{
    private static ConnectionSettings Valid => new("NAS", "wss://nas.example/websocket", null, null, 60, false, null, null);
    [Theory]
    [InlineData("name-control")]
    [InlineData("name-long")]
    [InlineData("uri-null")]
    [InlineData("uri-space")]
    [InlineData("uri-control")]
    [InlineData("uri-port-zero")]
    [InlineData("uri-long")]
    [InlineData("tunnel-long")]
    [InlineData("tunnel-control")]
    public void InvalidFieldsAreRejectedWithoutThrowing(string scenario)
    {
        var input = scenario switch {
            "name-control" => Valid with { Name = "NAS\nforged log" },
            "name-long" => Valid with { Name = new string('x', 201) },
            "uri-null" => Valid with { ApiUri = null! },
            "uri-space" => Valid with { ApiUri = " wss://nas.example/websocket" },
            "uri-control" => Valid with { ApiUri = "wss://nas.example/websocket\n" },
            "uri-port-zero" => Valid with { ApiUri = "wss://nas.example:0/websocket" },
            "uri-long" => Valid with { ApiUri = "wss://" + new string('x', 2048) + "/websocket" },
            "tunnel-long" => Valid with { ConnectHost = new string('x', 254), ConnectPort = 443 },
            _ => Valid with { ConnectHost = "localhost\n", ConnectPort = 443 }
        };
        Assert.NotNull(ConnectionEndpoints.Validate(input));
    }
    [Fact]
    public void DirectAndTunnelConnectionsKeepLogicalTlsIdentity()
    {
        Assert.Null(ConnectionEndpoints.Validate(Valid));
        Assert.Null(ConnectionEndpoints.Validate(Valid with { ConnectHost = "127.0.0.1", ConnectPort = 8443 }));
        Assert.Null(ConnectionEndpoints.Validate(Valid with { ApiUri = "wss://[::1]:8443/websocket" }));
        Assert.NotNull(ConnectionEndpoints.Validate(Valid with { ConnectPort = 8443 }));
    }
    [Fact]
    public void EnrollmentAndRotationShareBoundedOpaqueCredentialValidation()
    {
        foreach (var key in new string?[] { null, "", "   ", "key\nsecret", "key\0secret", new('k', 8193) })
            Assert.NotNull(ConnectionEndpoints.ValidateApiKey(key));
        Assert.Null(ConnectionEndpoints.ValidateApiKey(new string('k', 8192)));
        Assert.Null(ConnectionEndpoints.ValidateApiKey("opaque_key+/=:-.123"));
    }
}
