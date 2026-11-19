using System.Globalization;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.SchemaDefinitions;
using Sws2Armory.Models;

namespace Sws2Armory.Services;

// All calls must run on the game thread. Integer econ attributes store their
// bits in a float union; numeric conversion would corrupt sticker IDs/counters.
public sealed class EconApplicator(CosmeticCatalog catalog)
{
    private ulong _nextWeaponItemId = 1UL << 20;
    private ulong _nextGloveItemId = 0xF000000000000000;

    public static void UpdateCounter(CBasePlayerWeapon weapon, int count)
    {
        var item = weapon.AttributeManager.Item;
        item.AttributeList.SetOrAddAttribute("kill eater", BitConverter.Int32BitsToSingle(count));
        item.NetworkedDynamicAttributes.SetOrAddAttribute("kill eater", BitConverter.Int32BitsToSingle(count));
        item.AttributeList.AttributesUpdated();
        item.NetworkedDynamicAttributes.AttributesUpdated();
        item.AttributeListUpdated();
        item.NetworkedDynamicAttributesUpdated();
        weapon.FallbackStatTrak = count;
        weapon.FallbackStatTrakUpdated();
    }

    public void Paint(CEconItemView item, ulong steamId, CatalogEntry? cosmetic,
        ItemSelection selection, bool knife, bool stickersEnabled, bool charmsEnabled)
    {
        if (cosmetic is not null) item.ItemDefinitionIndex = cosmetic.DefinitionIndex;
        item.EntityQuality = selection.StatTrak.HasValue ? 9 : knife ? 3 : 0;
        // Use an ordinary econ ID for weapons so the client reads the dynamic attributes.
        var glove = cosmetic?.Kind == CosmeticKind.Glove;
        var id = glove ? _nextGloveItemId++ : _nextWeaponItemId++;
        item.ItemID = id;
        item.ItemIDLow = (uint)id;
        item.ItemIDHigh = (uint)(id >> 32);
        item.AccountID = (uint)steamId;
        item.Initialized = true;
        item.CustomName = selection.NameTag;
        item.CustomNameOverride = selection.NameTag;
        foreach (var list in new[] { item.AttributeList, item.NetworkedDynamicAttributes })
        {
            list.Attributes.RemoveAll();
            list.SetOrAddAttribute("set item texture prefab", cosmetic?.Kit ?? 0);
            list.SetOrAddAttribute("set item texture seed", selection.Seed ?? cosmetic?.Seed ?? 0);
            list.SetOrAddAttribute("set item texture wear", selection.Wear ?? cosmetic?.Wear ?? 0.0001f);
            if (selection.StatTrak is { } count)
            {
                list.SetOrAddAttribute("kill eater", BitConverter.Int32BitsToSingle(count));
                list.SetOrAddAttribute("kill eater score type", BitConverter.Int32BitsToSingle(0));
            }
            if (!knife && stickersEnabled)
                foreach (var (slot, stickerId) in selection.Stickers)
                    if (slot is >= 0 and <= 4 && catalog.Find(stickerId) is { Kind: CosmeticKind.Sticker } sticker)
                        list.SetOrAddAttribute($"sticker slot {slot} id", BitConverter.Int32BitsToSingle(sticker.Kit));
            if (!knife && charmsEnabled && catalog.Find(selection.Charm) is { Kind: CosmeticKind.Charm } charm)
            {
                list.SetOrAddAttribute("keychain slot 0 id", BitConverter.Int32BitsToSingle(charm.Kit));
                if (selection.CharmSeed != 0)
                    list.SetOrAddAttribute("keychain slot 0 seed", BitConverter.Int32BitsToSingle(selection.CharmSeed));
            }
            if (!glove) list.AttributesUpdated();
        }
        item.ItemDefinitionIndexUpdated();
        item.EntityQualityUpdated();
        item.ItemIDHighUpdated();
        item.ItemIDLowUpdated();
        item.AccountIDUpdated();
        item.InitializedUpdated();
        item.AttributeListUpdated();
        item.NetworkedDynamicAttributesUpdated();
        item.CustomNameUpdated();
    }

    public void Weapon(CBasePlayerWeapon weapon, ulong steamId, CatalogEntry? cosmetic,
        ItemSelection selection, ushort? knifeDefinition, bool stickersEnabled, bool charmsEnabled)
    {
        if (knifeDefinition is { } definition)
        {
            // The econ definition can already match while the entity still uses the stock subclass.
            var subclass = definition.ToString(CultureInfo.InvariantCulture);
            if (weapon.AttributeManager.Item.ItemDefinitionIndex != definition
                || cosmetic is not null && weapon.SubclassID.HashCode != MurmurHash2.HashString(subclass))
                weapon.AcceptInput("ChangeSubclass", subclass);
            weapon.AttributeManager.Item.ItemDefinitionIndex = definition;
        }
        weapon.FallbackPaintKit = cosmetic?.Kit ?? 0;
        weapon.FallbackSeed = selection.Seed ?? cosmetic?.Seed ?? 0;
        weapon.FallbackWear = selection.Wear ?? cosmetic?.Wear ?? 0.0001f;
        weapon.FallbackStatTrak = selection.StatTrak ?? -1;
        weapon.OriginalOwnerXuidLow = (uint)steamId;
        weapon.OriginalOwnerXuidHigh = (uint)(steamId >> 32);
        weapon.FallbackPaintKitUpdated();
        weapon.FallbackSeedUpdated();
        weapon.FallbackWearUpdated();
        weapon.FallbackStatTrakUpdated();
        weapon.OriginalOwnerXuidLowUpdated();
        weapon.OriginalOwnerXuidHighUpdated();
        Paint(weapon.AttributeManager.Item, steamId, cosmetic, selection, knifeDefinition.HasValue, stickersEnabled, charmsEnabled);
        weapon.AttributeManager.ItemUpdated();
        weapon.AttributeManagerUpdated();
        if (!knifeDefinition.HasValue)
            weapon.SetBodygroupByName("body", cosmetic?.LegacyModel == true ? 1 : 0);
        else if (cosmetic is { Kit: > 0 })
            weapon.SetMeshGroupMask(ulong.MaxValue);
        weapon.SetBodygroupByName("stattrak", selection.StatTrak.HasValue ? 1 : 0);
    }
}
