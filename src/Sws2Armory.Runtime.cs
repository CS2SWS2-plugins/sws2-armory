using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using Sws2Armory.Models;
using Sws2Armory.Services;

namespace Sws2Armory;

public partial class Sws2Armory
{
    private readonly record struct Stamp(ulong SteamId, long Revision, byte Team);
    private readonly Dictionary<uint, Stamp> _weaponsApplied = [];
    private readonly Dictionary<uint, Stamp> _pawnsApplied = [];
    private readonly Dictionary<uint, (string Model, ushort Character, string Voice, bool Female)> _originalAgents = [];
    private readonly Dictionary<ulong, (int Kit, int Mvps, bool NoMusic, ushort Inventory)> _originalMusic = [];
    private readonly HashSet<uint> _glovesApplied = [];
    private readonly HashSet<string> _precached = new(StringComparer.Ordinal);
    private readonly HashSet<ulong> _runtimeFailures = [];

    private void Reconcile()
    {
        if (!_loaded) return;
        foreach (var player in Core.PlayerManager.GetAllValidPlayers()) ReconcilePlayer(player);
        // Entity handles include serial numbers. Periodic pruning bounds state
        // while retaining dropped weapon appearance until another player owns it.
        if (_weaponsApplied.Count > 1024)
        {
            var live = Core.EntitySystem.GetAllEntities().Where(entity => entity.IsValid && entity.DesignerName.StartsWith("weapon_", StringComparison.Ordinal))
                .Select(w => w.Entity!.EntityHandle.Raw).ToHashSet();
            foreach (var raw in _weaponsApplied.Keys.Where(raw => !live.Contains(raw)).ToArray()) _weaponsApplied.Remove(raw);
        }
    }

    private void ReconcilePlayer(IPlayer player)
    {
        if (!_loaded || !player.IsValid || player.IsFakeClient || player.SteamID == 0
            || _runtimeFailures.Contains(player.SteamID) || player.PlayerPawn is not { IsValid: true } pawn
            || pawn.TeamNum is not (2 or 3) || pawn.LifeState != 0) return;
        var profile = Profile(player);
        if (profile is null) return;
        try
        {
            var stamp = new Stamp(player.SteamID, _revisions.GetValueOrDefault(player.SteamID), pawn.TeamNum);
            var weapons = pawn.WeaponServices;
            if (weapons is not null)
                foreach (var handle in weapons.MyWeapons)
                    if (handle.Value is { IsValid: true } weapon)
                    {
                        var raw = handle.Raw;
                        if (_weaponsApplied.TryGetValue(raw, out var applied) && applied == stamp) continue;
                        var definition = weapon.AttributeManager.Item.ItemDefinitionIndex;
                        var knife = CosmeticCatalog.IsKnife(definition);
                        var target = knife ? "knife" : _catalog.WeaponTarget(definition);
                        if (target is null) continue;
                        profile.Items.TryGetValue(target, out var selection);
                        var kind = knife ? CosmeticKind.Knife : CosmeticKind.Weapon;
                        var cosmetic = _catalog.Find(selection?.CosmeticId);
                        if (cosmetic is not null && (cosmetic.Kind != kind || cosmetic.Target != target)) cosmetic = null;
                        if (!_settings.Allows(kind)) { selection = null; cosmetic = null; }
                        // Never alter an unselected item unless this plugin
                        // previously painted this exact entity (full serial).
                        if (selection is not null || _weaponsApplied.ContainsKey(raw))
                        {
                            if (selection is null || cosmetic is null && knife)
                            {
                                cosmetic = null;
                                if (knife) weapon.AcceptInput("ChangeSubclass", pawn.TeamNum == 2 ? "59" : "42");
                            }
                            _econ.Weapon(weapon, player.SteamID, cosmetic, selection ?? new ItemSelection(), knife,
                                _settings.Allows(CosmeticKind.Sticker), _settings.Allows(CosmeticKind.Charm));
                            _weaponsApplied[raw] = stamp;
                        }
                    }
            if (!_pawnsApplied.TryGetValue(pawn.Entity!.EntityHandle.Raw, out var pawnStamp) || pawnStamp != stamp)
            {
                ApplyPawn(player, pawn, profile);
                _pawnsApplied[pawn.Entity!.EntityHandle.Raw] = stamp;
            }
            ApplyMusic(player, profile);
        }
        catch (Exception exception)
        {
            _runtimeFailures.Add(player.SteamID);
            Core.Logger.LogError(exception, "[sws2-armory] Cosmetic update failed for {SteamId}; retry with !armoryrefresh or respawn", player.SteamID);
            Reply(player, "error.apply");
        }
    }

