using System.Text.Json;
using System.Text.Json.Serialization;
using Sws2Armory.Models;

namespace Sws2Armory.Services;

public sealed class CosmeticCatalog
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public IReadOnlyList<CatalogEntry> Entries { get; }
    private readonly Dictionary<string, CatalogEntry> _byId;
    private readonly Dictionary<ushort, string> _weapons;

    public CosmeticCatalog(string path)
    {
        var entries = JsonSerializer.Deserialize<List<CatalogEntry>>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Empty armory catalog");
        if (entries.Count == 0) throw new InvalidDataException("Empty armory catalog");
        _byId = new(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!Enum.IsDefined(entry.Kind) || string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Name)
                || string.IsNullOrWhiteSpace(entry.Target) || entry.Kit < 0
                || !float.IsFinite(entry.Wear) || entry.Wear is < 0 or > 1
                || entry.Seed is < 0 or > 1000 || !_byId.TryAdd(entry.Id, entry))
                throw new InvalidDataException($"Invalid or duplicate catalog entry: {entry.Id}");
            if (entry.Kind is CosmeticKind.Weapon or CosmeticKind.Knife or CosmeticKind.Glove && entry.DefinitionIndex == 0)
                throw new InvalidDataException($"Missing item definition: {entry.Id}");
            if (entry.Kind == CosmeticKind.Agent && (entry.Target is not ("t" or "ct")
                || entry.Model is null || !(entry.Model.StartsWith("characters/models/", StringComparison.Ordinal) || entry.Model.StartsWith("agents/models/", StringComparison.Ordinal))
                || !entry.Model.EndsWith(".vmdl", StringComparison.Ordinal) || entry.Model.Contains("..", StringComparison.Ordinal)))
                throw new InvalidDataException($"Invalid agent: {entry.Id}");
        }
        Entries = entries.AsReadOnly();
        _weapons = entries.Where(e => e.Kind == CosmeticKind.Weapon)
            .DistinctBy(e => e.DefinitionIndex).ToDictionary(e => e.DefinitionIndex, e => e.Target);
    }

    public CatalogEntry? Find(string? id) => id is not null ? _byId.GetValueOrDefault(id) : null;
    public string? WeaponTarget(ushort definition) => _weapons.GetValueOrDefault(definition);
    public static bool IsKnife(ushort definition) => definition is 42 or 59 or >= 500 and <= 526;
    public IEnumerable<CatalogEntry> Search(CosmeticKind kind, string query = "", string? target = null) =>
        Entries.Where(e => e.Kind == kind && (target is null || e.Target == target)
            && (query.Length == 0 || e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (e.NameZh?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || e.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Group.Contains(query, StringComparison.OrdinalIgnoreCase)));
}
