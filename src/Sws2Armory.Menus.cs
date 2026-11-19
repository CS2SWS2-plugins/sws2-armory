using System.Globalization;
using SwiftlyS2.Shared.Players;
using Sws2Armory.Models;
using Sws2Armory.Services;
using Sws2Cs2Menus.Contracts;

namespace Sws2Armory;

public partial class Sws2Armory
{
    private static string KindKey(CosmeticKind kind) => $"menu.{kind.ToString().ToLowerInvariant()}";
    private string Text(IPlayer player, string key) => Core.Translation.GetPlayerLocalizer(player)[key];
    private bool IsChinese(IPlayer player) => Text(player, "language") == "zh";

    private IMenuBuilder? Menu(IPlayer player, string key, MenuLayout layout = MenuLayout.List)
    {
        if (!_loaded || !_settings.Enabled || !player.IsValid || Profile(player) is null) return null;
        if (_menus is not { IsAvailable: true }) { Reply(player, "error.menus"); return null; }
        if (!CustomHudReady) { Reply(player, "error.hud"); return null; }
        return _menus.CreateMenu(Text(player, key)).Id($"armory.{player.SteamID}.{Guid.NewGuid():N}")
            .Layout(layout).Comment(Text(player, "menu.help")).PageSize(5).ShowBack().ShowClose().CloseOnSelect(false);
    }

    private void Show(IPlayer player, IMenuBuilder builder, bool root = false, bool replaceCurrent = false)
    {
        // Recheck before opening: older providers and classic fallback are never
        // valid renderers for armory, including directly invoked subcommands.
        if (_menus is not { IsAvailable: true } || !CustomHudReady) { Reply(player, "error.hud"); return; }
        var ownedRoot = _menus.GetNavigationStack(player).FirstOrDefault()?.Id.StartsWith("armory.", StringComparison.Ordinal) == true;
        var result = root || !ownedRoot ? _menus.Open(player, builder.Build())
            : replaceCurrent ? _menus.ReplaceCurrent(player, builder.Build()) : _menus.OpenSubMenu(player, builder.Build());
        if (result != MenuOpenResult.Opened) Reply(player, "error.hud");
    }

    private void OpenMain(IPlayer player)
    {
        var menu = Menu(player, "menu.title", MenuLayout.Sidebar);
        if (menu is null) return;
        menu.ShowBack(false);
        foreach (var section in ArmoryHudNavigation.Sections(_settings))
        {
            var key = section switch
            {
                ArmoryHudSection.Weapons => "menu.weapon",
                ArmoryHudSection.Knives => "menu.knife",
                ArmoryHudSection.Gloves => "menu.glove",
                ArmoryHudSection.Player => "menu.player",
                _ => "menu.tools"
            };
            menu.AddOption(option => option.Id(section.ToString()).Text(Text(player, key))
                .Comment(Text(player, key + "_help"))
                .OnSelected(context =>
                {
                    switch (section)
                    {
                        case ArmoryHudSection.Player: OpenPlayerMenu(context.Player); break;
                        case ArmoryHudSection.Tools: OpenTools(context.Player); break;
                        default:
                            var kind = ArmoryHudNavigation.Kinds(section)[0];
                            OpenEntries(context.Player, _catalog.Search(kind).ToArray(), KindKey(kind));
                            break;
                    }
                    return ValueTask.CompletedTask;
                }));
        }
        Show(player, menu, root: true);
    }

    private void OpenPlayerMenu(IPlayer player)
    {
        var menu = Menu(player, "menu.player");
        if (menu is null) return;
        foreach (var kind in new[] { CosmeticKind.Agent, CosmeticKind.Music })
            if (_settings.Allows(kind))
                menu.AddOption(kind.ToString(), Text(player, KindKey(kind)), c =>
                {
                    OpenEntries(c.Player, _catalog.Search(kind).ToArray(), KindKey(kind));
                    return ValueTask.CompletedTask;
                });
        Show(player, menu);
    }

