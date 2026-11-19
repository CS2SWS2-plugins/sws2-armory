using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using Sws2Armory.Models;
using Sws2Armory.Services;

namespace Sws2Armory;

public partial class Sws2Armory
{
    private readonly record struct Stamp(ulong SteamId, long Revision, byte Team);
    private readonly record struct AppliedWeapon(Stamp Stamp, ulong ItemId, int PaintKit, ushort Definition);
    private readonly Dictionary<uint, AppliedWeapon> _weaponsApplied = [];
    private readonly Dictionary<uint, Stamp> _pawnsApplied = [];
    private readonly Dictionary<uint, (string Model, ushort Character, string Voice, bool Female)> _originalAgents = [];
    private readonly Dictionary<ulong, (int Kit, int Mvps, bool NoMusic, ushort Inventory)> _originalMusic = [];
    private readonly HashSet<uint> _glovesApplied = [];
    private readonly HashSet<string> _precached = new(StringComparer.Ordinal);
    private readonly HashSet<ulong> _runtimeFailures = [];
    private ulong _givingReplacementTo;

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

    private void ReconcilePlayer(IPlayer player, bool refreshWeapons = false, string? refreshTarget = null)
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
                // Giving or removing a weapon changes the native inventory vector.
                foreach (var handle in refreshWeapons ? weapons.MyWeapons.ToArray() : weapons.MyWeapons.AsEnumerable())
                    if (handle.Value is { IsValid: true } weapon)
                    {
                        var raw = handle.Raw;
                        var item = weapon.AttributeManager.Item;
                        var definition = item.ItemDefinitionIndex;
                        var knife = CosmeticCatalog.IsKnife(definition);
                        var target = knife ? "knife" : _catalog.WeaponTarget(definition);
                        if (target is null) continue;
                        var refresh = refreshWeapons && (refreshTarget is null || refreshTarget == target);
                        var previouslyApplied = _weaponsApplied.TryGetValue(raw, out var applied);
                        if (!refresh && previouslyApplied && applied.Stamp == stamp && applied.ItemId == item.ItemID
                            && applied.PaintKit == weapon.FallbackPaintKit && applied.Definition == definition) continue;
                        profile.Items.TryGetValue(target, out var selection);
                        var kind = knife ? CosmeticKind.Knife : CosmeticKind.Weapon;
                        var cosmetic = _catalog.Find(selection?.CosmeticId);
                        if (cosmetic is not null && (cosmetic.Kind != kind || cosmetic.Target != target)) cosmetic = null;
                        if (!_settings.Allows(kind)) { selection = null; cosmetic = null; }
                        // Never alter an unselected item unless this plugin
                        // previously painted this exact entity (full serial).
                        if (selection is null && !previouslyApplied) continue;
                        if (refresh && TryReplaceWeapon(player, pawn, weapons, weapon, target, selection, cosmetic, stamp)) continue;
                        ApplyWeapon(weapon, stamp, target, selection, cosmetic);
                        if (knife) ScheduleKnifePaint(player, pawn, raw, stamp, refresh);
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

    private void ApplyWeapon(CBasePlayerWeapon weapon, Stamp stamp, string target, ItemSelection? selection, CatalogEntry? cosmetic)
    {
        ushort? knifeDefinition = target == "knife" ? cosmetic?.DefinitionIndex ?? (ushort)(stamp.Team == 2 ? 59 : 42) : null;
        _econ.Weapon(weapon, stamp.SteamId, cosmetic, selection ?? new ItemSelection(), knifeDefinition,
            _settings.Allows(CosmeticKind.Sticker), _settings.Allows(CosmeticKind.Charm));
        var raw = weapon.Entity!.EntityHandle.Raw;
        if (selection is null) _weaponsApplied.Remove(raw);
        else
        {
            var item = weapon.AttributeManager.Item;
            _weaponsApplied[raw] = new(stamp, item.ItemID, weapon.FallbackPaintKit, item.ItemDefinitionIndex);
        }
    }

    private void LogHeldWeaponStatus(IPlayer player)
    {
        try
        {
            if (player.PlayerPawn?.WeaponServices?.ActiveWeapon.Value is not { IsValid: true } weapon)
            {
                Core.Logger.LogInformation("[sws2-armory] Weapon status for {SteamId}: no active weapon", player.SteamID);
                return;
            }

            var item = weapon.AttributeManager.Item;
            var definition = item.ItemDefinitionIndex;
            var knife = CosmeticCatalog.IsKnife(definition);
            var target = knife ? "knife" : _catalog.WeaponTarget(definition);
            var selection = target is null ? null : Profile(player)?.Items.GetValueOrDefault(target);
            var skin = _catalog.Find(selection?.CosmeticId);
            var charm = _catalog.Find(selection?.Charm);
            Core.Logger.LogInformation(
                "[sws2-armory] Weapon status for {SteamId}: entity={Entity} target={Target} definition={Definition} selected={Selection} expectedDefinition={ExpectedDefinition} paint={Paint} expectedPaint={ExpectedPaint} charm={Charm} itemId={ItemId} dynamicAttributes={Attributes} enabled={Enabled} applyFailed={ApplyFailed} model={Model} subclass={Subclass}",
                player.SteamID, weapon.DesignerName, target, definition, skin?.Id, skin?.DefinitionIndex,
                weapon.FallbackPaintKit, skin?.Kit, charm?.Id, item.ItemID,
                item.NetworkedDynamicAttributes.Attributes.Count, _settings.Allows(knife ? CosmeticKind.Knife : CosmeticKind.Weapon),
                _runtimeFailures.Contains(player.SteamID), weapon.GetSkeletonInstance()?.ModelState.ModelName, weapon.SubclassID.HashCode);
        }
        catch (Exception exception)
        {
            Core.Logger.LogWarning(exception, "[sws2-armory] Could not read weapon status for {SteamId}", player.SteamID);
        }
    }

    private bool TryReplaceWeapon(IPlayer player, CCSPlayerPawn pawn, CPlayer_WeaponServices weapons, CBasePlayerWeapon oldWeapon,
        string target, ItemSelection? selection, CatalogEntry? cosmetic, Stamp stamp)
    {
        if (pawn.ItemServices is not { } items) return false;
        var oldRaw = oldWeapon.Entity!.EntityHandle.Raw;
        var inventory = weapons.MyWeapons.ToArray();
        var active = weapons.ActiveWeapon;
        var clip1 = oldWeapon.Clip1;
        var clip2 = oldWeapon.Clip2;
        var oldReserve = oldWeapon.ReserveAmmo;
        var reserve = new int[oldReserve.ElementCount];
        for (var index = 0; index < reserve.Length; index++) reserve[index] = oldReserve[index];

        // A new entity makes the client build the material and attachments from its first snapshot.
        CBasePlayerWeapon replacement;
        _givingReplacementTo = stamp.SteamId;
        var designerName = target == "knife" ? (stamp.Team == 2 ? "weapon_knife_t" : "weapon_knife") : target;
        try { replacement = items.GiveItem<CBasePlayerWeapon>(designerName); }
        finally { _givingReplacementTo = 0; }
        if (replacement is not { IsValid: true, Entity: { } identity }) return false;
        var raw = identity.EntityHandle.Raw;
        if (inventory.Any(handle => handle.Raw == raw)) return false;
        if (!weapons.MyWeapons.Any(handle => handle.Raw == raw))
        {
            replacement.Despawn();
            return false;
        }

        try
        {
            if (selection is not null || target == "knife")
                ApplyWeapon(replacement, stamp, target, selection, cosmetic);
            if (target != "knife") RestoreAmmo(replacement, clip1, clip2, reserve);
        }
        catch
        {
            _weaponsApplied.Remove(raw);
            weapons.RemoveWeapon(replacement);
            if (active.Value is { IsValid: true } previous) weapons.SelectWeapon(previous);
            throw;
        }
        if (oldWeapon.IsValid)
        {
            if (weapons.MyWeapons.Any(handle => handle.Raw == oldRaw)) weapons.RemoveWeapon(oldWeapon);
            else if (!oldWeapon.OwnerEntity.IsValid) oldWeapon.Despawn();
        }
        _weaponsApplied.Remove(oldRaw);
        if (active.Raw == oldRaw && target == "knife")
        {
            // The engine may auto-select another weapon when it removes the old knife.
            var expectedActive = weapons.ActiveWeapon.Raw;
            ScheduleKnifePaint(player, pawn, raw, stamp, refreshView: true, expectedActive: expectedActive);
            return true;
        }
        if (active.Raw == oldRaw) weapons.SelectWeapon(replacement);
        else if (active.Value is { IsValid: true } previous && weapons.ActiveWeapon.Raw != active.Raw)
            weapons.SelectWeapon(previous);

        if (target == "knife")
        {
            ScheduleKnifePaint(player, pawn, raw, stamp, refreshView: true);
            return true;
        }

        var slot = player.PlayerID;
        var session = player.SessionId;
        var pawnRaw = pawn.Entity!.EntityHandle.Raw;
        Core.Scheduler.NextTick(() =>
        {
            if (!_loaded || Core.PlayerManager.GetPlayer(slot) is not { IsValid: true } current || current.SessionId != session
                || current.PlayerPawn is not { IsValid: true, LifeState: 0 } currentPawn || currentPawn.Entity!.EntityHandle.Raw != pawnRaw
                || currentPawn.TeamNum != stamp.Team || currentPawn.WeaponServices is not { } currentWeapons
                || !currentWeapons.MyWeapons.Any(handle => handle.Raw == raw)
                || new CHandle<CBasePlayerWeapon>(raw).Value is not { IsValid: true } liveWeapon) return;
            // Cap any delayed refill without refunding a shot fired since the switch.
            RestoreAmmo(liveWeapon, Math.Min(clip1, liveWeapon.Clip1), Math.Min(clip2, liveWeapon.Clip2), reserve, capReserve: true);
        });
        return true;
    }

    private void ScheduleKnifePaint(IPlayer player, CCSPlayerPawn pawn, uint raw, Stamp stamp,
        bool refreshView, uint? expectedActive = null)
    {
        var slot = player.PlayerID;
        var session = player.SessionId;
        var pawnRaw = pawn.Entity!.EntityHandle.Raw;
        Core.Scheduler.DelayBySeconds(0.2f, () =>
        {
            if (!_loaded || Core.PlayerManager.GetPlayer(slot) is not { IsValid: true } current || current.SessionId != session
                || current.PlayerPawn is not { IsValid: true, LifeState: 0 } currentPawn || currentPawn.Entity!.EntityHandle.Raw != pawnRaw
                || currentPawn.TeamNum != stamp.Team || currentPawn.WeaponServices is not { } weapons
                || !weapons.MyWeapons.Any(handle => handle.Raw == raw)
                || new CHandle<CBasePlayerWeapon>(raw).Value is not { IsValid: true } knife) return;
            try
            {
                if (_revisions.GetValueOrDefault(current.SteamID) == stamp.Revision)
                {
                    // Knife deployment can rebuild the model and overwrite paint written during GiveNamedItem.
                    var selection = _settings.Allows(CosmeticKind.Knife) ? Profile(current)?.Items.GetValueOrDefault("knife") : null;
                    var cosmetic = _catalog.Find(selection?.CosmeticId);
                    if (cosmetic is not { Kind: CosmeticKind.Knife, Target: "knife" }) cosmetic = null;
                    ApplyWeapon(knife, stamp, "knife", selection, cosmetic);
                }
                if (refreshView) RefreshKnifeView(current, weapons, knife, expectedActive);
            }
            catch (Exception exception)
            {
                if (_runtimeFailures.Add(stamp.SteamId))
                    Core.Logger.LogError(exception, "[sws2-armory] Failed to finish applying the knife for {SteamId}", stamp.SteamId);
            }
        });
    }

    private static void RefreshKnifeView(IPlayer player, CPlayer_WeaponServices weapons, CBasePlayerWeapon knife, uint? expectedActive)
    {
        var active = weapons.ActiveWeapon;
        if (expectedActive.HasValue && (!active.IsValid || active.Raw == expectedActive.Value)
            && active.Raw != knife.Entity!.EntityHandle.Raw)
            weapons.SelectWeapon(knife);

        // The owner's first-person model can retain the old material across delta updates.
        // Send a fresh snapshot after the give, subclass change and paint have settled.
        var client = player.ServerSideClient;
        if (client.Address != 0 && client.IsActive) client.ForceFullUpdate();
    }

    private static void RestoreAmmo(CBasePlayerWeapon weapon, int clip1, int clip2, int[] reserve, bool capReserve = false)
    {
        weapon.Clip1 = clip1;
        weapon.Clip2 = clip2;
        weapon.Clip1Updated();
        weapon.Clip2Updated();
        var currentReserve = weapon.ReserveAmmo;
        for (var index = 0; index < reserve.Length && index < currentReserve.ElementCount; index++)
            currentReserve[index] = capReserve ? Math.Min(reserve[index], currentReserve[index]) : reserve[index];
        weapon.ReserveAmmoUpdated();
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
        if (@event.UserIdPlayer is { SteamID: > 0 } player && player.SteamID != _givingReplacementTo
            && @event.DefIndex is > 0 and <= ushort.MaxValue)
        {
            var definition = (ushort)@event.DefIndex;
            if (!CosmeticCatalog.IsKnife(definition) && _catalog.WeaponTarget(definition) is null) return HookResult.Continue;
            // The pickup may be transmitted this frame. Recheck after GiveNamedItem finishes.
            ReconcilePlayer(player);
            var slot = player.PlayerID;
            var session = player.SessionId;
            Core.Scheduler.NextTick(() =>
            {
                if (_loaded && Core.PlayerManager.GetPlayer(slot) is { } current && current.SessionId == session)
                    ReconcilePlayer(current);
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
