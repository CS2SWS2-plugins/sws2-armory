using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using Sws2Armory.Configuration;
using Sws2Armory.Models;
using Sws2Armory.Services;
using Sws2Cs2Menus.Contracts;

namespace Sws2Armory;

public partial class Sws2Armory(ISwiftlyCore core) : BasePlugin(core)
{
    private ArmorySettings _settings = new();
    private CosmeticCatalog _catalog = null!;
    private LoadoutStore _store = null!;
    private EconApplicator _econ = null!;
    private ServiceProvider? _provider;
    private IDisposable? _subscription;
    private IMenuService? _menus;
    private Func<bool>? _customHudAvailable;
    private bool CustomHudReady => _menus?.IsAvailable == true && _customHudAvailable?.Invoke() == true;
    private CancellationTokenSource? _reconcile;
    private readonly List<Guid> _commands = [];
    private readonly List<Guid> _hooks = [];
    private readonly Dictionary<ulong, long> _revisions = [];
    private readonly Dictionary<ulong, long> _pawnRevisions = [];
    private readonly Dictionary<ulong, long> _cooldowns = [];
    private readonly HashSet<ulong> _failedProfiles = [];
    private readonly Dictionary<int, ulong> _slotSteamIds = [];
    private bool _loaded;

    public override void Load(bool hotReload)
    {
        _catalog = new CosmeticCatalog(Path.Combine(Core.PluginPath, "resources/catalog/items.json"));
        _econ = new EconApplicator(_catalog);
        _store = new LoadoutStore(Path.Combine(Core.PluginDataDirectory, "players"),
            ex => Core.Logger.LogError(ex, "[sws2-armory] Failed to save player preferences"));
        Core.Configuration.InitializeWithTemplate("config.jsonc", "config.template.jsonc")
            .Configure(builder => builder.AddJsonFile("config.jsonc", optional: false, reloadOnChange: true));
        var services = new ServiceCollection();
        services.AddSwiftly(Core).AddOptionsWithValidateOnStart<ArmorySettings>().BindConfiguration("sws2-armory");
        _provider = services.BuildServiceProvider();
        var options = _provider.GetRequiredService<IOptionsMonitor<ArmorySettings>>();
        _settings = options.CurrentValue;
        _subscription = options.OnChange(settings => Core.Scheduler.NextTick(() =>
        {
            if (!_loaded) return;
            var refreshWeapons = settings.Allows(CosmeticKind.Weapon) != _settings.Allows(CosmeticKind.Weapon)
                || settings.Allows(CosmeticKind.Knife) != _settings.Allows(CosmeticKind.Knife)
                || settings.Allows(CosmeticKind.Sticker) != _settings.Allows(CosmeticKind.Sticker)
                || settings.Allows(CosmeticKind.Charm) != _settings.Allows(CosmeticKind.Charm);
            _settings = settings;
            foreach (var player in Core.PlayerManager.GetAllValidPlayers())
            {
                Invalidate(player.SteamID);
                if (refreshWeapons) ReconcilePlayer(player, refreshWeapons: true);
            }
            CloseOwnedMenus();
        }));
        Register("armory", OnArmory);
        Register("ws", OnArmory);
        Register("knife", c => OpenCategoryCommand(c, CosmeticKind.Knife));
        Register("gloves", c => OpenCategoryCommand(c, CosmeticKind.Glove));
        Register("agents", c => OpenCategoryCommand(c, CosmeticKind.Agent));
        Register("music", c => OpenCategoryCommand(c, CosmeticKind.Music));
        Register("stickers", c => OpenAttachmentCommand(c, CosmeticKind.Sticker));
        Register("charms", c => OpenAttachmentCommand(c, CosmeticKind.Charm));
        Register("wear", OnWear);
        Register("seed", OnSeed);
        Register("stattrak", OnStatTrak);
        Register("nametag", OnNameTag);
        Register("armoryrefresh", OnRefresh);
        Register("armoryreset", OnReset);
        Register("armorystatus", OnStatus);
        _hooks.Add(Core.GameEvent.HookPost<EventPlayerSpawn>(OnSpawn));
        _hooks.Add(Core.GameEvent.HookPost<EventItemPickup>(OnPickup));
        _hooks.Add(Core.GameEvent.HookPost<EventPlayerDeath>(OnDeath));
        _hooks.Add(Core.GameEvent.HookPre<EventRoundMvp>(OnMvp));
        Core.GameHooks.Controller.ProcessUsercmds.Post += OnProcessUsercmds;
        Core.Event.OnPrecacheResource += OnPrecache;
        Core.Event.OnMapUnload += OnMapUnload;
        Core.Event.OnClientDisconnected += OnDisconnected;
        _loaded = true;
        _reconcile = Core.Scheduler.RepeatBySeconds(0.5f, Reconcile);
        Core.Logger.LogInformation("[sws2-armory] Loaded {Count} cosmetics; hot reload: {HotReload}", _catalog.Entries.Count, hotReload);
    }

