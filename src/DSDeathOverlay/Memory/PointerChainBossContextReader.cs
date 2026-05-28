using System;
using DSDeathOverlay.Logging;

namespace DSDeathOverlay.Memory;

/// <summary>
/// Reads "active boss" from game memory by walking a configured pointer chain
/// and looking the resulting integer up in <see cref="BossDetection.FlagToBossId"/>.
///
/// The shipped <c>bosses.json</c> does not enable this for any game today — it
/// is a forward hook so community-supplied offsets can be dropped in via the
/// external <c>bosses.json</c> without code changes.
/// </summary>
public sealed class PointerChainBossContextReader : IBossContextReader
{
    private readonly IMemoryReader _reader;
    private readonly BossDetection _detection;
    private readonly ILogger _log;
    private readonly int[] _offsets;

    private string? _activeBossId;

    public string? ActiveBossId => _activeBossId;

    public PointerChainBossContextReader(IMemoryReader reader, BossDetection detection, ILogger log)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _detection = detection ?? throw new ArgumentNullException(nameof(detection));
        _log = log ?? NullLogger.Instance;

        int[]? chain = _reader.IsWow64 ? _detection.ChainOffsets32 : _detection.ChainOffsets64;
        if (chain is null || chain.Length == 0)
        {
            throw new ArgumentException(
                "BossDetection of type 'pointerChainFlag' has no pointer chain for " +
                $"{(_reader.IsWow64 ? "32" : "64")}-bit.", nameof(detection));
        }
        _offsets = chain;
    }

    public void Refresh()
    {
        if (!PointerChainDeathReader.TryWalk(_reader, _offsets, out int flag))
        {
            _activeBossId = null;
            return;
        }

        if (flag == 0 || _detection.FlagToBossId is null)
        {
            _activeBossId = null;
            return;
        }

        _activeBossId = _detection.FlagToBossId.TryGetValue(flag.ToString(), out string? id) ? id : null;
    }
}
