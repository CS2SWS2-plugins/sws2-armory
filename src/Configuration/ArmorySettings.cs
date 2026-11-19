using Sws2Armory.Models;

namespace Sws2Armory.Configuration;

public sealed class ArmorySettings
{
    public bool Enabled { get; set; } = true;
    public bool Weapons { get; set; } = true;
    public bool Knives { get; set; } = true;
    public bool Gloves { get; set; } = true;
    public bool Agents { get; set; } = true;
    public bool Music { get; set; } = true;
    public bool Stickers { get; set; } = true;
    public bool Charms { get; set; } = true;
    public float CommandCooldownSeconds { get; set; } = 1;
    public bool Allows(CosmeticKind kind) => Enabled && kind switch
    {
        CosmeticKind.Weapon => Weapons,
        CosmeticKind.Knife => Knives,
        CosmeticKind.Glove => Gloves,
        CosmeticKind.Agent => Agents,
        CosmeticKind.Music => Music,
        CosmeticKind.Sticker => Stickers,
        CosmeticKind.Charm => Charms,
        _ => false
    };
}
