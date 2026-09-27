namespace AnoMech.Relay.Tests;

public class RelayAdminModelTests
{
    [TestCase("http://localhost:7890", true)]
    [TestCase("http://[::1]:7890", true)]
    [TestCase("https://relay.example", true)]
    [TestCase("http://relay.example", false)]
    [TestCase("https://user:secret@relay.example", false)]
    public void AdminTransportPolicy(string uri, bool safe)
        => Assert.That(RelayAdmin.IsSafeAdminUri(uri), Is.EqualTo(safe));
}
