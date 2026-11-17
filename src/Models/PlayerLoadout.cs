namespace Sws2Armory.Models;

public sealed class PlayerLoadout
{
    public Dictionary<string, ItemSelection> Items { get; set; } = new(StringComparer.Ordinal);
    public string? TerroristAgent { get; set; }
    public string? CounterTerroristAgent { get; set; }
    public string? Music { get; set; }
    public int MusicMvps { get; set; }
}

public sealed class ItemSelection
{
    public string? CosmeticId { get; set; }
    public float? Wear { get; set; }
    public int? Seed { get; set; }
    public int? StatTrak { get; set; }
    public string NameTag { get; set; } = "";
    public Dictionary<int, string> Stickers { get; set; } = [];
    public string? Charm { get; set; }
    public int CharmSeed { get; set; }
}
