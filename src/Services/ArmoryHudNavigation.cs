using Sws2Armory.Configuration;
using Sws2Armory.Models;

namespace Sws2Armory.Services;

public enum ArmoryHudSection { Weapons, Knives, Gloves, Player, Tools }

public static class ArmoryHudNavigation
{
    // The shared Panorama layout has five sidebar slots. Keep agents/music
    // together and attachments/customization/reset under Tools so none is lost.
    public static IEnumerable<ArmoryHudSection> Sections(ArmorySettings settings)
    {
        if (!settings.Enabled) yield break;
        if (settings.Weapons) yield return ArmoryHudSection.Weapons;
        if (settings.Knives) yield return ArmoryHudSection.Knives;
        if (settings.Gloves) yield return ArmoryHudSection.Gloves;
        if (settings.Agents || settings.Music) yield return ArmoryHudSection.Player;
        yield return ArmoryHudSection.Tools;
    }

    public static CosmeticKind[] Kinds(ArmoryHudSection section) => section switch
    {
        ArmoryHudSection.Weapons => [CosmeticKind.Weapon],
        ArmoryHudSection.Knives => [CosmeticKind.Knife],
        ArmoryHudSection.Gloves => [CosmeticKind.Glove],
        ArmoryHudSection.Player => [CosmeticKind.Agent, CosmeticKind.Music],
        ArmoryHudSection.Tools => [CosmeticKind.Sticker, CosmeticKind.Charm],
        _ => []
    };
}
