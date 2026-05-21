namespace DSDeathOverlay.Logging;

/// <summary>
/// Minimal logging contract. We keep it tiny so we don't have to drag in a logging
/// framework for a single-purpose overlay.
/// </summary>
public interface ILogger
{
    void Log(string message);
}

/// <summary>Discards everything. Useful for unit tests.</summary>
public sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();
    public void Log(string message) { }
}
