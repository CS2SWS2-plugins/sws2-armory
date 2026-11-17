using SwiftlyS2.Shared.Players;
using Sws2Armory.Models;
using Sws2Cs2Menus.Contracts;

namespace Sws2Armory;

public partial class Sws2Armory
{
    private static string KindKey(CosmeticKind kind) => $"menu.{kind.ToString().ToLowerInvariant()}";
    private string Text(IPlayer player, string key) => Core.Translation.GetPlayerLocalizer(player)[key];
    private bool IsChinese(IPlayer player) => Text(player, "language") == "zh";

    private IMenuBuilder? Menu(IPlayer player, string key)
    {
        if (!_loaded || !_settings.Enabled || !player.IsValid || Profile(player) is null) return null;
        if (_menus is not { IsAvailable: true }) { Reply(player, "error.menus"); return null; }
        return _menus.CreateMenu(Text(player, key)).Id($"armory.{player.SteamID}.{Guid.NewGuid():N}")
            .Comment(Text(player, "menu.help")).PageSize(5).ShowBack().ShowClose().CloseOnSelect(false);
    }

    private void Show(IPlayer player, IMenuBuilder builder, bool root)
    {
        var result = root ? _menus!.Open(player, builder.Build()) : _menus!.OpenSubMenu(player, builder.Build());
        if (result != MenuOpenResult.Opened) Reply(player, "error.menus");
    }

    private void OpenMain(IPlayer player)
    {
        var menu = Menu(player, "menu.title");
        if (menu is null) return;
        foreach (var kind in Enum.GetValues<CosmeticKind>())
        {
            if (!_settings.Allows(kind)) continue;
            menu.AddOption(kind.ToString(), Text(player, KindKey(kind)), context =>
            {
                if (kind is CosmeticKind.Sticker or CosmeticKind.Charm)
                {
                    var target = HeldTarget(context.Player);
                    if (target is null or "knife") Reply(context.Player, "error.no_weapon");
                    else if (kind == CosmeticKind.Sticker) OpenStickerSlots(context.Player, target);
                    else OpenEntries(context.Player, _catalog.Search(kind).ToArray(), KindKey(kind), target);
                }
                else OpenEntries(context.Player, _catalog.Search(kind).ToArray(), KindKey(kind));
                return ValueTask.CompletedTask;
            });
        }
        menu.AddOption("customize", Text(player, "menu.customize"), c => { OpenCustomize(c.Player); return ValueTask.CompletedTask; });
        menu.AddOption("reset", Text(player, "menu.reset"), c => { OpenReset(c.Player); return ValueTask.CompletedTask; });
        Show(player, menu, root: true);
    }

    private void OpenEntries(IPlayer player, CatalogEntry[] entries, string titleKey,
        string? attachmentTarget = null, int slot = 0, bool root = false, bool grouped = false)
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
                menu.AddOption(group.Key, $"{group.Key} ({subset.Length})", c =>
                {
                    OpenEntries(c.Player, subset, titleKey, attachmentTarget, slot, grouped: true);
                    return ValueTask.CompletedTask;
                });
            }
        }
        else if (entries.Length > 100)
        {
            // Bound option count even for 11k tournament stickers. Groups and
            // range menus are constructed only when the player opens them.
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
                menu.AddOption(entry.Id, entry.DisplayName(IsChinese(player)), c =>
                {
                    Select(c.Player, entry, attachmentTarget, slot);
                    return ValueTask.CompletedTask;
                });
        Show(player, menu, root);
    }

    private void OpenStickerSlots(IPlayer player, string target, bool root = false)
    {
        var menu = Menu(player, "menu.sticker");
        if (menu is null) return;
        for (var index = 0; index < 5; index++)
        {
            var slot = index;
            menu.AddOption($"slot-{slot}", $"{Text(player, "menu.slot")} {slot}", c =>
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
                Show(c.Player, picker, root: false);
                return ValueTask.CompletedTask;
            });
        }
        Show(player, menu, root);
    }

    private void OpenCustomize(IPlayer player)
    {
        var menu = Menu(player, "menu.customize");
        if (menu is null) return;
        var target = HeldTarget(player);
        if (target is null || !Profile(player)!.Items.TryGetValue(target, out var item) || _catalog.Find(item.CosmeticId) is null)
        { Reply(player, "error.no_skin"); return; }
        foreach (var wear in new[] { 0.0001f, 0.07f, 0.15f, 0.38f, 0.8f })
            menu.AddOption($"wear-{wear}", $"Float {wear.ToString(System.Globalization.CultureInfo.InvariantCulture)}", c =>
            {
                if (CanMutate(c.Player)) { item.Wear = wear; Changed(c.Player); Reply(c.Player, "saved"); }
                return ValueTask.CompletedTask;
            });
        menu.AddOption("stattrak", "StatTrak ON / OFF", c =>
        {
            if (CanMutate(c.Player)) { item.StatTrak = item.StatTrak.HasValue ? null : 0; Changed(c.Player); Reply(c.Player, "saved"); }
            return ValueTask.CompletedTask;
        });
        Show(player, menu, root: false);
    }

    private void OpenReset(IPlayer player)
    {
        var menu = Menu(player, "menu.confirm_reset");
        if (menu is null) { Reply(player, "usage.reset"); return; }
        menu.AddOption("confirm", Text(player, "menu.confirm"), c => { Reset(c.Player); return ValueTask.CompletedTask; });
        Show(player, menu, root: false);
    }
}
