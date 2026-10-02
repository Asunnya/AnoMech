namespace AnoMech.Core.Native.Interfaces;

// The client's RSV table: resolves the placeholder action/status names newer content ships with,
// which the server only sends inside the duty.
public interface IRsvFunctions
{
    void Add(string rsvKey, string resolved);
    void AddRaw(string rsvKey, byte[] valueBytes);
}
