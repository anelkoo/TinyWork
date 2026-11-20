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
public sealed record Upgrade(string Id, string Name, string Description, int Max, int BaseCost);

// IDs are strings everywhere (also in saves) so adding items later never shifts existing ones.
public static class Defs
{
    public static readonly Dictionary<string, ItemDef> Items = new();
    public static readonly Dictionary<string, BuildingDef> Buildings = new();
    public static readonly List<Recipe> Recipes = new();
    public static readonly string[] Ores = { "iron_ore", "copper_ore", "stone", "coal" };
    public static readonly string[] BaseUnlocks = { "belt", "crossing", "inserter", "miner", "furnace", "pole", "generator" };
    public static readonly string[] BuildOrder = { "belt", "fast_belt", "express_belt", "crossing", "inserter", "miner", "furnace", "assembler", "chest", "pole", "generator", "splitter", "tunnel", "pipe", "pipe_tunnel", "pump", "refinery" };
    // Hotbar keys 1-9. Key 1 cycles through the unlocked belt tiers, so faster belts need no slot of their own.
    public static readonly string[] HotbarOrder = { "belt", "inserter", "miner", "furnace", "crossing", "pole", "generator", "assembler", "chest" };

    // Power (kW) a machine draws while it works. Power only ever speeds things up: see World.PowerBoost.
    public static readonly Dictionary<string, float> PowerUse = new() { ["miner"] = 90, ["furnace"] = 90, ["assembler"] = 75, ["inserter"] = 15, ["pump"] = 30, ["refinery"] = 120 };
    public const float GeneratorKW = 600, CoalKJ = 4000, SparkgasKJ = 200; // kJ per unit
    public const int TunnelRange = 5, PipeTunnelRange = 10; // how far apart an entrance and its exit may be
    public const float PipeCap = 100, PumpRate = 1; // fluid units per pipe tile, units per tick
    public const int WireRange = 7, PoleReach = 2; // poles link up to 7 tiles apart and power a 5x5 square
    public static readonly Dictionary<string, long> StarterKit = new() { ["iron_plate"] = 40, ["stone"] = 16 };

    public static readonly Milestone[] Milestones =
    {
        new("First Plates", new[] { ("iron_plate", 40), ("copper_plate", 20) }, new[] { "assembler", "chest", "splitter", "tunnel" }),
        new("Gears Turning", new[] { ("gear", 40), ("copper_wire", 60) }, new[] { "fast_belt", "steel" }),
        new("Smart Factory", new[] { ("circuit", 30), ("steel_plate", 15) }, new[] { "express_belt", "pipe", "pipe_tunnel", "pump", "refinery" }),
    };

    public static readonly Upgrade[] Upgrades =
    {
        new("machine_speed", "Faster Machines", "+10% speed for miners, furnaces and assemblers", 5, 100),
        new("arm_speed", "Quick Arms", "+15% inserter speed", 3, 80),
        new("hand", "Strong Hands", "+1 ore per click when mining by hand", 3, 30),
    };

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
        Item("circuit", "Circuit", "62b86a", 16);
        Item("plastic", "Plastic", "e9e4f2", 12);
        Item("sulfur", "Sulfur", "e8d84a", 10);
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
        Building("generator", "Generator", 1, ("iron_plate", 10), ("stone", 5));
        Building("splitter", "Splitter", 1, ("iron_plate", 5), ("gear", 2));
        Building("tunnel", "Underground Belt", 1, ("iron_plate", 6), ("gear", 3));
        Building("pipe", "Pipe", 1, ("iron_plate", 1));
        Building("pipe_tunnel", "Underground Pipe", 1, ("iron_plate", 8));
        Building("pump", "Pump", 1, ("iron_plate", 8), ("gear", 2));
        Building("refinery", "Refinery", 1, ("steel_plate", 5), ("circuit", 5), ("brick", 10));
        Building("hub", "Hub", 3);

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
    }

    static void Item(string id, string name, string hex, int price) => Items[id] = new ItemDef(id, name, new Color(hex), price);
    static void Fluid(string id, string name, string hex) => Items[id] = new ItemDef(id, name, new Color(hex), 0, true);
    static void Building(string id, string name, int size, params (string, int)[] cost) => Buildings[id] = new BuildingDef(id, name, size, cost);


    public static bool IsBelt(string type) => type is "belt" or "fast_belt" or "express_belt";
    public static bool IsFluid(string id) => id != null && Items.TryGetValue(id, out var d) && d.Fluid;
    public static Recipe RecipeById(string id) => Recipes.FirstOrDefault(r => r.Id == id);
    public static int UpgradeCost(Upgrade u, int level) => u.BaseCost << level;

    // The milestone (1-based tier) that unlocks a building or recipe, 0 if it is there from the start.
    public static int UnlockTier(string key)
    {
        if (System.Array.IndexOf(BaseUnlocks, key) >= 0) return 0;
        for (int i = 0; i < Milestones.Length; i++) if (System.Array.IndexOf(Milestones[i].Unlocks, key) >= 0) return i + 1;
        return 0;
    }
}
