using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TinyWorks.Sim;

// Fluids are items too (names, colours, icons), but only ever live in pipes and machines, never on belts or in storage.
public sealed record ItemDef(string Id, string Name, Color Color, int Price, bool Fluid = false);
public sealed record BuildingDef(string Id, string Name, int Size, (string Item, int Count)[] Cost);
// Unlock: null = available from the start, otherwise a key some milestone unlocks.
public sealed record Recipe(string Id, string Machine, (string Item, int Count)[] In, string Out, int OutCount, int Ticks, string Unlock = null);
public sealed record Milestone(string Name, (string Item, int Count)[] Need, string[] Unlocks);
// Bought once with coins, then worn in its slot: belt, machine, hat or particle.
public sealed record Cosmetic(string Id, string Slot, string Name, int Cost, Color Color);
public sealed record Upgrade(string Id, string Name, string Description, int Max, int BaseCost);
// A temporary boost bought with coins. Price per minute is multiplied by World.PriceLevel, which grows with progress.
// Strengths are the multipliers you can brew; the price scales with the bonus (strength - 1) relative to Default.
public sealed record Potion(string Id, string Name, string Effect, Color Color, int CoinsPerMinute, float[] Strengths, float Default)
{
    public string Describe(float strength) => string.Format(Effect, Defs.X(strength));
}

// IDs are strings everywhere (also in saves) so adding items later never shifts existing ones.
public static class Defs
{
    public static readonly Dictionary<string, ItemDef> Items = new();
    public static readonly Dictionary<string, BuildingDef> Buildings = new();
    public static readonly List<Recipe> Recipes = new();
    public static readonly (string Id, string Name, int PerPlate)[] Parts = { ("screw", "Screw", 4), ("nail", "Nail", 4), ("rod", "Rod", 2) };
    public static readonly string[] Ores ={ "iron_ore", "copper_ore", "stone", "coal" };
    public static readonly string[] BaseUnlocks = { "belt", "crossing", "inserter", "miner", "furnace", "pole", "generator" };
    public static readonly string[] BuildOrder = { "belt", "fast_belt", "express_belt", "crossing", "inserter", "miner", "furnace", "assembler", "chest", "pole", "big_pole", "super_pole", "generator", "splitter", "tunnel", "pipe", "pipe_tunnel", "pump", "refinery", "solar" };
    // Every building has a hotbar slot, locked ones greyed out, so slots never shift when something unlocks.
    // Keys 1-9 and 0 reach the first ten. Pressing a tiered slot again (belts, poles) steps through its unlocked tiers.
    // Sandbox tools: no milestone unlocks them, so outside sandbox they stay locked and hidden.
    public static readonly string[] SandboxOnly = { "source", "void" };
    public static readonly string[] HotbarOrder = { "belt", "inserter", "miner", "furnace", "crossing", "splitter", "tunnel", "assembler", "chest", "pole", "generator", "pipe", "pipe_tunnel", "pump", "refinery", "solar" };