    private void ApplyPawn(IPlayer player, CCSPlayerPawn pawn, PlayerLoadout profile)
    {
        var raw = pawn.Entity!.EntityHandle.Raw;
        var team = pawn.TeamNum == 2 ? "t" : "ct";
        var agent = _catalog.Find(team == "t" ? profile.TerroristAgent : profile.CounterTerroristAgent);
        if (_settings.Allows(CosmeticKind.Agent) && agent is { Kind: CosmeticKind.Agent } && agent.Target == team && _precached.Contains(agent.Model!))
        {
            if (!_originalAgents.ContainsKey(raw))
                _originalAgents[raw] = (pawn.GetSkeletonInstance()?.ModelState.ModelName ?? "", pawn.CharacterDefIndex, pawn.StrVOPrefix, pawn.HasFemaleVoice);
            pawn.SetModel(agent.Model!);
            pawn.CharacterDefIndex = agent.DefinitionIndex;
            pawn.StrVOPrefix = agent.VoicePrefix ?? "";
            pawn.HasFemaleVoice = agent.FemaleVoice;
            pawn.HasFemaleVoiceUpdated();
        }
        else if (_originalAgents.Remove(raw, out var original))
        {
            if (original.Model.Length > 0) pawn.SetModel(original.Model);
            pawn.CharacterDefIndex = original.Character;
            pawn.StrVOPrefix = original.Voice;
            pawn.HasFemaleVoice = original.Female;
            pawn.HasFemaleVoiceUpdated();
        }
        profile.Items.TryGetValue("glove", out var selection);
        var glove = _catalog.Find(selection?.CosmeticId);
        if (_settings.Allows(CosmeticKind.Glove) && glove is { Kind: CosmeticKind.Glove } && selection is not null)
        {
            _econ.Paint(pawn.EconGloves, player.SteamID, glove, selection, false, false, false);
            pawn.EconGloves.EntityQuality = 3;
            pawn.EconGloves.EntityQualityUpdated();
            _glovesApplied.Add(raw);
            RefreshGloves(player, pawn, true);
        }
        else if (_glovesApplied.Remove(raw))
        {
            pawn.EconGloves.ItemDefinitionIndex = 0;
            pawn.EconGloves.AttributeList.Attributes.RemoveAll();
            pawn.EconGloves.NetworkedDynamicAttributes.Attributes.RemoveAll();
            pawn.EconGloves.ItemDefinitionIndexUpdated();
            pawn.EconGloves.AttributeListUpdated();
            pawn.EconGloves.NetworkedDynamicAttributesUpdated();
            RefreshGloves(player, pawn, false);
        }
    }

    private void RefreshGloves(IPlayer player, CCSPlayerPawn pawn, bool hideDefaultGloves)
    {
        unchecked { pawn.EconGlovesChanged++; }
        pawn.EconGlovesChangedUpdated();
        pawn.EconGlovesUpdated();
        pawn.SetBodygroupByName("first_or_third_person", hideDefaultGloves ? 0 : 1);
        var slot = player.PlayerID;
        var session = player.SessionId;
        var raw = pawn.Entity!.EntityHandle.Raw;
        var revision = _revisions.GetValueOrDefault(player.SteamID);
        Core.Scheduler.NextTick(() =>
        {
            if (_loaded && Core.PlayerManager.GetPlayer(slot) is { IsValid: true } current && current.SessionId == session
                && _revisions.GetValueOrDefault(current.SteamID) == revision
                && current.PlayerPawn is { IsValid: true } livePawn && livePawn.Entity!.EntityHandle.Raw == raw)
                livePawn.SetBodygroupByName("first_or_third_person", hideDefaultGloves ? 1 : 0);
        });
    }

