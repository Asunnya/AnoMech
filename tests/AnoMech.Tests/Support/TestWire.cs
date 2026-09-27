using System.Text;
using System.Text.Json;
using AnoMech.Multiplayer;
using AnoMech.Network;

namespace AnoMech.Tests;

internal static class TestWire
{
    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    public static void Rejects(TestDelegate action, string what)
    {
        var error = Assert.Catch(action, $"Accepted invalid input: {what}");
        Assert.That(error, Is.InstanceOf<InvalidDataException>().Or.InstanceOf<JsonException>().Or.InstanceOf<ArgumentException>()
            .Or.InstanceOf<FormatException>().Or.InstanceOf<TrafficLimitException>(), what);
    }

    // What RelayClient does with a decoded body.
    private static readonly JsonSerializerOptions ClientJson = new() { PropertyNameCaseInsensitive = true };
    public static MpMessage Receive(byte[] body, bool fromHost, Guid sender)
    {
        RelayWire.Validate(body, fromHost, sender);
        return JsonSerializer.Deserialize<MpMessage>(body, ClientJson) ?? throw new InvalidDataException("Null message.");
    }
}