    // Power (kW) a machine draws while it works. Power only ever speeds things up: see World.PowerBoost.
    public static readonly Dictionary<string, float> PowerUse = new() { ["miner"] = 90, ["furnace"] = 90, ["assembler"] = 75, ["inserter"] = 15, ["pump"] = 30, ["refinery"] = 120 };
    public const float SolarKW = 100; // always on: there is no day and night
    public const float GeneratorKW = 600, CoalKJ = 4000, SparkgasKJ = 200; // kJ per unit
    public const int TunnelRange = 5, PipeTunnelRange = 10; // how far apart an entrance and its exit may be
    public const float PipeCap = 100, PumpRate = 1; // fluid units per pipe tile, units per tick
    // Tier families share one hotbar slot, like the belts. Better poles wire further and power a bigger square.
    public static readonly string[] BeltTiers = { "belt", "fast_belt", "express_belt" }, PoleTiers = { "pole", "big_pole", "super_pole" };
    // Every tier family: the first id is the base building, which names the hotbar slot, the entity class and the recipes.
    public static readonly string[][] TierFamilies = { BeltTiers, PoleTiers };
    static readonly Dictionary<string, string[]> FamilyOf = TierFamilies.SelectMany(f => f.Select(id => (id, f))).ToDictionary(x => x.id, x => x.f);
    public static string[] TiersOf(string type) => type != null && FamilyOf.TryGetValue(type, out var f) ? f : null;
    public static string BaseOf(string type) => TiersOf(type)?[0] ?? type;
    public static int TierOf(string type) => TiersOf(type) is { } f ? System.Array.IndexOf(f, type) : 0;
    // How much faster a machine tier works than its base (and how much more power it draws). Belts use BeltSpeed.
    public static readonly Dictionary<string, float> TierSpeed = new();
    public static float SpeedOf(string type) => TierSpeed.GetValueOrDefault(type, 1f);
    public static float PowerOf(string type) => PowerUse.TryGetValue(BaseOf(type), out var kw) ? kw * SpeedOf(type) : 0;
    public static bool UsesPower(string type) => PowerUse.ContainsKey(BaseOf(type));
    // Tiles per tick for everything that carries items along (x80 = items/s). Must stay below Belt.Spacing.
    public static readonly Dictionary<string, float> BeltSpeed = new() { ["belt"] = 0.1f, ["fast_belt"] = 0.15f, ["express_belt"] = 0.2f, ["splitter"] = 0.2f, ["tunnel"] = 0.15f };
    // Two poles link when they are within the shorter of their two ranges, so a small pole never reaches 18 tiles.
    public static int WireRangeOf(string pole) => pole switch { "super_pole" => 18, "big_pole" => 12, _ => 7 };
    public static int PoleReachOf(string pole) => pole switch { "super_pole" => 4, "big_pole" => 3, _ => 2 }; // 9x9, 7x7, 5x5
    public static readonly Dictionary<string, long> StarterKit = new() { ["iron_plate"] = 40, ["stone"] = 16 };

    public static readonly Milestone[] Milestones =
    {
        new("First Plates", new[] { ("iron_plate", 40), ("copper_plate", 20) }, new[] { "assembler", "chest", "splitter", "tunnel" }),
        new("Gears Turning", new[] { ("gear", 40), ("copper_wire", 60) }, new[] { "fast_belt", "steel", "big_pole" }),
        new("Smart Factory", new[] { ("circuit", 30), ("steel_plate", 15) }, new[] { "express_belt", "super_pole", "pipe", "pipe_tunnel", "pump", "refinery" }),
        new("Clear Glass", new[] { ("plastic", 40), ("sulfur", 30) }, new[] { "sand", "glass", "lens", "battery", "motor", "lamp" }),
        new("Bright Ideas", new[] { ("lamp", 40), ("battery", 30), ("motor", 30) }, new[] { "smart_chip", "frame", "toy_robot", "clock", "solar_panel", "solar" }),
        new("Reach the Sky", new[] { ("toy_robot", 20), ("clock", 20), ("solar_panel", 20) }, new[] { "drone", "computer", "sky_beacon" }),
    };

    public static readonly Upgrade[] Upgrades =
    {
        new("machine_speed", "Faster Machines", "+10% speed for miners, furnaces, assemblers and refineries", 10, 100),
        new("arm_speed", "Quick Arms", "+15% inserter speed", 6, 80),
        new("hand", "Strong Hands", "+1 ore per click when mining by hand", 5, 30),
        new("sell_price", "Better Deals", "+10% coins for everything auto-sold", 10, 150),
        new("power", "Bigger Generators", "+20% power from every generator", 5, 120),
        new("pump", "Strong Pumps", "+25% water from every pump", 4, 200),
        new("pole_range", "Long Wires", "+2 tiles of wire reach for every pole", 3, 250),
    };

