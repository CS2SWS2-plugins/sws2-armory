using System.Text.Json;
using Sws2Armory.Models;

namespace Sws2Armory.Services;

// Accessed on the game thread. Serialize a snapshot before queuing disk work;
// the single writer preserves ordering, including disconnect/unload flushes.
public sealed class LoadoutStore
{
    private readonly string _directory;
    private readonly Dictionary<ulong, PlayerLoadout> _profiles = [];
    private Task _writer = Task.CompletedTask;
    private readonly Action<Exception> _onError;

    public LoadoutStore(string directory, Action<Exception> onError)
    {
        _directory = directory;
        _onError = onError;
        Directory.CreateDirectory(directory);
    }

    public PlayerLoadout Get(ulong steamId)
    {
        if (_profiles.TryGetValue(steamId, out var cached)) return cached;
        var path = PathFor(steamId);
        var profile = File.Exists(path)
            ? JsonSerializer.Deserialize<PlayerLoadout>(File.ReadAllText(path), CosmeticCatalog.JsonOptions)
                ?? throw new InvalidDataException($"Empty profile: {steamId}")
            : new PlayerLoadout();
        // Refuse malformed saves rather than silently overwriting player data.
        if (profile.MusicMvps is < 0 or > 999999 || profile.Items is null || profile.Items.Values.Any(i => i is null || i.Stickers is null
            || i.Stickers.Keys.Any(s => s is < 0 or > 4)
            || (i.Wear is { } w && (!float.IsFinite(w) || w is < 0 or > 1))
            || i.Seed is < 0 or > 1000 || i.StatTrak is < 0 or > 999999
            || i.NameTag is null || !CustomizationInput.ValidName(i.NameTag) || i.CharmSeed is < 0 or > 100000))
            throw new InvalidDataException($"Invalid profile: {steamId}");
        _profiles.Add(steamId, profile);
        return profile;
    }

    public void Save(ulong steamId)
    {
        var json = JsonSerializer.Serialize(Get(steamId), CosmeticCatalog.JsonOptions);
        var path = PathFor(steamId);
        _writer = _writer.ContinueWith(_ =>
        {
            try
            {
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, json);
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception exception) { _onError(exception); }
        }, TaskScheduler.Default);
    }

    public void Reset(ulong steamId) => _profiles[steamId] = new PlayerLoadout();
    public void Forget(ulong steamId)
    {
        Flush();
        _profiles.Remove(steamId);
    }
    public void Flush() => _writer.GetAwaiter().GetResult();
    private string PathFor(ulong steamId) => Path.Combine(_directory, $"{steamId}.json");
}
