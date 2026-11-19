using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using Sws2Armory.Models;
using Sws2Armory.Services;

namespace Sws2Armory;

public partial class Sws2Armory
{
    private void OnArmory(ICommandContext context)
    {
        if (!TryPlayer(context, out var player)) return;
        if (context.Args is ["equip", var id])
        {
            if (_catalog.Find(id) is { Kind: not (CosmeticKind.Sticker or CosmeticKind.Charm) } entry) Select(player, entry);
            else Reply(player, "error.not_found");
            return;
        }
        if (context.Args.Length > 0)
        {
            var query = string.Join(' ', context.Args).Trim();
            OpenEntries(player, _catalog.Entries.Where(e => _settings.Allows(e.Kind)
                && e.Kind is not (CosmeticKind.Sticker or CosmeticKind.Charm)
                && (e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (e.NameZh?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || e.Group.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || e.Id.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray(), "menu.search", root: true);
        }
        else OpenMain(player);
    }

    private void OpenCategoryCommand(ICommandContext context, CosmeticKind kind)
    {
        if (!TryPlayer(context, out var player)) return;
        OpenEntries(player, _catalog.Search(kind, string.Join(' ', context.Args)).ToArray(), KindKey(kind), root: true);
    }

    private string? HeldTarget(IPlayer player)
    {
        var weapon = player.PlayerPawn?.WeaponServices?.ActiveWeapon.Value;
        if (weapon is not { IsValid: true }) return null;
        var definition = weapon.AttributeManager.Item.ItemDefinitionIndex;
        return CosmeticCatalog.IsKnife(definition) ? "knife" : _catalog.WeaponTarget(definition);
    }

    private bool TryCustomization(ICommandContext context, out IPlayer player, out ItemSelection selection, out string? target)
    {
        selection = null!;
        target = null;
        if (!TryPlayer(context, out player, cooldown: true)) return false;
        target = context.Args.Length > 1 && context.Args[^1] is "knife" or "glove" ? context.Args[^1] : HeldTarget(player);
        if (target is null || !_settings.Allows(target switch
        {
            "knife" => CosmeticKind.Knife,
            "glove" => CosmeticKind.Glove,
            _ => CosmeticKind.Weapon
        })) { Reply(player, "error.no_weapon"); return false; }
        var profile = Profile(player)!;
        if (!profile.Items.TryGetValue(target, out selection!) || _catalog.Find(selection.CosmeticId) is null)
        {
            Reply(player, "error.no_skin");
            return false;
        }
        return true;
    }

    private void OnWear(ICommandContext context)
    {
        if (!TryCustomization(context, out var player, out var item, out var target)) return;
        if (context.Args.Length < 1 || !CustomizationInput.TryWear(context.Args[0], out var value)) { Reply(player, "usage.wear"); return; }
        item.Wear = value;
        Changed(player, target);
        Reply(player, "saved");
    }

    private void OnSeed(ICommandContext context)
    {
        if (!TryCustomization(context, out var player, out var item, out var target)) return;
        if (context.Args.Length < 1 || !CustomizationInput.TrySeed(context.Args[0], out var value)) { Reply(player, "usage.seed"); return; }
        item.Seed = value;
        Changed(player, target);
        Reply(player, "saved");
    }

    private void OnStatTrak(ICommandContext context)
    {
        if (!TryCustomization(context, out var player, out var item, out var target)) return;
        if (context.Args.LastOrDefault() == "glove") { Reply(player, "usage.stattrak"); return; }
        var token = context.Args.FirstOrDefault() ?? (item.StatTrak is null ? "on" : "off");
        if (token == "off") item.StatTrak = null;
        else if (token == "on") item.StatTrak ??= 0;
        else if (CustomizationInput.TryCounter(token, out var count)) item.StatTrak = count;
        else { Reply(player, "usage.stattrak"); return; }
        Changed(player, target);
        Reply(player, "saved");
    }

    private void OnNameTag(ICommandContext context)
    {
        if (!TryCustomization(context, out var player, out var item, out var target)) return;
        var text = string.Join(' ', context.Args).Trim();
        if (text == "clear") text = "";
        if (!CustomizationInput.ValidName(text)) { Reply(player, "usage.nametag"); return; }
        item.NameTag = text;
        Changed(player, target);
        Reply(player, "saved");
    }

    private void OpenAttachmentCommand(ICommandContext context, CosmeticKind kind)
    {
        if (!TryPlayer(context, out var player)) return;
        var target = HeldTarget(player);
        if (target is null or "knife" || !_settings.Allows(CosmeticKind.Weapon) || !_settings.Allows(kind))
        { Reply(player, "error.no_weapon"); return; }
        if (kind == CosmeticKind.Sticker && context.Args.Length == 0) { OpenStickerSlots(player, target, root: true); return; }
        var slot = 0;
        var query = string.Join(' ', context.Args);
        if (kind == CosmeticKind.Sticker)
        {
            if (!int.TryParse(context.Args.FirstOrDefault(), out slot) || slot is < 0 or > 4) { Reply(player, "usage.stickers"); return; }
            query = string.Join(' ', context.Args.Skip(1));
        }
        if (query == "clear") { ClearAttachment(player, target, kind, slot); return; }
        if (_catalog.Find(query) is { } exact && exact.Kind == kind) { Select(player, exact, target, slot); return; }
        OpenEntries(player, _catalog.Search(kind, query).ToArray(), KindKey(kind), target, slot, root: true);
    }

    private void OnRefresh(ICommandContext context)
    {
        if (!TryPlayer(context, out var player, cooldown: true)) return;
        Refresh(player);
    }

    private void OnStatus(ICommandContext context)
    {
        context.Reply($"sws2-armory {VersionInfo.Version}; enabled={_settings.Enabled}; catalog={_catalog.Entries.Count}; menus={_menus?.IsAvailable == true}; customHud={CustomHudReady}; precachedAgents={_precached.Count}");
        if (context.Sender is { IsValid: true, IsFakeClient: false, SteamID: > 0 } player)
            LogHeldWeaponStatus(player);
    }

    private void OnReset(ICommandContext context)
    {
        if (!TryPlayer(context, out var player)) return;
        if (context.Args is ["confirm"])
        {
            Reset(player);
            return;
        }
        OpenReset(player);
    }

    private void Reset(IPlayer player)
    {
        if (!CanMutate(player)) return;
        if (_menus?.GetNavigationStack(player).Any(menu => menu.Id.StartsWith("armory.", StringComparison.Ordinal)) == true) _menus.Close(player);
        _store.Reset(player.SteamID);
        Changed(player, refreshWeapons: true, refreshPawn: true);
        Reply(player, "reset");
    }

    private bool CanMutate(IPlayer player)
    {
        if (!_loaded || !_settings.Enabled || Profile(player) is null) return false;
        var now = Environment.TickCount64;
        if (_cooldowns.GetValueOrDefault(player.SteamID) > now) { Reply(player, "error.cooldown"); return false; }
        var seconds = float.IsFinite(_settings.CommandCooldownSeconds) ? Math.Clamp(_settings.CommandCooldownSeconds, 0, 30) : 1;
        _cooldowns[player.SteamID] = now + (long)(seconds * 1000);
        return true;
    }

    private bool Select(IPlayer player, CatalogEntry entry, string? attachmentTarget = null, int slot = 0)
    {
        if (!_settings.Allows(entry.Kind) || !CanMutate(player)) return false;
        var profile = Profile(player)!;
        switch (entry.Kind)
        {
            case CosmeticKind.Agent:
                if (entry.Target == "t") profile.TerroristAgent = entry.Id;
                else profile.CounterTerroristAgent = entry.Id;
                if (!_precached.Contains(entry.Model!)) Reply(player, "agent.next_map");
                break;
            case CosmeticKind.Music:
                if (profile.Music != entry.Id) profile.MusicMvps = 0;
                profile.Music = entry.Id;
                break;
            case CosmeticKind.Sticker:
            case CosmeticKind.Charm:
                if (attachmentTarget is null || !_catalog.Search(CosmeticKind.Weapon, target: attachmentTarget).Any()
                    || !_settings.Allows(CosmeticKind.Weapon) || slot is < 0 or > 4) return false;
                var attached = GetItem(profile, attachmentTarget);
                if (entry.Kind == CosmeticKind.Sticker) attached.Stickers[slot] = entry.Id;
                else attached.Charm = entry.Id;
                break;
            default:
                GetItem(profile, entry.Target).CosmeticId = entry.Id;
                break;
        }
        Changed(player, entry.Kind switch
        {
            CosmeticKind.Weapon or CosmeticKind.Knife => entry.Target,
            CosmeticKind.Sticker or CosmeticKind.Charm => attachmentTarget,
            _ => null
        }, refreshPawn: entry.Kind is CosmeticKind.Agent or CosmeticKind.Glove);
        Reply(player, "selected", entry.DisplayName(IsChinese(player)));
        return true;
    }

    private static ItemSelection GetItem(PlayerLoadout profile, string target)
    {
        if (!profile.Items.TryGetValue(target, out var item)) profile.Items[target] = item = new ItemSelection();
        return item;
    }

    private void ClearAttachment(IPlayer player, string target, CosmeticKind kind, int slot)
    {
        if (!_settings.Allows(kind) || !CanMutate(player)) return;
        var item = GetItem(Profile(player)!, target);
        if (kind == CosmeticKind.Sticker) item.Stickers.Remove(slot);
        else item.Charm = null;
        Changed(player, target);
        Reply(player, "saved");
    }
}
