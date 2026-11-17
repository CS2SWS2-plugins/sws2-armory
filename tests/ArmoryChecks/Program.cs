using System.Text.Json;
using Sws2Armory.Configuration;
using Sws2Armory.Models;
using Sws2Armory.Services;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}

var catalog = new CosmeticCatalog(Path.Combine(AppContext.BaseDirectory, "items.json"));
Check(catalog.Entries.Count > 13000, "Production catalog must be shipped");
Check(Enum.GetValues<CosmeticKind>().All(kind => catalog.Search(kind).Any()), "Every requested cosmetic category must exist");
Check(catalog.Search(CosmeticKind.Weapon).Select(e => e.Target).Distinct().Count() == 34, "34 firearm mappings");
Check(catalog.Search(CosmeticKind.Knife).Select(e => e.DefinitionIndex).Distinct().Count() == 20, "20 knife types including Kukri");
Check(catalog.Search(CosmeticKind.Glove).Select(e => e.DefinitionIndex).Distinct().Count() == 8, "8 glove types");
Check(catalog.Search(CosmeticKind.Agent).Any(e => e.Target == "t") && catalog.Search(CosmeticKind.Agent).Any(e => e.Target == "ct"), "Both agent teams");
Check(catalog.WeaponTarget(60) == "weapon_m4a1_silencer" && catalog.WeaponTarget(61) == "weapon_usp_silencer", "Silenced weapon mapping");
Check(catalog.WeaponTarget(31) is null, "Unsupported Zeus must not be treated as a rifle");
Check(catalog.Search(CosmeticKind.Weapon, "二西莫夫").Any(), "Chinese search");
Check(catalog.Search(CosmeticKind.Knife, "VANILLA").Count() == 20, "Case-insensitive vanilla knife search");
Check(CustomizationInput.TryWear("0", out _) && CustomizationInput.TryWear("1", out _), "Float endpoints");
foreach (var invalid in new[] { "NaN", "Infinity", "-Infinity", "-0.1", "1.01", "0,1", "1e100", "" })
    Check(!CustomizationInput.TryWear(invalid, out _), $"Reject float {invalid}");
Check(CustomizationInput.TrySeed("1000", out _) && CustomizationInput.TrySeed("0", out _), "Seed endpoints");
foreach (var invalid in new[] { "-1", "1001", "1.5", "1e3", "2147483648" }) Check(!CustomizationInput.TrySeed(invalid, out _), $"Reject seed {invalid}");
Check(!CustomizationInput.TryCounter("1000000", out _) && CustomizationInput.TryCounter("999999", out _), "Counter range");
Check(!CustomizationInput.ValidName("bad\nname") && !CustomizationInput.ValidName(new string('a', 65)), "Reject invalid names");
Check(!CustomizationInput.ValidName(new string('中', 54)) && CustomizationInput.ValidName(new string('中', 53)), "UTF-8 engine buffer limit");
var settings = new ArmorySettings { Enabled = false };
Check(Enum.GetValues<CosmeticKind>().All(kind => !settings.Allows(kind)), "Global disable covers all categories");
settings = new ArmorySettings { Knives = false };
Check(!settings.Allows(CosmeticKind.Knife) && settings.Allows(CosmeticKind.Weapon), "Independent category toggles");

var temporary = Path.Combine(Path.GetTempPath(), $"armory-checks-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporary);
try
{
    var errors = new List<Exception>();
    var store = new LoadoutStore(temporary, errors.Add);
    const ulong steamId = 76561198000000001;
    var profile = store.Get(steamId);
    profile.Items["weapon_ak47"] = new ItemSelection { CosmeticId = "weapon/weapon_ak47:801", Wear = 0.123456f, Seed = 1000, StatTrak = 12, NameTag = "测试" };
    profile.Items["weapon_ak47"].Stickers[4] = catalog.Search(CosmeticKind.Sticker).First().Id;
    profile.Items["weapon_ak47"].Charm = catalog.Search(CosmeticKind.Charm).First().Id;
    profile.CounterTerroristAgent = catalog.Search(CosmeticKind.Agent, target: "ct").First().Id;
    store.Save(steamId);
    // Exercise queued immutable snapshots and ensure the latest state wins.
    for (var i = 13; i <= 80; i++) { profile.Items["weapon_ak47"].StatTrak = i; store.Save(steamId); }
    store.Flush();
    Check(errors.Count == 0, "Background saves must succeed");
    store.Forget(steamId);
    var restored = store.Get(steamId);
    Check(restored.Items["weapon_ak47"].StatTrak == 80, "Ordered saves preserve last update");
    Check(restored.Items["weapon_ak47"].Wear == 0.123456f && restored.Items["weapon_ak47"].Seed == 1000, "Float and seed survive reconnect");
    Check(restored.Items["weapon_ak47"].Stickers.ContainsKey(4) && restored.Items["weapon_ak47"].Charm is not null, "Attachments survive reconnect");
    Check(restored.CounterTerroristAgent == profile.CounterTerroristAgent, "Agent survives reconnect");
    store.Reset(steamId);
    store.Save(steamId);
    store.Flush();
    store.Forget(steamId);
    Check(store.Get(steamId).Items.Count == 0, "Reset persists");
    var savedPath = Path.Combine(temporary, $"{steamId}.json");
    var originalContents = File.ReadAllText(savedPath);
    Directory.CreateDirectory(savedPath + ".tmp");
    store.Get(steamId).MusicMvps = 10;
    store.Save(steamId);
    store.Flush();
    Check(errors.Count == 1, "Write errors are reported");
    Check(File.ReadAllText(savedPath) == originalContents, "Failed temporary write preserves previous save");
    var corruptPath = Path.Combine(temporary, "2.json");
    File.WriteAllText(corruptPath, "{broken");
    try { store.Get(2); Check(false, "Corrupt profile must fail"); } catch (JsonException) { checks++; }
    Check(File.ReadAllText(corruptPath) == "{broken", "Corrupt profile is preserved");
    File.WriteAllText(Path.Combine(temporary, "3.json"), "{\"Items\":{\"weapon_ak47\":{\"Wear\":1.1}}}");
    try { store.Get(3); Check(false, "Out of range persisted data must fail"); } catch (InvalidDataException) { checks++; }
}
finally { Directory.Delete(temporary, recursive: true); }
Console.WriteLine($"{checks} armory regression checks passed.");
