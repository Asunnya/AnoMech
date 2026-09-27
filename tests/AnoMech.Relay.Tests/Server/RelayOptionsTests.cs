using System.Net;

namespace AnoMech.Relay.Tests;

public class RelayOptionsTests
{
    [Test]
    public void MappedProxyCidrIsNormalized()
    {
        Assert.That(RelayOptions.TryParseNetwork("::ffff:192.0.2.0/120", out var mappedNetwork));
        Assert.That(mappedNetwork.Contains(IPAddress.Parse("192.0.2.8")));
    }
}
