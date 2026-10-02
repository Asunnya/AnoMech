namespace AnoMech.Core.Native.Interfaces;

// The client's RSF table: resolves obfuscated file paths of duty content, which the server only
// sends inside the duty.
public interface IRsfFunctions
{
    void Add(byte[] record);
}