    public static readonly Potion[] Potions =
    {
        new("coin_rush", "Coin Rush", "Auto-sell pays {0}", new Color("f7c948"), 20, new[] { 1.5f, 2f, 3f }, 2f),
        new("overdrive", "Overdrive", "Machines work {0} as fast", new Color("e8665a"), 15, new[] { 1.25f, 1.5f, 2f }, 1.5f),
        new("quick_arms", "Arm Tonic", "Inserters swing {0} as fast", new Color("5aa9e8"), 8, new[] { 1.25f, 1.5f, 2f }, 1.5f),
        new("rock_crusher", "Rock Crusher", "Mining by hand gives {0} ore", new Color("62b86a"), 4, new[] { 2f, 5f, 10f }, 5f),
    };
    public static readonly Cosmetic[] Cosmetics =
    {
        new("belt_mint", "belt", "Mint", 400, new Color("9fe0c0")),
        new("belt_rose", "belt", "Rose", 400, new Color("f4b0c4")),
        new("belt_dusk", "belt", "Dusk", 900, new Color("a7aaf0")),
        new("machine_sky", "machine", "Sky", 600, new Color("b9dcf5")),
        new("machine_moss", "machine", "Moss", 600, new Color("c4e2ad")),
        new("machine_gold", "machine", "Gold", 1500, new Color("f7dc8a")),
        new("hat_party", "hat", "Party Hat", 300, new Color("e8665a")),
        new("hat_top", "hat", "Top Hat", 800, new Color("2a2433")),
        new("hat_crown", "hat", "Crown", 2000, new Color("f7c948")),
        new("fx_rainbow", "particle", "Rainbow", 500, new Color("b98cf0")),
        new("fx_gold", "particle", "Gold Dust", 900, new Color("f7c948")),
        new("fx_snow", "particle", "Snow", 900, new Color("e6f0ff")),
    };
    public static Cosmetic CosmeticById(string id) => System.Array.Find(Cosmetics, c => c.Id == id);