    private void RefreshView(IPlayer player)
    {
        if (_runtimeFailures.Contains(player.SteamID) || player.PlayerPawn?.WeaponServices?.ActiveWeapon.Value is not { IsValid: true } active) return;
        var definition = active.AttributeManager.Item.ItemDefinitionIndex;
        var knife = CosmeticCatalog.IsKnife(definition);
        if (!knife && _catalog.WeaponTarget(definition) is null) return;
        var select = knife ? "slot3" : definition is 1 or 2 or 3 or 4 or 30 or 32 or 36 or 61 or 63 or 64 ? "slot2" : "slot1";
        var slot = player.PlayerID;
        var session = player.SessionId;
        var raw = active.Entity!.EntityHandle.Raw;
        var revision = _revisions.GetValueOrDefault(player.SteamID);
        Core.Scheduler.NextTick(() =>
        {
            if (!_loaded || Core.PlayerManager.GetPlayer(slot) is not { IsValid: true } current || current.SessionId != session
                || current.PlayerPawn?.WeaponServices?.ActiveWeapon.Raw != raw
                || _revisions.GetValueOrDefault(current.SteamID) != revision) return;
            current.ExecuteCommand(knife ? "lastinv" : "slot3");
            Core.Scheduler.NextTick(() =>
            {
                if (_loaded && Core.PlayerManager.GetPlayer(slot) is { IsValid: true } next && next.SessionId == session
                    && next.PlayerPawn?.WeaponServices?.MyWeapons.Any(handle => handle.Raw == raw) == true
                    && _revisions.GetValueOrDefault(next.SteamID) == revision) next.ExecuteCommand(select);
            });
        });
    }

    private void ApplyMusic(IPlayer player, PlayerLoadout profile)
    {
        var controller = player.Controller;
        var inventory = controller.InventoryServices;
        var kit = _settings.Allows(CosmeticKind.Music) ? _catalog.Find(profile.Music) : null;
        if (kit is not { Kind: CosmeticKind.Music })
        {
            if (!_originalMusic.Remove(player.SteamID, out var original)) return;
            controller.MusicKitID = original.Kit;
            controller.MusicKitMVPs = original.Mvps;
            controller.MvpNoMusic = original.NoMusic;
            if (inventory is not null) { inventory.MusicID = original.Inventory; inventory.MusicIDUpdated(); }
        }
        else
        {
            _originalMusic.TryAdd(player.SteamID, (controller.MusicKitID, controller.MusicKitMVPs, controller.MvpNoMusic, inventory?.MusicID ?? 0));
            if (controller.MusicKitID == kit.Kit && controller.MusicKitMVPs == profile.MusicMvps
                && !controller.MvpNoMusic && (inventory is null || inventory.MusicID == kit.Kit)) return;
            controller.MusicKitID = kit.Kit;
            controller.MusicKitMVPs = profile.MusicMvps;
            controller.MvpNoMusic = false;
            if (inventory is not null) { inventory.MusicID = checked((ushort)kit.Kit); inventory.MusicIDUpdated(); }
        }
        controller.MusicKitIDUpdated();
        controller.MusicKitMVPsUpdated();
        controller.MvpNoMusicUpdated();
    }

    private void OnPrecache(IOnPrecacheResourceEvent @event)
    {
        foreach (var model in _catalog.Entries.Where(e => e.Kind == CosmeticKind.Agent).Select(e => e.Model!).Distinct())
        {
            @event.AddItem(model);
            _precached.Add(model);
        }
    }

    private HookResult OnSpawn(EventPlayerSpawn @event)
    {
        if (@event.UserIdPlayer is { } player)
        {
            Invalidate(player.SteamID);
            _runtimeFailures.Remove(player.SteamID);
            if (player.PlayerPawn is { } pawn) _originalAgents.Remove(pawn.Entity!.EntityHandle.Raw);
            // Re-resolve by session; never retain native pawn/weapon wrappers.
            var slot = player.PlayerID;
            var session = player.SessionId;
            Core.Scheduler.DelayBySeconds(0.2f, () =>
            {
                if (_loaded && Core.PlayerManager.GetPlayer(slot) is { } current && current.SessionId == session) ReconcilePlayer(current);
            });
        }
        return HookResult.Continue;
    }