    private void OpenTools(IPlayer player)
    {
        var menu = Menu(player, "menu.tools");
        if (menu is null) return;
        menu.AddOption("customize", Text(player, "menu.customize"), c => { OpenCustomize(c.Player); return ValueTask.CompletedTask; });
        foreach (var kind in new[] { CosmeticKind.Sticker, CosmeticKind.Charm })
            if (_settings.Allows(CosmeticKind.Weapon) && _settings.Allows(kind))
                menu.AddOption(kind.ToString(), Text(player, KindKey(kind)), c =>
                {
                    var target = HeldTarget(c.Player);
                    if (target is null or "knife") Reply(c.Player, "error.no_weapon");
                    else if (kind == CosmeticKind.Sticker) OpenStickerSlots(c.Player, target);
                    else OpenCharmMenu(c.Player, target);
                    return ValueTask.CompletedTask;
                });
        menu.AddOption("refresh", Text(player, "menu.refresh"), c =>
        {
            if (CanMutate(c.Player)) Refresh(c.Player);
            return ValueTask.CompletedTask;
        });
        menu.AddOption(option => option.Id("reset").Text(Text(player, "menu.reset"))
            .Tag(new MenuOptionAppearance("", Destructive: true))
            .OnSelected(c => { OpenReset(c.Player); return ValueTask.CompletedTask; }));
        Show(player, menu);
    }

    private void OpenEntries(IPlayer player, CatalogEntry[] entries, string titleKey,
        string? attachmentTarget = null, int slot = 0, bool root = false, bool grouped = false, bool replaceCurrent = false)
    {
        entries = entries.Where(e => _settings.Allows(e.Kind)).ToArray();
        if (entries.Length == 0) { Reply(player, "error.not_found"); return; }
        var menu = Menu(player, titleKey);
        if (menu is null) return;
        if (!grouped && entries.Select(e => e.Group).Distinct().Skip(1).Any())
        {
            foreach (var group in entries.GroupBy(e => e.Group).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var subset = group.ToArray();
                menu.AddOption(option => option.Id(group.Key).Text(group.Key).Comment($"{Text(player, "menu.items")}: {subset.Length}")
                    .OnSelected(c =>
                    {
                        OpenEntries(c.Player, subset, titleKey, attachmentTarget, slot, grouped: true);
                        return ValueTask.CompletedTask;
                    }));
            }
        }
        else if (entries.Length > 100)
        {
            var offset = 0;
            foreach (var chunk in entries.Chunk(100))
            {
                var start = offset + 1;
                offset += chunk.Length;
                menu.AddOption($"range-{start}", $"{start}–{offset}", c =>
                {
                    OpenEntries(c.Player, chunk, titleKey, attachmentTarget, slot, grouped: true);
                    return ValueTask.CompletedTask;
                });
            }
        }
        else
            foreach (var entry in entries)
                menu.AddOption(option => option.Id(entry.Id).Text(entry.DisplayName(IsChinese(player)))
                    .Comment((IsSelected(player, entry, attachmentTarget, slot) ? Text(player, "menu.selected") + " · " : "") + EntryDetails(player, entry))
                    .OnSelected(c =>
                    {
                        Select(c.Player, entry, attachmentTarget, slot);
                        OpenEntries(c.Player, entries, titleKey, attachmentTarget, slot, grouped: grouped, replaceCurrent: true);
                        return ValueTask.CompletedTask;
                    }));
        Show(player, menu, root, replaceCurrent);
    }

    private string EntryDetails(IPlayer player, CatalogEntry entry)
    {
        var label = Text(player, "menu.choose");
        return entry.Kind switch
        {
            CosmeticKind.Weapon or CosmeticKind.Knife or CosmeticKind.Glove => $"{entry.Group} · Paint {entry.Kit} · {label}",
            CosmeticKind.Agent => $"{entry.Target.ToUpperInvariant()} · {label}",
            _ => $"{entry.Group} · {label}"
        };
    }

    private bool IsSelected(IPlayer player, CatalogEntry entry, string? target, int slot)
    {
        var profile = Profile(player)!;
        return entry.Kind switch
        {
            CosmeticKind.Agent => (entry.Target == "t" ? profile.TerroristAgent : profile.CounterTerroristAgent) == entry.Id,
            CosmeticKind.Music => profile.Music == entry.Id,
            CosmeticKind.Sticker => target is not null && profile.Items.GetValueOrDefault(target)?.Stickers.GetValueOrDefault(slot) == entry.Id,
            CosmeticKind.Charm => target is not null && profile.Items.GetValueOrDefault(target)?.Charm == entry.Id,
            _ => profile.Items.GetValueOrDefault(entry.Target)?.CosmeticId == entry.Id
        };
    }

