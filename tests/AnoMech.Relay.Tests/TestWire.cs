using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AnoMech.Network;

namespace AnoMech.Relay.Tests;

internal static class TestWire
{
    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    public static byte[] Zip(byte[] bytes)
    {
        using var stream = new MemoryStream();
        using (var compressor = new BrotliStream(stream, CompressionLevel.Fastest, true)) compressor.Write(bytes);
        return stream.ToArray();
    }

    public static void Rejects(TestDelegate action, string what)
    {
        var error = Assert.Catch(action, $"Accepted invalid input: {what}");
        Assert.That(error, Is.InstanceOf<InvalidDataException>().Or.InstanceOf<JsonException>().Or.InstanceOf<ArgumentException>()
            .Or.InstanceOf<FormatException>().Or.InstanceOf<TrafficLimitException>(), what);
    }
}

internal sealed class QuietLog : IRelayLog
{
    public readonly ConcurrentQueue<string> Lines = new();
    public void Info(string message) => Lines.Enqueue(message);
    public void Warn(string message) => Lines.Enqueue(message);
    public void Detail(string message) => Lines.Enqueue(message);
}
