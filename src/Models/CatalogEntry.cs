namespace Sws2Armory.Models;

public enum CosmeticKind { Weapon, Knife, Glove, Agent, Music, Sticker, Charm }

public sealed record CatalogEntry
{
    public required string Id { get; init; }
    public required CosmeticKind Kind { get; init; }
    public required string Target { get; init; }
    public required string Group { get; init; }
    public required string Name { get; init; }
    public string? NameZh { get; init; }
    public ushort DefinitionIndex { get; init; }
    public int Kit { get; init; }
    public float Wear { get; init; } = 0.0001f;
    public int Seed { get; init; }
    public bool LegacyModel { get; init; }
    public string? Model { get; init; }
    public string? VoicePrefix { get; init; }
    public bool FemaleVoice { get; init; }
    public string DisplayName(bool chinese) => chinese && !string.IsNullOrWhiteSpace(NameZh) ? NameZh : Name;
}