    private void OpenStickerSlots(IPlayer player, string target, bool root = false)
    {
        var menu = Menu(player, "menu.sticker");
        if (menu is null) return;
        for (var index = 0; index < 5; index++)
        {
            var slot = index;
            var selected = _catalog.Find(Profile(player)!.Items.GetValueOrDefault(target)?.Stickers.GetValueOrDefault(slot));
            menu.AddOption(option => option.Id($"slot-{slot}").Text($"{Text(player, "menu.slot")} {slot}")
                .Comment(selected?.DisplayName(IsChinese(player)) ?? Text(player, "menu.empty"))
                .OnSelected(c =>
                {
                    var picker = Menu(c.Player, "menu.sticker");
                    if (picker is null) return ValueTask.CompletedTask;
                    picker.AddOption("choose", Text(c.Player, "menu.choose"), next =>
                    {
                        OpenEntries(next.Player, _catalog.Search(CosmeticKind.Sticker).ToArray(), "menu.sticker", target, slot);
                        return ValueTask.CompletedTask;
                    });
                    picker.AddOption("clear", Text(c.Player, "menu.clear"), next =>
                    {
                        ClearAttachment(next.Player, target, CosmeticKind.Sticker, slot);
                        return ValueTask.CompletedTask;
                    });
                    Show(c.Player, picker);
                    return ValueTask.CompletedTask;
                }));
        }
        Show(player, menu, root);
    }

    private void OpenCharmMenu(IPlayer player, string target)
    {
        var menu = Menu(player, "menu.charm");
        if (menu is null) return;
        menu.AddOption("choose", Text(player, "menu.choose"), c =>
        {
            OpenEntries(c.Player, _catalog.Search(CosmeticKind.Charm).ToArray(), "menu.charm", target);
            return ValueTask.CompletedTask;
        });
        menu.AddOption("clear", Text(player, "menu.clear"), c => { ClearAttachment(c.Player, target, CosmeticKind.Charm, 0); return ValueTask.CompletedTask; });
        Show(player, menu);
    }

    private void OpenCustomize(IPlayer player)
    {
        var menu = Menu(player, "menu.customize");
        if (menu is null) return;
        var target = HeldTarget(player);
        if (target is null || !_settings.Allows(target == "knife" ? CosmeticKind.Knife : CosmeticKind.Weapon)
            || !Profile(player)!.Items.TryGetValue(target, out var item) || _catalog.Find(item.CosmeticId) is null)
        { Reply(player, "error.no_skin"); return; }
        foreach (var wear in new[] { 0.0001f, 0.07f, 0.15f, 0.38f, 0.8f })
            menu.AddOption($"wear-{wear}", $"Float {wear.ToString(CultureInfo.InvariantCulture)}", c =>
            {
                if (CanMutate(c.Player) && Profile(c.Player)!.Items.TryGetValue(target, out var current))
                { current.Wear = wear; Changed(c.Player, target); Reply(c.Player, "saved"); }
                return ValueTask.CompletedTask;
            });
        menu.AddOption("seed", Text(player, "menu.seed"), c => { OpenSeeds(c.Player, target); return ValueTask.CompletedTask; });
        menu.AddOption("stattrak", $"StatTrak: {(item.StatTrak is { } count ? count.ToString(CultureInfo.InvariantCulture) : "OFF")}", c =>
        {
            if (CanMutate(c.Player) && Profile(c.Player)!.Items.TryGetValue(target, out var current))
            { current.StatTrak = current.StatTrak.HasValue ? null : 0; Changed(c.Player, target); Reply(c.Player, "saved"); }
            return ValueTask.CompletedTask;
        });
        Show(player, menu);
    }

    private void OpenSeeds(IPlayer player, string target)
    {
        var menu = Menu(player, "menu.seed");
        if (menu is null) return;
        menu.Comment(Text(player, "usage.seed"));
        foreach (var seed in new[] { 0, 1, 100, 420, 661, 1000 })
            menu.AddOption($"seed-{seed}", seed.ToString(CultureInfo.InvariantCulture), c =>
            {
                if (CanMutate(c.Player) && Profile(c.Player)!.Items.TryGetValue(target, out var current))
                { current.Seed = seed; Changed(c.Player, target); Reply(c.Player, "saved"); }
                return ValueTask.CompletedTask;
            });
        Show(player, menu);
    }

    private void OpenReset(IPlayer player)
    {
        var menu = Menu(player, "menu.confirm_reset", MenuLayout.Confirmation);
        if (menu is null) { Reply(player, "usage.reset"); return; }
        menu.AddOption(option => option.Id("confirm").Text(Text(player, "menu.confirm"))
            .Tag(new MenuOptionAppearance("", Destructive: true))
            .OnSelected(c => { Reset(c.Player); return ValueTask.CompletedTask; }));
        Show(player, menu);
    }
}
