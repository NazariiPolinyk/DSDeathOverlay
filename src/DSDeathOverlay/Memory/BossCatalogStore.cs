using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DSDeathOverlay.Logging;

namespace DSDeathOverlay.Memory;

/// <summary>
/// Loads the <see cref="BossCatalogSet"/>. Resolution order mirrors
/// <see cref="GameProfileStore"/>:
///
///   1. <c>bosses.json</c> next to the running .exe (lets the user add/edit
///      bosses or auto-detection offsets without rebuilding).
///   2. Embedded <c>bosses.json</c> resource (always present so the app boots
///      with a usable catalog).
/// </summary>
public static class BossCatalogStore
{
    private const string FileName = "bosses.json";

    private const string EmbeddedResourceName = "DSDeathOverlay.bosses.json";

    public static BossCatalogSet Load(ILogger log)
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
                    log.Log($"Loaded {parsed.Games.Length} boss catalog(s) from {externalPath}");
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
        log.Log($"Loaded {embedded.Games.Length} boss catalog(s) from embedded resource.");
        return embedded;
    }

    public static BossCatalogSet? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<BossCatalogSet>(json, JsonOptions);
    }

    public static BossCatalogSet? LoadEmbedded()
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
