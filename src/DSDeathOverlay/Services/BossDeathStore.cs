using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DSDeathOverlay.Logging;

namespace DSDeathOverlay.Services;

/// <summary>
/// Persisted per-boss death counts, keyed by game id (e.g. <c>"DS3"</c>) then
/// by boss id (the stable <c>id</c> in <c>bosses.json</c>).
///
/// Stored as plain JSON at
/// <c>%LOCALAPPDATA%\DSDeathOverlay\boss-deaths.json</c>. All errors are logged
/// and degrade gracefully to empty data — losing per-boss history is annoying
/// but never fatal.
/// </summary>
public sealed class BossDeathStore
{
    private readonly ILogger _log;
    private readonly string _path;

    /// <summary>Game id -> (boss id -> death count).</summary>
    public Dictionary<string, Dictionary<string, int>> Counts { get; private set; } = new();

    private BossDeathStore(string path, ILogger log)
    {
        _path = path;
        _log = log;
    }

    public static BossDeathStore Load(ILogger log)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DSDeathOverlay");
        string path = Path.Combine(dir, "boss-deaths.json");
        var store = new BossDeathStore(path, log);

        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json);
                if (parsed is not null) store.Counts = parsed;
            }
        }
        catch (Exception ex)
        {
            log.Log($"Failed to load boss deaths from {path}: {ex.Message}. Starting fresh.");
        }

        return store;
    }

    public int GetCount(string gameId, string bossId)
    {
        return Counts.TryGetValue(gameId, out var perGame)
            && perGame.TryGetValue(bossId, out int c)
                ? c
                : 0;
    }

    public void Increment(string gameId, string bossId)
    {
        if (!Counts.TryGetValue(gameId, out var perGame))
        {
            perGame = new Dictionary<string, int>();
            Counts[gameId] = perGame;
        }
        perGame[bossId] = (perGame.TryGetValue(bossId, out int c) ? c : 0) + 1;
    }

    public void ResetBoss(string gameId, string bossId)
    {
        if (Counts.TryGetValue(gameId, out var perGame))
            perGame.Remove(bossId);
    }

    public void ResetGame(string gameId)
    {
        Counts.Remove(gameId);
    }

    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(Counts, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            _log.Log($"Failed to save boss deaths to {_path}: {ex.Message}");
        }
    }
}
