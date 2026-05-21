using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace DSDeathOverlay.Logging;

/// <summary>
/// Minimal thread-safe file logger that writes to <c>deaths.log</c> next to the executable.
/// Used only for diagnostics (AOB misses, process-not-found). Never throws — logging must
/// never crash the app.
/// </summary>
public sealed class FileLogger : ILogger, IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter? _writer;
    private bool _disposed;

    public FileLogger()
    {
        try
        {
            string dir = AppContext.BaseDirectory;
            string path = Path.Combine(dir, "deaths.log");
            // overwrite on each run; logs are diagnostic, not historical.
            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch
        {
            _writer = null; // disk write failed; degrade silently
        }
    }

    public void Log(string message)
    {
        if (_disposed) return;
        try
        {
            lock (_gate)
            {
                _writer?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
        }
        catch
        {
            // ignore: logging failures must not bubble out.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { lock (_gate) _writer?.Dispose(); } catch { /* ignore */ }
    }
}