    public const int PotionMaxMinutes = 120; // per potion and strength; buying more extends it up to this
    public static Potion PotionById(string id) => System.Array.Find(Potions, p => p.Id == id);
    // "x1.5", "x2": multipliers as players read them, the same in every language setting.
    public static string X(float m) => "x" + m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    static Defs()
    {
        Item("iron_ore", "Iron Ore", "8fa3bf", 1);
        Item("copper_ore", "Copper Ore", "e0894f", 1);
        Item("stone", "Stone", "c9b391", 1);
        Item("coal", "Coal", "4a4652", 1);
        Item("iron_plate", "Iron Plate", "c8d4e3", 3);
        Item("copper_plate", "Copper Plate", "f2a66b", 3);
        Item("brick", "Brick", "c4624a", 4);
        Item("steel_plate", "Steel Plate", "7d8aa0", 18);
        Item("gear", "Gear", "aeb8c6", 7);
        Item("copper_wire", "Copper Wire", "f0a05a", 2);
        // Parts made from every metal's plate: one entry here per new metal, one per new part.
        foreach (var (metal, name, unlock) in new (string, string, string)[] { ("iron", "Iron", null), ("copper", "Copper", null), ("steel", "Steel", "steel") })
            foreach (var (part, partName, n) in Parts)
            {
                var plate = Items[metal + "_plate"];
                Items[$"{metal}_{part}"] = new ItemDef($"{metal}_{part}", $"{name} {partName}", plate.Color, (plate.Price * 5 + 4 * n - 1) / (4 * n)); // a quarter more than the plate, rounded up
                Recipes.Add(new($"{metal}_{part}", "assembler", new[] { (plate.Id, 1) }, $"{metal}_{part}", n, 20, unlock));
            }
        Item("lightning_rod", "Lightning Rod", "e8c15a", 28);
        Recipes.Add(new("lightning_rod", "assembler", new[] { ("copper_rod", 2), ("steel_plate", 1) }, "lightning_rod", 1, 40, "steel"));
        Item("circuit", "Circuit", "62b86a", 16);
        Item("plastic", "Plastic", "e9e4f2", 12);
        Item("sulfur", "Sulfur", "e8d84a", 10);
        Item("sand", "Sand", "e3cf8e", 1);
        Item("glass", "Glass", "a9dcef", 5);
        Item("lens", "Lens", "7fc8e8", 16);
        Item("battery", "Battery", "6fbf73", 24);
        Item("motor", "Motor", "9aa3b5", 50);
        Item("lamp", "Lamp", "ffe08a", 18);
        Item("smart_chip", "Smart Chip", "a77fe0", 60);
        Item("frame", "Frame", "8a7d6b", 65);
        Item("toy_robot", "Toy Robot", "e8665a", 120);
        Item("clock", "Clock", "f2a65a", 70);
        Item("solar_panel", "Solar Panel", "4a6fd0", 60);
        Item("drone", "Drone", "5aa9e8", 330);
        Item("computer", "Computer", "3d4250", 330);
        Item("sky_beacon", "Sky Beacon", "fff3a0", 1200);
        Fluid("water", "Water", "4f8fd9");
        Fluid("black_goo", "Black Goo", "3a2f45");
        Fluid("sparkgas", "Sparkgas", "b6e86a");

        Building("belt", "Belt", 1, ("iron_plate", 1));
        Building("fast_belt", "Fast Belt", 1, ("iron_plate", 1), ("gear", 1));
        Building("express_belt", "Express Belt", 1, ("steel_plate", 1), ("gear", 1));
        Building("crossing", "Crossing", 1, ("iron_plate", 4));
        Building("inserter", "Inserter", 1, ("iron_plate", 2));
        Building("miner", "Miner", 1, ("iron_plate", 10));
        Building("furnace", "Furnace", 1, ("stone", 8));
        Building("assembler", "Assembler", 1, ("iron_plate", 10), ("copper_plate", 5));
        Building("chest", "Chest", 1, ("iron_plate", 4));
        Building("pole", "Power Pole", 1, ("iron_plate", 2), ("copper_plate", 1));
        Building("big_pole", "Big Pole", 1, ("steel_plate", 2), ("copper_plate", 4));
        Building("super_pole", "Super Pole", 1, ("steel_plate", 4), ("circuit", 2), ("copper_plate", 4));
        Building("generator", "Generator", 1, ("iron_plate", 10), ("stone", 5));
        Building("splitter", "Splitter", 1, ("iron_plate", 5), ("gear", 2));
        Building("tunnel", "Underground Belt", 1, ("iron_plate", 6), ("gear", 3));
        Building("pipe", "Pipe", 1, ("iron_plate", 1));
        Building("pipe_tunnel", "Underground Pipe", 1, ("iron_plate", 8));
        Building("pump", "Pump", 1, ("iron_plate", 8), ("gear", 2));
        Building("refinery", "Refinery", 1, ("steel_plate", 5), ("circuit", 5), ("brick", 10));
        Building("solar", "Solar Array", 1, ("solar_panel", 2), ("steel_plate", 2));
        Building("hub", "Hub", 3);
        Building("source", "Item Source", 1);
        Building("void", "Item Void", 1);

        Recipes.Add(new("iron_plate", "furnace", new[] { ("iron_ore", 1) }, "iron_plate", 1, 40));
        Recipes.Add(new("copper_plate", "furnace", new[] { ("copper_ore", 1) }, "copper_plate", 1, 40));
        Recipes.Add(new("brick", "furnace", new[] { ("stone", 2) }, "brick", 1, 60));
        Recipes.Add(new("steel_plate", "furnace", new[] { ("iron_plate", 5) }, "steel_plate", 1, 100, "steel"));
        Recipes.Add(new("gear", "assembler", new[] { ("iron_plate", 2) }, "gear", 1, 20));
        Recipes.Add(new("copper_wire", "assembler", new[] { ("copper_plate", 1) }, "copper_wire", 2, 20));
        // Refinery: at most one fluid in (from the pipe behind it), fluid or item out (pipe or inserter in front).
        Recipes.Add(new("black_goo", "refinery", new[] { ("water", 20), ("coal", 1) }, "black_goo", 20, 40));
        Recipes.Add(new("plastic", "refinery", new[] { ("black_goo", 20), ("coal", 1) }, "plastic", 2, 40));
        Recipes.Add(new("sparkgas", "refinery", new[] { ("black_goo", 20) }, "sparkgas", 20, 40));
        Recipes.Add(new("sulfur", "refinery", new[] { ("sparkgas", 20), ("stone", 1) }, "sulfur", 2, 60));
        Recipes.Add(new("circuit", "assembler", new[] { ("iron_plate", 1), ("copper_wire", 3) }, "circuit", 1, 30));
        // Milestone 4: glass and the first parts
        Recipes.Add(new("sand", "assembler", new[] { ("stone", 1) }, "sand", 2, 20, "sand"));
        Recipes.Add(new("glass", "furnace", new[] { ("sand", 2) }, "glass", 1, 60, "glass"));
        Recipes.Add(new("lens", "assembler", new[] { ("glass", 2), ("copper_plate", 1) }, "lens", 1, 40, "lens"));
        Recipes.Add(new("battery", "assembler", new[] { ("sulfur", 1), ("copper_plate", 1), ("iron_plate", 1) }, "battery", 1, 40, "battery"));
        Recipes.Add(new("motor", "assembler", new[] { ("steel_plate", 1), ("gear", 2), ("copper_wire", 2) }, "motor", 1, 60, "motor"));
        Recipes.Add(new("lamp", "assembler", new[] { ("glass", 1), ("copper_wire", 2), ("iron_plate", 1) }, "lamp", 1, 40, "lamp"));
        // Milestone 5: smarter parts and the first products
        Recipes.Add(new("smart_chip", "assembler", new[] { ("circuit", 2), ("plastic", 1) }, "smart_chip", 1, 60, "smart_chip"));
        Recipes.Add(new("frame", "assembler", new[] { ("steel_plate", 2), ("plastic", 1) }, "frame", 1, 60, "frame"));
        Recipes.Add(new("toy_robot", "assembler", new[] { ("motor", 1), ("circuit", 1), ("plastic", 2) }, "toy_robot", 1, 80, "toy_robot"));
        Recipes.Add(new("clock", "assembler", new[] { ("gear", 3), ("glass", 1), ("battery", 1) }, "clock", 1, 80, "clock"));
        Recipes.Add(new("solar_panel", "assembler", new[] { ("glass", 2), ("circuit", 1), ("steel_plate", 1) }, "solar_panel", 1, 80, "solar_panel"));
        // Milestone 6: the big ones. The Sky Beacon sells for the most.
        Recipes.Add(new("drone", "assembler", new[] { ("motor", 2), ("smart_chip", 1), ("frame", 1), ("battery", 1) }, "drone", 1, 120, "drone"));
        Recipes.Add(new("computer", "assembler", new[] { ("smart_chip", 3), ("frame", 1), ("glass", 1) }, "computer", 1, 120, "computer"));
        Recipes.Add(new("sky_beacon", "assembler", new[] { ("drone", 1), ("computer", 1), ("solar_panel", 2), ("lens", 2) }, "sky_beacon", 1, 200, "sky_beacon"));
    }