    public override void UseSharedInterface(IInterfaceManager interfaces)
    {
        CloseOwnedMenus();
        _menus = interfaces.TryGetSharedInterface<IMenuService>(MenuServiceConstants.InterfaceKey, out var service) ? service : null;
        _customHudAvailable = interfaces.TryGetSharedInterface<Func<bool>>(MenuServiceConstants.CustomHudInterfaceKey, out var ready) ? ready : null;
    }

    public override void Unload()
    {
        _loaded = false;
        CloseOwnedMenus();
        _customHudAvailable = null;
        _menus = null;
        _reconcile?.Cancel();
        _reconcile?.Dispose();
        _subscription?.Dispose();
        foreach (var id in _commands) Core.Command.UnregisterCommand(id);
        foreach (var id in _hooks) Core.GameEvent.Unhook(id);
        Core.GameHooks.Controller.ProcessUsercmds.Post -= OnProcessUsercmds;
        Core.Event.OnPrecacheResource -= OnPrecache;
        Core.Event.OnMapUnload -= OnMapUnload;
        Core.Event.OnClientDisconnected -= OnDisconnected;
        _store.Flush();
        _provider?.Dispose();
        ClearRuntime();
    }

    private void Register(string command, ICommandService.CommandListener handler) =>
        _commands.Add(Core.Command.RegisterCommand(command, handler, registerRaw: true, permission: "", helpText: "SWS2 Armory"));

    private PlayerLoadout? Profile(IPlayer player)
    {
        if (!player.IsValid || player.IsFakeClient || player.SteamID == 0 || _failedProfiles.Contains(player.SteamID)) return null;
        _slotSteamIds[player.PlayerID] = player.SteamID;
        try { return _store.Get(player.SteamID); }
        catch (Exception exception)
        {
            _failedProfiles.Add(player.SteamID);
            Core.Logger.LogError(exception, "[sws2-armory] Cannot load preferences for {SteamId}; preserving original file", player.SteamID);
            Reply(player, "error.storage");
            return null;
        }
    }

    private bool TryPlayer(ICommandContext context, out IPlayer player, bool cooldown = false)
    {
        player = context.Sender!;
        if (!context.IsSentByPlayer || player is not { IsValid: true, IsFakeClient: false } || player.SteamID == 0)
        {
            context.Reply("This command requires an authenticated player.");
            return false;
        }
        if (!_settings.Enabled) { Reply(player, "error.disabled"); return false; }
        if (Profile(player) is null) return false;
        if (cooldown)
        {
            var now = Environment.TickCount64;
            if (_cooldowns.GetValueOrDefault(player.SteamID) > now) { Reply(player, "error.cooldown"); return false; }
            var seconds = float.IsFinite(_settings.CommandCooldownSeconds) ? Math.Clamp(_settings.CommandCooldownSeconds, 0, 30) : 1;
            _cooldowns[player.SteamID] = now + (long)(seconds * 1000);
        }
        return true;
    }

    private void Changed(IPlayer player, string? target = null, bool refreshWeapons = false, bool refreshPawn = false)
    {
        _store.Save(player.SteamID);
        Invalidate(player.SteamID, refreshPawn || target == "glove");
        ReconcilePlayer(player, refreshWeapons || target is not (null or "glove"), target);
    }

    private void Refresh(IPlayer player)
    {
        _runtimeFailures.Remove(player.SteamID);
        Invalidate(player.SteamID);
        ReconcilePlayer(player, refreshWeapons: true);
        Reply(player, "refreshed");
    }

    private void Invalidate(ulong steamId, bool refreshPawn = true)
    {
        _revisions[steamId] = _revisions.GetValueOrDefault(steamId) + 1;
        if (refreshPawn) _pawnRevisions[steamId] = _pawnRevisions.GetValueOrDefault(steamId) + 1;
    }
    private void Reply(IPlayer player, string key, params object[] args)
    {
        var localizer = Core.Translation.GetPlayerLocalizer(player);
        player.SendChat($"{localizer["prefix"]} {(args.Length == 0 ? localizer[key] : localizer[key, args])}");
    }

    private void CloseOwnedMenus()
    {
        if (_menus is null) return;
        foreach (var player in Core.PlayerManager.GetAllValidPlayers())
            if (_menus.GetNavigationStack(player).Any(menu => menu.Id.StartsWith("armory.", StringComparison.Ordinal))) _menus.Close(player);
    }
}
