using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DSDeathOverlay.Logging;

namespace DSDeathOverlay.Memory;

/// <summary>
/// Loads the list of <see cref="GameProfile"/>s. Resolution order:
///
///   1. <c>games.json</c> next to the running .exe (lets the user edit offsets
///      without rebuilding when a game patch shifts them).
///   2. Embedded <c>games.json</c> resource (always present in the binary so the
///      app works out of the box).
/// </summary>
public static class GameProfileStore
{
    private const string FileName = "games.json";

    /// <summary>
    /// Resource name for the embedded copy. The default .NET resource naming uses
    /// "{RootNamespace}.{FilePath dotted}" — for a top-level games.json that's
    /// "DSDeathOverlay.games.json".
    /// </summary>
    private const string EmbeddedResourceName = "DSDeathOverlay.games.json";

    public static GameProfileSet Load(ILogger log)
    {
        string externalPath = Path.Combine(AppContext.BaseDirectory, FileName);

        if (File.Exists(externalPath))
        {
            try
            {
                string json = File.ReadAllText(externalPath);
                var parsed = Deserialize(json);
                if (parsed is { Games.Length: > 0 })
                {
                    log.Log($"Loaded {parsed.Games.Length} game profile(s) from {externalPath}");
                    return parsed;
                }
                log.Log($"{externalPath} was empty / malformed; falling back to embedded.");
            }
            catch (Exception ex)
            {
                log.Log($"Failed to read {externalPath}: {ex.Message}. Falling back to embedded.");
            }
        }

        var embedded = LoadEmbedded()
            ?? throw new InvalidOperationException(
                $"Embedded resource '{EmbeddedResourceName}' is missing. Build is broken.");
        log.Log($"Loaded {embedded.Games.Length} game profile(s) from embedded resource.");
        return embedded;
    }

    /// <summary>Internal helper exposed for tests.</summary>
    public static GameProfileSet? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<GameProfileSet>(json, JsonOptions);
    }

    /// <summary>Internal helper exposed for tests.</summary>
    public static GameProfileSet? LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();
        return Deserialize(json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