    static void Item(string id, string name, string hex, int price) => Items[id] = new ItemDef(id, name, new Color(hex), price);
    static void Fluid(string id, string name, string hex) => Items[id] = new ItemDef(id, name, new Color(hex), 0, true);
    static void Building(string id, string name, int size, params (string, int)[] cost) => Buildings[id] = new BuildingDef(id, name, size, cost);


    public static bool IsBelt(string type) => BaseOf(type) == "belt";
    public static bool IsFluid(string id) => id != null && Items.TryGetValue(id, out var d) && d.Fluid;
    public static Recipe RecipeById(string id) => Recipes.FirstOrDefault(r => r.Id == id);
    public static int UpgradeCost(Upgrade u, int level) => u.BaseCost << level;

    public static string LockedWhy(string type) =>
        System.Array.IndexOf(SandboxOnly, type) >= 0 ? $"{Buildings[type].Name} is a sandbox tool" : $"{Buildings[type].Name} unlocks with milestone {UnlockTier(type)}";

    // The milestone (1-based tier) that unlocks a building or recipe, 0 if it is there from the start.
    public static int UnlockTier(string key)
    {
        if (System.Array.IndexOf(BaseUnlocks, key) >= 0) return 0;
        for (int i = 0; i < Milestones.Length; i++) if (System.Array.IndexOf(Milestones[i].Unlocks, key) >= 0) return i + 1;
        return 0;
    }
}