    private HookResult OnPickup(EventItemPickup @event)
    {
        if (@event.UserIdPlayer is { } player)
        {
            var slot = player.PlayerID;
            var session = player.SessionId;
            Core.Scheduler.DelayBySeconds(0.2f, () =>
            {
                if (_loaded && Core.PlayerManager.GetPlayer(slot) is { } current && current.SessionId == session)
                {
                    // Let GiveNamedItem finish; it can reset paint/counters
                    // after the immediate pickup frame or periodic scan.
                    if (current.PlayerPawn?.WeaponServices is { } services)
                        foreach (var handle in services.MyWeapons)
                            if (_weaponsApplied.TryGetValue(handle.Raw, out var stamp))
                                _weaponsApplied[handle.Raw] = stamp with { Revision = -1 };
                    ReconcilePlayer(current);
                }
            });
        }
        return HookResult.Continue;
    }

    private HookResult OnDeath(EventPlayerDeath @event)
    {
        var player = @event.AttackerPlayer;
        if (!_loaded || !_settings.Enabled || player is not { IsValid: true, IsFakeClient: false }
            || @event.UserIdPlayer is not { } victim || victim.SteamID == player.SteamID
            || victim.Controller.TeamNum == player.Controller.TeamNum) return HookResult.Continue;
        // Use the event weapon, not the gun held when a delayed grenade kills.
        var eventWeapon = @event.Weapon.StartsWith("weapon_", StringComparison.Ordinal) ? @event.Weapon : "weapon_" + @event.Weapon;
        var target = eventWeapon.Contains("knife", StringComparison.Ordinal) || eventWeapon == "weapon_bayonet" ? "knife" : eventWeapon;
        var kind = target == "knife" ? CosmeticKind.Knife : CosmeticKind.Weapon;
        if (_settings.Allows(kind) && Profile(player) is { } profile && profile.Items.TryGetValue(target, out var selection)
            && selection.StatTrak is { } count)
        {
            selection.StatTrak = Math.Min(999999, count + 1);
            _store.Save(player.SteamID);
            try
            {
                if (player.PlayerPawn?.WeaponServices is { } weapons)
                    foreach (var handle in weapons.MyWeapons)
                        if (handle.Value is { IsValid: true } weapon)
                        {
                            var definition = weapon.AttributeManager.Item.ItemDefinitionIndex;
                            var weaponTarget = CosmeticCatalog.IsKnife(definition) ? "knife" : _catalog.WeaponTarget(definition);
                            if (weaponTarget == target) EconApplicator.UpdateCounter(weapon, selection.StatTrak.Value);
                        }
            }
            catch (Exception exception)
            {
                Invalidate(player.SteamID);
                Core.Logger.LogWarning(exception, "[sws2-armory] Could not update live StatTrak counter");
            }
        }
        return HookResult.Continue;
    }

    private HookResult OnMvp(EventRoundMvp @event)
    {
        if (_loaded && _settings.Allows(CosmeticKind.Music) && @event.UserIdPlayer is { } player
            && Profile(player) is { } profile && _catalog.Find(profile.Music) is { Kind: CosmeticKind.Music } kit)
        {
            profile.MusicMvps = Math.Min(999999, profile.MusicMvps + 1);
            @event.MusickItID = kit.Kit;
            @event.MusickItMvps = profile.MusicMvps;
            @event.NoMusic = 0;
            _store.Save(player.SteamID);
            ReconcilePlayer(player);
        }
        return HookResult.Continue;
    }

    private void OnDisconnected(IOnClientDisconnectedEvent @event)
    {
        if (!_slotSteamIds.Remove(@event.PlayerId, out var steamId)) return;
        _store.Forget(steamId);
        _cooldowns.Remove(steamId);
        _revisions.Remove(steamId);
        _originalMusic.Remove(steamId);
        _runtimeFailures.Remove(steamId);
        _failedProfiles.Remove(steamId);
    }

    private void OnMapUnload(IOnMapUnloadEvent @event) => ClearRuntime();
    private void ClearRuntime()
    {
        _weaponsApplied.Clear();
        _pawnsApplied.Clear();
        _originalAgents.Clear();
        _originalMusic.Clear();
        _glovesApplied.Clear();
        _precached.Clear();
        _runtimeFailures.Clear();
        _cooldowns.Clear();
    }
}
