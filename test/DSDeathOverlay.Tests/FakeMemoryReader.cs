using System.Collections.Generic;
using DSDeathOverlay.Memory;

namespace DSDeathOverlay.Tests;

/// <summary>
/// In-memory fake implementing <see cref="IMemoryReader"/> so we can unit-test
/// the pointer-chain walker without a real remote process. Pre-populate the
/// <see cref="Memory"/> dictionary with the values you want returned at specific
/// remote addresses.
/// </summary>
internal sealed class FakeMemoryReader : IMemoryReader
{
    public Dictionary<ulong, ulong> Qwords { get; } = new();
    public Dictionary<ulong, uint> Dwords { get; } = new();

    public ulong ModuleBase { get; init; } = 0x0000_0000_1000_0000UL;
    public uint ModuleSize { get; init; } = 0x0010_0000;
    public bool IsWow64 { get; init; }

    public int ReadBytes(ulong remoteAddress, byte[] buffer) => 0;

    public bool TryReadInt32(ulong address, out int value)
    {
        if (Dwords.TryGetValue(address, out uint v))
        {
            value = unchecked((int)v);
            return true;
        }
        value = 0;
        return false;
    }

    public bool TryReadUInt32(ulong address, out uint value)
        => Dwords.TryGetValue(address, out value);

    public bool TryReadUInt64(ulong address, out ulong value)
        => Qwords.TryGetValue(address, out value);
}
