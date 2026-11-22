using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TinyWorks.Sim;

// Run with: godot --headless --path . -- --selftest   (exit code 0 = all passed)
public static class SelfTest
{
    static int _fails;

    public static int Run()
    {
        _fails = 0;
        Test("belts keep spacing and back up", BeltsBackUp);
        Test("miner -> furnace -> hub conserves items", Chain);
        Test("removal refunds costs and contents", Refunds);
        Test("save/load continues identically", RoundTrip);
        Test("assembler turns plates into gears without loss", AssemblerChain);
        Test("chest passes items through, filter blocks others", ChestAndFilter);
        Test("crossing lets two lines pass without mixing", CrossingLines);
        Test("power doubles speed, burns coal, never slows below normal", PowerBonus);
        Test("too little power gives part of the bonus", PowerShortage);
        Test("poles out of range form separate networks", PowerNetworks);
        Test("milestone unlocks after exact deliveries", Milestones);
        Test("auto-sell keeps the reserve and pays the price", AutoSell);
        Test("speed upgrade makes miners faster", SpeedUpgrade);
        Test("every tier can be built with earlier unlocks", Producible);
        Test("underground belt carries items past buildings without loss", Tunnels);
        Test("splitter deals items out evenly and skips missing sides", Splitting);
        Test("fluid survives network rebuilds and saves", FluidConserved);
        Test("joining two fluids keeps the larger one", FluidMixing);
        Test("pump -> refinery -> plastic, sparkgas powers a generator", RefineryChain);
        Test("copper melts and alloys with tin into bronze; steam and fuel power a generator", AlloysAndFuels);
        Test("items glide over belt edges without pausing", GlideAcrossEdges);
        Test("coin rush doubles sales, then wears off", CoinRush);
        Test("potions stack up to the cap and cost more later in the game", PotionPrices);
        Test("strongest potion runs first, weaker ones wait, never multiplied", PotionOrder);
        Test("potions from v0.0.3 saves still load", LegacyPotions);
        Test("better deals and bigger generators upgrades work", NewUpgrades);
        Test("big poles wire far, but only to each other", BigPoles);
        Test("drawing across a line makes a crossing; splitter commits and never jams", CrossAndSplit);
        Test("side-loaded items glide in from the side edge", SideGlide);
        Test("older saves still load and run", LegacySaves);
        Test("undo and redo give back exactly what they took", UndoRedo);
        Test("a turned copy of an underground pair stays paired", PasteTurned);
        Test("item source feeds an item void without loss; locked outside sandbox", SourceVoid);
        Test("production counters match what miners made and the session lands in the save", StatsAndPause);
        Test("solar powers machines without fuel and is used before coal", SolarPower);
        Test("long wires let poles link further", LongWires);
        Test("every metal makes screws, nails and rods; they survive a save", MetalParts);
        Test("own music folder lists only music files, in name order", MusicFolder);
        Test("every belt, splitter and underground tier at full load keeps spacing and loses nothing", CarriersAtFullLoad);
        Test("Belt Rush x10 moves ten times as much and loses nothing", BeltRush);
        Test("items sort into their list groups", ItemGroups);
        Test("lightning hits only rods, powers their network for a while, then stops", LightningRods);
        Test("new ores never move old ore tiles; each has a starter patch near the Hub", OreLayout);
        Test("Mk2 and Mk3 machines work two and three times as fast", MachineTiers);
        Test("a better tier goes straight onto the old one, refunds it, keeps the recipe; undo swaps back", UpgradeInPlace);
        GD.Print(_fails == 0 ? "SELFTEST PASS" : $"SELFTEST FAIL ({_fails})");
        return _fails;
    }

    static void Test(string name, Action body)
    {
        try { body(); GD.Print($"  ok   {name}"); }
        catch (Exception e) { _fails++; GD.Print($"  FAIL {name}: {e.Message}"); }
    }

    static void Check(bool ok, string msg) { if (!ok) throw new Exception(msg); }

    static World Sandbox()
    {
        var w = new World(1) { Mode = "sandbox" };
        w.ForcedOre[new Vector2I(-99, -99)] = null; // non-empty => ore comes only from ForcedOre
        return w;
    }

    static void Belts(World w, int rot, params (int x, int y)[] tiles) { foreach (var (x, y) in tiles) w.Place("belt", new(x, y), rot); }

    static void CheckSpacing(World w)
    {
        foreach (var b in w.Entities.OfType<Belt>())
        {
            for (int i = 1; i < b.Items.Count; i++)
                Check(b.Items[i - 1].Pos - b.Items[i].Pos >= Belt.Spacing - 1e-4f, $"items too close on belt {b.Pos} at tick {w.Tick}");
            if (b.Items.Count > 0 && w.At(b.Front) is Belt nb && nb.InputFrom(w) == b.Pos && nb.Items.Count > 0)
                Check(1 - b.Items[0].Pos + nb.Items[^1].Pos >= Belt.Spacing - 1e-4f, $"items too close across {b.Pos}->{nb.Pos} at tick {w.Tick}");
        }
    }

    static int OnBelts(World w) => w.Entities.OfType<Belt>().Sum(b => b.Items.Count);

    static void BeltsBackUp()
    {
        var w = Sandbox();
        w.ForcedOre[new(0, 0)] = "iron_ore";
        w.ForcedOre[new(6, 1)] = "iron_ore";
        w.Place("miner", new(0, 0), 0);
        w.Place("miner", new(6, 1), 2); // side-loads onto (5,1)
        // Mixed tiers: express feeds fast feeds basic, so a quick belt runs into a slow one.
        foreach (var (x, y) in new[] { (1, 0), (2, 0) }) w.Place("express_belt", new(x, y), 0);
        foreach (var (x, y) in new[] { (3, 0), (4, 0) }) w.Place("fast_belt", new(x, y), 0);
        Belts(w, 1, (5, 0), (5, 1), (5, 2));
        Belts(w, 2, (5, 3), (4, 3), (3, 3)); // dead end, nothing in front
        for (int i = 0; i < 6000; i++) { w.Step(); CheckSpacing(w); }
        int full = OnBelts(w);
        for (int i = 0; i < 200; i++) { w.Step(); CheckSpacing(w); }
        Check(OnBelts(w) == full, $"belt count changed after backing up: {full} -> {OnBelts(w)}");
        Check(full >= 36, $"expected about 40 items backed up on 10 belts, got {full}");
        long produced = w.Entities.OfType<Miner>().Sum(m => m.Produced);
        long held = w.Entities.OfType<Miner>().Count(m => m.Buffer != null);
        Check(produced == full + held, $"items lost: produced {produced}, on belts {full}, in miners {held}");
    }

    static World ChainWorld()
    {
        var w = Sandbox();
        w.ForcedOre[new(0, 0)] = "iron_ore";
        w.Place("hub", new(10, -1), 0, force: true);
        w.Place("miner", new(0, 0), 0);
        Belts(w, 0, (1, 0), (2, 0), (3, 0));
        w.Place("inserter", new(4, 0), 0);
        w.Place("furnace", new(5, 0), 0);
        w.Place("inserter", new(6, 0), 0);
        Belts(w, 0, (7, 0), (8, 0), (9, 0));
        return w;
    }

    static long InTransit(World w)
    {
        var all = new List<string>();
        foreach (var e in w.Entities) e.Contents(all);
        return all.Count;
    }

    static void Chain()
    {
        var w = ChainWorld();
        for (int i = 0; i < 3000; i++) { w.Step(); CheckSpacing(w); }
        long produced = w.Entities.OfType<Miner>().Sum(m => m.Produced);
        long delivered = w.Storage.GetValueOrDefault("iron_plate");
        Check(delivered > 20, $"only {delivered} plates reached the hub");
        Check(produced == delivered + InTransit(w), $"items lost: produced {produced}, delivered {delivered}, in transit {InTransit(w)}");
    }

    static void Refunds()
    {
        var w = World.New("t", "normal", false, 1);
        w.ForcedOre[new(8, 0)] = "iron_ore";
        w.AddStorage("iron_plate", 100);
        long plates = w.Storage["iron_plate"], stone = w.Storage["stone"];
        Check(w.Place("miner", new(8, 0), 0) != null, "miner not placed");
        Belts(w, 0, (9, 0), (10, 0), (11, 0));
        Check(w.Storage["iron_plate"] == plates - 13, "costs not deducted");
        Check(w.Place("furnace", new(20, 20), 0) != null && w.Storage["stone"] == stone - 8, "furnace cost not deducted");
        for (int i = 0; i < 600; i++) w.Step();
        long produced = w.Entities.OfType<Miner>().Sum(m => m.Produced);
        foreach (var e in w.Entities.ToList()) w.Remove(e.Pos);
        Check(w.Entities.Count == 1, "hub should stay");
        Check(w.Storage["iron_plate"] == plates && w.Storage["stone"] == stone, "costs not refunded");
        Check(w.Storage.GetValueOrDefault("iron_ore") == produced, $"contents lost: produced {produced}, refunded {w.Storage.GetValueOrDefault("iron_ore")}");
        Check(w.Place("miner", new(30, 30), 0) == null, "miner placed off ore");
    }

    static void RoundTrip()
    {
        foreach (var make in new Func<World>[] { ChainWorld, AssemblerWorld, EconomyWorld, FluidWorld, TunnelWorld, LightningWorld })
        {
            var a = make();
            for (int i = 0; i < 1000; i++) a.Step();
            var b = World.FromSave(Persist.Deserialize(Persist.Serialize(a.ToSave())));
            foreach (var kv in a.ForcedOre) b.ForcedOre[kv.Key] = kv.Value;
            for (int i = 0; i < 1000; i++) { a.Step(); b.Step(); }
            var sa = a.ToSave(); var sb = b.ToSave();
            sa.SavedAt = sb.SavedAt = null;
            sa.LastSession = sb.LastSession = null; // session stats restart on load by design
            Check(Persist.Serialize(sa) == Persist.Serialize(sb), $"loaded world diverged from the original ({make.Method.Name})");
        }
    }

    static World AssemblerWorld()
    {
        var w = Sandbox();
        w.Place("hub", new(10, -1), 0, force: true);
        var chest = (Chest)w.Place("chest", new(0, 0), 0);
        chest.Inv["iron_plate"] = 100;
        w.Place("inserter", new(1, 0), 0);
        w.SetRecipe((Assembler)w.Place("assembler", new(2, 0), 0), "gear");
        w.Place("inserter", new(3, 0), 0);
        Belts(w, 0, (4, 0), (5, 0), (6, 0), (7, 0), (8, 0), (9, 0));
        return w;
    }

    // Normal mode with coins, auto-sell, a finished milestone and an upgrade, for the save round trip.
    static World EconomyWorld()
    {
        var w = World.New("t", "normal", false, 1);
        w.ForcedOre[new(8, 0)] = "iron_ore";
        w.AddStorage("iron_plate", 500);
        w.AddStorage("copper_plate", 50);
        w.MilestoneIndex = 1;
        w.Delivered["gear"] = 3;
        w.Coins = 77;
        w.AutoSell.Add("iron_ore");
        w.Reserve["iron_ore"] = 5;
        w.Upgrades["machine_speed"] = 2;
        w.Place("miner", new(8, 0), 0);
        Belts(w, 0, (9, 0), (10, 0));
        var ins = (Inserter)w.Place("inserter", new(11, 0), 0);
        ins.Filter = "iron_ore";
        w.Place("chest", new(12, 0), 0);
        w.SetRecipe((Assembler)w.Place("assembler", new(14, 0), 0), "copper_wire");
        // a crossing mid-line, so its lanes go through the save round trip too
        w.ForcedOre[new(8, 3)] = "iron_ore";
        w.Place("miner", new(8, 3), 0);
        Belts(w, 0, (9, 3));
        w.Place("crossing", new(10, 3), 0);
        Belts(w, 0, (11, 3));
        // and a powered pair, so generator fuel/energy is part of the round trip
        w.Place("pole", new(9, 1), 0);
        ((Generator)w.Place("generator", new(10, 1), 0)).Fuel = 5;
        // a potion that runs out halfway through the round trip
        Check(w.BuyPotion("rock_crusher", 5f, 1), "potion purchase in the economy world");
        w.Upgrades["sell_price"] = 1;
        return w;
    }

    static void AssemblerChain()
    {
        var w = AssemblerWorld();
        for (int i = 0; i < 3000; i++) w.Step();
        var all = new List<string>();
        foreach (var e in w.Entities) e.Contents(all);
        long plates = all.Count(i => i == "iron_plate");
        long gears = all.Count(i => i == "gear") + w.Storage.GetValueOrDefault("gear");
        Check(w.Storage.GetValueOrDefault("gear") >= 40, $"only {w.Storage.GetValueOrDefault("gear")} gears delivered");
        Check(plates + gears * 2 == 100, $"plates lost: {plates} plates + {gears} gears x2 != 100");
    }

    static void MetalParts()
    {
        foreach (var m in new[] { "iron", "copper", "steel" })
            foreach (var p in Defs.Parts)
                Check(Defs.RecipeById($"{m}_{p.Id}") is { Machine: "assembler" } r && r.In[0].Item == m + "_plate" && r.OutCount == p.PerPlate && Defs.Items[r.Out].Price > 0, $"{m}_{p.Id} is missing or wrong");
        Check(Defs.RecipeById("lightning_rod") is { } lr && lr.In.Any(x => x.Item == "copper_rod") && Defs.Items["lightning_rod"].Price > 0, "lightning rod is missing");
        var w = AssemblerWorld();
        w.SetRecipe(w.Entities.OfType<Assembler>().First(), "iron_screw");
        for (int i = 0; i < 3000; i++) w.Step();
        var all = new List<string>();
        foreach (var e in w.Entities) e.Contents(all);
        long plates = all.Count(i => i == "iron_plate"), screws = all.Count(i => i == "iron_screw") + w.Storage.GetValueOrDefault("iron_screw");
        Check(w.Storage.GetValueOrDefault("iron_screw") >= 40, $"only {w.Storage.GetValueOrDefault("iron_screw")} screws delivered");
        Check(plates * 4 + screws == 400, $"plates lost: {plates} plates x4 + {screws} screws != 400");
        var b = World.FromSave(Persist.Deserialize(Persist.Serialize(w.ToSave())));
        Check(b.Storage.GetValueOrDefault("iron_screw") == w.Storage["iron_screw"] && b.Entities.OfType<Assembler>().First().RecipeId == "iron_screw", "screws or the recipe got lost in the save");
    }

    // A 30-tile line backs up full from item sources, then a void opens at its end: the backed-up queue drains
    // at exactly what the line can carry. mid: a splitter (x 28) or an underground pair (x 25 and 28) near the end.
    // Returns items per second at the void.
    static float FullLoad(string belt, string mid, float rush = 1)
    {
        var w = Sandbox();
        if (rush > 1) { w.Coins = 1_000_000_000; Check(w.BuyPotion("belt_rush", rush, 60), "buying Belt Rush"); }
        var src = new List<ItemSource> { (ItemSource)w.Place("source", new(0, 0), 0, config: "gear") };
        foreach (int x in new[] { 1, 2, 3, 4, 5 }) src.Add((ItemSource)w.Place("source", new(x, 1), 3, config: "gear"));
        bool split = mid != null && Defs.BaseOf(mid) is "splitter" or "crossing";
        for (int x = 1; x <= 30; x++)
            if (mid == null || (split ? x != 28 : x is < 25 or > 28)) w.Place(belt, new(x, 0), 0);
        if (split) w.Place(mid, new(28, 0), 0);
        else if (mid != null) { w.Place(mid, new(25, 0), 0); w.Place(mid, new(28, 0), 0); }
        long made = 0;
        void Run(int ticks) { for (int t = 0; t < ticks; t++) { w.Step(); CheckSpacing(w); made += src.Count(s => s.Working); } }
        Run(2000);
        var sink = (ItemVoid)w.Place("void", new(31, 0), 0);
        int window = rush > 1 ? 10 : 40; // a rushed line drains the backed-up queue faster than the sources refill it
        Run(window / 4);
        long before = sink.Eaten;
        Run(window);
        float rate = (sink.Eaten - before) * World.TicksPerSecond / (float)window;
        Run(300);
        var all = new List<string>();
        foreach (var e in w.Entities) e.Contents(all);
        Check(made == sink.Eaten + all.Count, $"{belt}/{mid}: made {made}, eaten {sink.Eaten}, in transit {all.Count}");
        return rate;
    }

    // A pole with ten rods around it: lightning strikes one about every 9 s.
    static World LightningWorld()
    {
        var w = Sandbox();
        w.Place("pole", new(0, 0), 0);
        for (int x = -2; x <= 2; x++) { w.Place("lightning_rod", new(x, -2), 0); w.Place("lightning_rod", new(x, 2), 0); }
        w.Place("chest", new(2, 0), 0); // something that isn't a rod, in reach
        return w;
    }

    static void LightningRods()
    {
        var w = LightningWorld();
        var rods = w.Entities.OfType<LightningRod>().ToList();
        var net = w.NetOf(rods[0]);
        Check(net != null && net.Rods.Count == rods.Count, "every rod should join the pole's network");
        var struck = new Dictionary<LightningRod, long>();
        int strikes = 0, charged = 0;
        for (int t = 0; t < 6000; t++)
        {
            w.Step();
            // Power is worked out at the start of a tick, from the charges the tick before.
            Check(net.Capacity == charged * Defs.LightningKW, $"capacity {net.Capacity} with {charged} charged rods");
            while (w.Events.TryDequeue(out var e))
                if (e.Kind == "lightning")
                {
                    strikes++;
                    if (w.At(e.Tile) is LightningRod r) struck[r] = w.Tick;
                    else Check(false, $"lightning hit {e.Tile}, which is no rod");
                }
            foreach (var (r, at) in struck)
                if (w.Tick == at + Defs.LightningTicks) Check(r.Charge == 0, $"the bonus should end {Defs.LightningTicks} ticks after the strike");
            charged = rods.Count(r => r.Charge > 0);
        }
        Check(strikes >= 15, $"ten rods over 5 minutes should see plenty of lightning (got {strikes})");
    }

    static void OreLayout()
    {
        string[] old = { "iron_ore", "copper_ore", "stone", "coal" };
        foreach (int seed in new[] { 1, 42, 4242, 99991 })
        {
            var w = new World(seed);
            var noise = new FastNoiseLite[old.Length];
            for (int i = 0; i < old.Length; i++) noise[i] = new FastNoiseLite { Seed = seed + i * 101, Frequency = 0.035f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
            string Before(Vector2I t) // OreAt as it was with four ores
            {
                float d = ((Vector2)t).Length();
                if (d < 6) return null;
                for (int i = 0; i < old.Length; i++)
                {
                    float a = Mathf.Tau * (i + 0.5f) / old.Length + seed % 7;
                    var c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 13f;
                    if ((t - c).Length() < 4.2f + 1.3f * noise[i].GetNoise2D(t.X * 3, t.Y * 3)) return old[i];
                }
                if (d < 22) return null;
                for (int i = 0; i < old.Length; i++) if (noise[i].GetNoise2D(t.X, t.Y) > 0.5f) return old[i];
                return null;
            }
            var near = new HashSet<string>();
            int moved = 0;
            for (int x = -80; x <= 80; x++)
                for (int y = -80; y <= 80; y++)
                {
                    var t = new Vector2I(x, y);
                    string was = Before(t), now = w.OreAt(t);
                    if (was != null && now != was || was == null && Array.IndexOf(old, now) >= 0) moved++;
                    if (now != null && ((Vector2)t).Length() < 26) near.Add(now);
                }
            Check(moved == 0, $"seed {seed}: {moved} tiles changed their old ore");
            var missing = Defs.Ores.Where(o => !near.Contains(o)).ToList();
            Check(missing.Count == 0, $"seed {seed}: no starter patch for {string.Join(", ", missing)}");
        }
    }

    static void ItemGroups()
    {
        foreach (var (id, group) in new[] { ("coal", "Ores"), ("brick", "Plates"), ("steel_plate", "Plates"), ("copper_nail", "Metal parts"), ("steel_rod", "Metal parts"),
                                            ("lightning_rod", "Products"), ("gear", "Intermediates"), ("solar_panel", "Intermediates"), ("sky_beacon", "Products"), ("water", "Fluids"),
                                            ("bronze_plate", "Plates"), ("zinc_rod", "Metal parts"), ("explosive", "Products"), ("concrete", "Intermediates"), ("steam", "Fluids") })
            Check(Defs.GroupOf(id) == group, $"{id} is in {Defs.GroupOf(id)}, expected {group}");
    }

    static void BeltRush()
    {
        float plain = FullLoad("belt", null);
        foreach (var mid in new string[] { null, "tunnel", "crossing" })
        {
            float rushed = FullLoad("belt", mid, 10);
            GD.Print($"         belt{(mid != null ? " + " + mid : "")} x10: {rushed:0.#} items/s (plain {plain:0.#})");
            Check(rushed >= plain * 9, $"Belt Rush x10 on belt/{mid} carries {rushed:0.#} items/s, plain {plain:0.#}");
        }
    }

    static void CarriersAtFullLoad()
    {
        float last = 0;
        foreach (var belt in Defs.BeltTiers)
        {
            float rate = FullLoad(belt, null), cap = Defs.BeltSpeed[belt] * 80;
            GD.Print($"         {belt}: {rate:0.#} items/s (rated {cap:0})");
            Check(rate > last && rate >= cap * 0.9f, $"{belt} carries {rate:0.#} items/s, rated {cap:0}");
            last = rate;
        }
        float crossing = FullLoad("ultra_belt", "crossing");
        Check(crossing >= 36, $"a crossing in an ultra belt line carries only {crossing} items/s");
        foreach (var family in new[] { Defs.TiersOf("splitter"), Defs.TiersOf("tunnel") })
        {
            last = 0;
            foreach (var mid in family)
            {
                float rate = FullLoad("ultra_belt", mid), cap = Defs.BeltSpeed[mid] * 80;
                GD.Print($"         {mid}: {rate:0.#} items/s (rated {cap:0})");
                Check(rate > last && rate >= cap * 0.9f, $"{mid} carries {rate:0.#} items/s, rated {cap:0}");
                last = rate;
            }
        }
    }

    static void MachineTiers()
    {
        foreach (var family in new[] { Defs.TiersOf("miner"), Defs.TiersOf("furnace") })
        {
            var made = new List<long>();
            foreach (var type in family)
            {
                var w = Sandbox();
                w.ForcedOre[new(0, 0)] = "iron_ore";
                if (type.StartsWith("miner")) w.Place(type, new(0, 0), 0);
                else { var c = (Chest)w.Place("chest", new(0, 0), 0); c.Inv["iron_ore"] = 200; w.Place("inserter_mk3", new(1, 0), 0); w.Place(type, new(2, 0), 0); w.Place("inserter_mk3", new(3, 0), 0); }
                var sink = (ItemVoid)w.Place("void", type.StartsWith("miner") ? new(1, 0) : new(4, 0), 0);
                for (int t = 0; t < 1200; t++) w.Step();
                made.Add(sink.Eaten);
            }
            Check(Math.Abs(made[1] - made[0] * 2) <= 2 && Math.Abs(made[2] - made[0] * 3) <= 3, $"{family[0]} tiers made {string.Join(", ", made)}");
        }
        Check(Defs.PowerOf("assembler_mk3") == Defs.PowerUse["assembler"] * 3, "Mk3 should draw three times the power");
    }

    static void UpgradeInPlace()
    {
        var w = World.New("t", "normal", false, 1);
        w.MilestoneIndex = Defs.Milestones.Length;
        foreach (var item in new[] { "iron_plate", "copper_plate", "steel_plate", "gear", "circuit", "steel_screw", "steel_rod", "iron_rod" }) w.AddStorage(item, 500);
        var asm = w.Place("assembler", new(10, 10), 0, config: "gear");
        long steel = w.Storage["steel_plate"], iron = w.Storage["iron_plate"];
        w.BeginAction();
        var mk2 = w.Replace("assembler_mk2", new(10, 10), 0);
        w.EndAction();
        Check(mk2 is Assembler { Type: "assembler_mk2", RecipeId: "gear" } && w.At(new(10, 10)) == mk2, "the assembler should become an Mk2 with its recipe");
        Check(w.Storage["steel_plate"] == steel - 4 && w.Storage["iron_plate"] == iron + 10, "the Mk2 is paid and the old assembler refunded");
        Check(w.Undo() == 0 && w.At(new(10, 10)) is Assembler { Type: "assembler", RecipeId: "gear" } && w.Storage["iron_plate"] == iron, "undo brings the old assembler back");
        Check(!w.CanReplace("miner_mk2", new(10, 10)) && !w.CanReplace("assembler", new(10, 10)), "only another tier of the same building can replace it");
        w.Place("tunnel", new(10, 12), 0);
        w.Place("tunnel", new(13, 12), 0);
        Check(w.Replace("fast_tunnel", new(13, 12), 0) != null, "replacing the exit of an underground pair");
        var a = w.At(new(10, 12)); var b = w.At(new(13, 12));
        Check(a.Type == "fast_tunnel" && b.Type == "fast_tunnel" && !a.Exit && b.Exit && w.Partner(a) == b, "both ends become fast and stay paired");
    }

    static void MusicFolder()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tinyworks_music_test");
        System.IO.Directory.CreateDirectory(dir);
        foreach (var f in new[] { "b.ogg", "a.MP3", "c.wav", "notes.txt", "cover.png" }) System.IO.File.WriteAllText(System.IO.Path.Combine(dir, f), "");
        var names = TinyWorks.View.Sfx.Tracks(dir).Select(System.IO.Path.GetFileName).ToList();
        System.IO.Directory.Delete(dir, true);
        Check(string.Join(",", names) == "a.MP3,b.ogg,c.wav", $"got {string.Join(",", names)}");
        Check(TinyWorks.View.Sfx.Tracks(null).Count == 0 && TinyWorks.View.Sfx.Tracks(dir).Count == 0, "no folder or a missing one should give no tracks");
    }

    static void ChestAndFilter()
    {
        var w = Sandbox();
        w.ForcedOre[new(0, 0)] = "iron_ore";
        w.ForcedOre[new(0, 2)] = "iron_ore";
        w.Place("hub", new(7, 1), 0, force: true);
        w.Place("miner", new(0, 0), 0);
        Belts(w, 0, (1, 0), (2, 0));
        ((Inserter)w.Place("inserter", new(3, 0), 0)).Filter = "copper_ore";
        var blocked = (Chest)w.Place("chest", new(4, 0), 0);
        w.Place("miner", new(0, 2), 0);
        Belts(w, 0, (1, 2), (2, 2));
        ((Inserter)w.Place("inserter", new(3, 2), 0)).Filter = "iron_ore";
        w.Place("chest", new(4, 2), 0);
        w.Place("inserter", new(5, 2), 0);
        Belts(w, 0, (6, 2)); // into the hub at (7,2)
        for (int i = 0; i < 2000; i++) w.Step();
        Check(blocked.Count == 0, "filter let the wrong item through");
        Check(w.Storage.GetValueOrDefault("iron_ore") > 10, "nothing came through the chest");
        long produced = w.Entities.OfType<Miner>().Sum(m => m.Produced);
        Check(produced == InTransit(w) + w.Storage.GetValueOrDefault("iron_ore"), "items lost on the chest line");
    }

    static void CrossingLines()
    {
        var w = Sandbox();
        w.ForcedOre[new(0, 2)] = "iron_ore";
        w.ForcedOre[new(2, 0)] = "copper_ore";
        w.Place("miner", new(0, 2), 0);          // iron, heading right
        Belts(w, 0, (1, 2), (3, 2));
        var east = (Chest)w.Place("chest", new(4, 2), 0);
        w.Place("miner", new(2, 0), 1);          // copper, heading down
        Belts(w, 1, (2, 1), (2, 3));
        var south = (Chest)w.Place("chest", new(2, 4), 0);
        w.Place("crossing", new(2, 2), 0);
        for (int i = 0; i < 2000; i++) w.Step();
        Check(east.Inv.Keys.All(k => k == "iron_ore") && east.Count > 30, $"east chest: {string.Join(",", east.Inv)}");
        Check(south.Inv.Keys.All(k => k == "copper_ore") && south.Count > 30, $"south chest: {string.Join(",", south.Inv)}");
        long produced = w.Entities.OfType<Miner>().Sum(m => m.Produced);
        Check(produced == InTransit(w), "items lost in the crossing");
    }

    // A miner into a chest, optionally with a pole and a generator holding `coal`.
    static (World w, Miner m, Generator g) PowerRig(bool powered, int coal)
    {
        var w = Sandbox();
        w.ForcedOre[new(0, 0)] = "iron_ore";
        var m = (Miner)w.Place("miner", new(0, 0), 0);
        w.Place("chest", new(1, 0), 0);
        Generator g = null;
        if (powered)
        {
            w.Place("pole", new(0, 2), 0);
            g = (Generator)w.Place("generator", new(1, 2), 0);
            g.Fuel = coal;
        }
        return (w, m, g);
    }

    static void PowerBonus()
    {
        var (plain, mPlain, _) = PowerRig(false, 0);
        var (powered, mPow, gen) = PowerRig(true, 10);
        var (dry, mDry, _) = PowerRig(true, 0);
        for (int i = 0; i < 800; i++) { plain.Step(); powered.Step(); dry.Step(); }
        Check(powered.NetOf(mPow) != null && powered.NetOf(gen) == powered.NetOf(mPow), "miner and generator not on the pole's network");
        Check(mPow.Produced >= mPlain.Produced * 19 / 10, $"powered made {mPow.Produced}, unpowered {mPlain.Produced}: expected about double");
        Check(mDry.Produced == mPlain.Produced, $"generator without coal changed speed ({mDry.Produced} vs {mPlain.Produced})");
        // At most 800 ticks at 90 kW = 3600 kJ (a little less: the miner pauses a tick per ore), so exactly one coal got lit.
        float used = Defs.CoalKJ - gen.Energy;
        Check(gen.Fuel == 9 && used > 3300 && used <= 3600, $"coal use wrong: fuel {gen.Fuel}, used {used} kJ");
    }

    static void SolarPower()
    {
        var w = Sandbox();
        w.ForcedOre[new(0, 0)] = "iron_ore";
        var m = (Miner)w.Place("miner", new(0, 0), 1);
        w.Place("chest", new(0, 1), 0);
        w.Place("pole", new(2, 0), 0);
        var sun = w.Place("solar", new(3, 0), 0);
        var g = (Generator)w.Place("generator", new(2, 1), 0);
        g.Fuel = 10;
        for (int i = 0; i < 400; i++) w.Step();
        var net = w.NetOf(m);
        Check(net != null && net.Solars.Contains(sun) && net.Capacity == Defs.GeneratorKW + Defs.SolarKW, $"solar not on the network (capacity {net?.Capacity})");
        Check(net.Satisfaction == 1 && g.Fuel == 10 && g.Energy == 0, $"a 90 kW miner under 100 kW of solar should burn no coal (fuel {g.Fuel}, energy {g.Energy})");
        Check(w.PowerBoost(m) == 2, "solar power should give the full bonus");
    }

    static void LongWires()
    {
        var w = Sandbox();
        var a = w.Place("pole", new(0, 0), 0);
        var b = w.Place("pole", new(9, 0), 0);
        Check(w.NetOf(a) != w.NetOf(b), "9 tiles apart is out of a normal pole's reach");
        w.Coins = 1000;
        Check(w.BuyUpgrade("pole_range"), "couldn't buy Long Wires");
        Check(w.NetOf(a) == w.NetOf(b), "one level of Long Wires should link poles 9 tiles apart");
    }

    static void PowerShortage()
    {
        // 10 miners (900 kW) on one 600 kW generator: 2/3 satisfaction, so about 1.67x speed.
        var w = Sandbox();
        w.Place("pole", new(2, 2), 0);
        w.Place("pole", new(7, 2), 0);
        var g = (Generator)w.Place("generator", new(2, 4), 0);
        g.Fuel = 10;
        var miners = new System.Collections.Generic.List<Miner>();
        for (int x = 0; x < 10; x++)
        {
            w.ForcedOre[new(x, 0)] = "iron_ore";
            miners.Add((Miner)w.Place("miner", new(x, 0), 1));
            w.Place("chest", new(x, 1), 0);
        }
        for (int i = 0; i < 400; i++) w.Step();
        var net = w.NetOf(g);
        Check(net.Users.Count == 10, $"expected 10 miners on the network, got {net.Users.Count}");
        Check(Mathf.Abs(net.Satisfaction - 600f / 900f) < 0.01f, $"satisfaction {net.Satisfaction}");
        long made = miners.Sum(m => m.Produced);
        Check(made >= 10 * 400 * 1.6 / Miner.Interval && made <= 10 * 400 * 1.75 / Miner.Interval, $"10 miners made {made} in 400 ticks");
    }

    static void PowerNetworks()
    {
        var w = Sandbox();
        w.Place("pole", new(0, 0), 0);
        w.Place("pole", new(6, 0), 0);
        w.Place("pole", new(20, 0), 0);
        Check(w.Nets.Count == 2 && w.Nets.Any(n => n.Poles.Count == 2 && n.Wires.Count == 1), $"expected 2 networks, got {w.Nets.Count}");
        Check(w.NetOf(w.At(new(0, 0))) == w.NetOf(w.At(new(6, 0))) && w.NetOf(w.At(new(0, 0))) != null, "a pole should know its own network");
        w.Place("pole", new(13, 0), 0); // bridges the gap
        Check(w.Nets.Count == 1 && w.Nets[0].Wires.Count == 3, $"bridge pole should join everything ({w.Nets.Count} nets)");
        w.Remove(new(13, 0));
        Check(w.Nets.Count == 2, "removing the bridge should split the network again");
    }

    static void Milestones()
    {
        var w = World.New("t", "normal", false, 1);
        Check(!w.IsUnlocked("assembler"), "assembler unlocked too early");
        for (int i = 0; i < 39; i++) w.Deliver("iron_plate", Vector2I.Zero);
        for (int i = 0; i < 25; i++) w.Deliver("copper_plate", Vector2I.Zero);
        Check(w.MilestoneIndex == 0 && w.Delivered["copper_plate"] == 20, "progress wrong before the last item");
        w.Deliver("iron_plate", Vector2I.Zero);
        Check(w.MilestoneIndex == 1 && w.IsUnlocked("assembler") && w.IsUnlocked("chest") && w.Delivered.Count == 0, "milestone 1 did not complete");
        Check(w.Events.Any(e => e.Kind == "milestone"), "no milestone event");
        Check(w.CanPlace("assembler", new(30, 30), out var why), $"assembler still not placeable: {why}");
        Check(!w.CanPlace("fast_belt", new(31, 30), out _), "fast belt placeable before milestone 2");
    }

    static void AutoSell()
    {
        var w = World.New("t", "normal", false, 1);
        w.Storage["iron_plate"] = 120;
        w.AutoSell.Add("iron_plate");
        for (int i = 0; i < 20; i++) w.Step();
        Check(w.Storage["iron_plate"] == World.DefaultReserve && w.Coins == 70 * 3, $"after sale: {w.Storage["iron_plate"]} plates, {w.Coins} coins");
        w.Reserve["iron_plate"] = 0;
        for (int i = 0; i < 20; i++) w.Step();
        Check(w.Storage["iron_plate"] == 0 && w.Coins == 120 * 3, "reserve 0 should sell everything");
        w.Coins = 100;
        Check(w.BuyUpgrade("machine_speed") && w.Up("machine_speed") == 1 && w.Coins == 0, "upgrade purchase");
        Check(!w.BuyUpgrade("machine_speed"), "upgrade bought without coins");
        w.Coins = World.RawCost("coal");
        Check(w.BuyRaw("coal") && w.Storage["coal"] == World.RawBatch && w.Coins == 0, "raw purchase");
    }

    static void SpeedUpgrade()
    {
        long Run(int level)
        {
            var w = Sandbox();
            w.ForcedOre[new(0, 0)] = "iron_ore";
            w.Upgrades["machine_speed"] = level;
            w.Place("miner", new(0, 0), 0);
            w.Place("chest", new(1, 0), 0);
            for (int i = 0; i < 800; i++) w.Step();
            return w.Entities.OfType<Miner>().Sum(m => m.Produced);
        }
        long slow = Run(0), fast = Run(5);
        Check(fast * 10 >= slow * 14, $"level 5 made {fast} vs {slow} at level 0");
    }

    // Every milestone's items and every unlocked building's cost must be makeable with what came before.
    static void Producible()
    {
        var avail = new HashSet<string>(Defs.Ores);
        for (int t = 0; t <= Defs.Milestones.Length; t++)
        {
            var unlocked = new HashSet<string>(Defs.BaseUnlocks);
            for (int i = 0; i < t; i++) unlocked.UnionWith(Defs.Milestones[i].Unlocks);
            if (unlocked.Contains("pump")) avail.Add("water");
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var r in Defs.Recipes)
                    if (unlocked.Contains(r.Machine) && (r.Unlock == null || unlocked.Contains(r.Unlock)) && r.In.All(x => avail.Contains(x.Item)))
                        grew |= avail.Add(r.Out);
            }
            foreach (var b in unlocked.Where(Defs.Buildings.ContainsKey))
                foreach (var (item, _) in Defs.Buildings[b].Cost)
                    Check(avail.Contains(item), $"{b} costs {item}, which can't be made by tier {t}");
            if (t < Defs.Milestones.Length)
                foreach (var (item, _) in Defs.Milestones[t].Need)
                    Check(avail.Contains(item), $"milestone {t + 1} needs {item}, which can't be made yet");
        }
        // Every item can be made in the end, and is sold (has a price) or, for fluids, used by a recipe or a generator.
        foreach (var (id, d) in Defs.Items)
        {
            Check(avail.Contains(id), $"{id} can never be made");
            Check(d.Fluid ? Defs.GasKJ.ContainsKey(id) || Defs.Recipes.Any(r => r.In.Any(x => x.Item == id)) : d.Price > 0, $"{id} is neither sold nor used");
        }
        // Only refineries take fluids, and at most one (from the pipe behind them).
        foreach (var r in Defs.Recipes)
            Check(r.In.Count(x => Defs.IsFluid(x.Item)) <= (r.Machine == "refinery" ? 1 : 0) && (r.Machine == "refinery" || !Defs.IsFluid(r.Out)), $"{r.Id} breaks the fluid rule");
    }

    // Where an item was drawn last tick (Prev) must be where it really was, also right after it changed belts.
    static void GlideAcrossEdges()
    {
        var w = ChainWorld(); // miner, straight belts, furnace, belts, hub: free flowing
        var last = new List<float>();
        for (int t = 0; t < 600; t++)
        {
            w.Step();
            var now = new List<float>();
            foreach (var b in w.Entities.OfType<Belt>().Where(b => b.Rot == 0 && b.InputFrom(w) == b.Back))
                foreach (var it in b.Items)
                {
                    now.Add(b.Pos.X + it.Pos);
                    bool fedByBelt = w.At(b.Back) is Belt;
                    if (t > 0 && fedByBelt)
                        Check(last.Any(x => Mathf.Abs(x - (b.Pos.X + it.Prev)) < 1e-3f), $"item jumped over the edge into {b.Pos} at tick {w.Tick}");
                }
            last = now;
        }
    }

    static World TunnelWorld()
    {
        var w = Sandbox();
        w.ForcedOre[new(0, 0)] = "iron_ore";
        w.Place("hub", new(10, -1), 0, force: true);
        w.Place("miner", new(0, 0), 0);
        Belts(w, 0, (1, 0));
        w.Place("tunnel", new(2, 0), 0);
        w.Place("chest", new(3, 0), 0); // in the way on the surface
        w.Place("furnace", new(4, 0), 0);
        w.Place("tunnel", new(5, 0), 0);
        Belts(w, 0, (6, 0), (7, 0), (8, 0), (9, 0));
        w.Place("splitter", new(7, 2), 0); // an idle splitter too, for the round trip
        return w;
    }

    static void UndoRedo()
    {
        var w = World.New("t", "normal", false, 1);
        w.AddStorage("iron_plate", 100);
        w.AddStorage("copper_plate", 20);
        w.MilestoneIndex = 1; // assemblers unlocked
        long plates = w.Storage["iron_plate"];
        w.BeginAction();
        Belts(w, 0, (5, 0), (6, 0), (7, 0));
        w.SetRot(new(7, 0), 1);
        w.EndAction();
        w.BeginAction();
        var asm = w.Place("assembler", new(5, 2), 0, config: "gear");
        w.EndAction();
        Check(asm is Assembler { RecipeId: "gear" }, "placing with a config should set the recipe");
        w.BeginAction(); w.Remove(new(5, 2)); w.EndAction();
        Check(w.Undo() == 0 && w.At(new(5, 2)) is Assembler { RecipeId: "gear" }, "undoing a removal should bring the assembler back with its recipe");
        Check(w.Undo() == 0 && w.At(new(5, 2)) == null, "undoing the placement should remove it");
        Check(w.Undo() == 0 && w.At(new(5, 0)) == null && w.At(new(7, 0)) == null, "one undo should take back the whole belt drag");
        Check(w.Storage["iron_plate"] == plates, $"undo should refund everything: {w.Storage["iron_plate"]} != {plates}");
        Check(w.Undo() == -1, "nothing left to undo");
        Check(w.Redo() == 0 && w.At(new(7, 0)) is Belt { Rot: 1 } && w.At(new(5, 0)) is Belt, "redo should replay the drag, turn included");
        Check(w.Storage["iron_plate"] == plates - 3, "redo should pay again");
        w.Storage["iron_plate"] = 0;
        w.Storage.Remove("copper_plate");
        Check(w.Redo() == 1, "redo without materials should report the building it couldn't place");
    }

    static void PasteTurned()
    {
        var w = Sandbox();
        w.Place("tunnel", new(0, 0), 0);
        w.Place("tunnel", new(3, 0), 0);
        w.Place("inserter", new(1, 1), 0, config: "gear");
        var clip = w.Copy(new Rect2I(0, 0, 4, 2)).Select(p => p.Turned()).ToList();
        Check(clip.Count == 3, $"expected 3 parts, got {clip.Count}");
        var at = new Vector2I(20, 20);
        Check(w.Paste(clip, at) == 0, "turned paste should fit on empty ground");
        var pieces = w.Entities.OfType<Tunnel>().Where(t => t.Pos.X >= 10).ToList();
        Check(pieces.Count == 2 && pieces.All(t => t.Rot == 1), "pasted tunnels should face down");
        var entrance = pieces.Single(t => !t.Exit);
        Check(w.Partner(entrance) is { Exit: true }, "pasted tunnels should still be a pair");
        Check(w.Entities.OfType<Inserter>().Any(i => i.Pos.X >= 10 && i.Filter == "gear"), "the inserter's filter should come along");
        Check(w.Paste(clip, at) == 3, "pasting onto itself should place nothing");
    }

    static void SourceVoid()
    {
        var w = Sandbox();
        var src = (ItemSource)w.Place("source", new(0, 0), 0, config: "gear");
        Belts(w, 0, (1, 0), (2, 0), (3, 0));
        var sink = (ItemVoid)w.Place("void", new(4, 0), 0);
        long made = 0;
        for (int i = 0; i < 400; i++) { w.Step(); if (src.Working) made++; }
        Check(sink.Eaten > 50, $"only {sink.Eaten} items reached the void");
        Check(made == sink.Eaten + OnBelts(w), $"items lost: made {made}, eaten {sink.Eaten}, on belts {OnBelts(w)}");
        var normal = World.New("t", "normal", false, 1);
        Check(!normal.CanPlace("source", new(9, 9), out var why) && why.Contains("sandbox"), "item source must stay a sandbox tool");
    }

    static void StatsAndPause()
    {
        var w = Sandbox();
        w.ForcedOre[new(0, 0)] = "iron_ore";
        var m = (Miner)w.Place("miner", new(0, 0), 0);
        w.Place("chest", new(1, 0), 0);
        for (int i = 0; i < World.StatBucketTicks * 3; i++) w.Step();
        long counted = w.History.Sum(b => b.GetValueOrDefault("iron_ore"));
        Check(w.History.Count == 3 && counted == m.Produced, $"counted {counted} ore in {w.History.Count} buckets, miner made {m.Produced}");
        Check(Math.Abs(w.PerMinute("iron_ore") - counted * 2) < 0.01f, $"per minute should be {counted * 2}, got {w.PerMinute("iron_ore")}");
        var save = w.ToSave();
        var back = World.FromSave(Persist.Deserialize(Persist.Serialize(save)));
        Check(back.LastSession?.Made == counted && back.Session.Made == 0, "the save should carry last session's stats, and a fresh session should start at 0");
    }

    static void Tunnels()
    {
        var w = TunnelWorld();
        var a = w.At(new(2, 0)); var b = w.At(new(5, 0));
        Check(!a.Exit && b.Exit, "second underground belt should become the exit");
        Check(w.Partner(a) == b && w.Partner(b) == a, "underground pair not linked");
        Check(((Belt)w.At(new(6, 0))).InputFrom(w) == new Vector2I(5, 0), "belt after the exit should take items from it straight on");
        for (int i = 0; i < 3000; i++) w.Step();
        var miner = (Miner)w.At(new(0, 0));
        long delivered = w.Storage.GetValueOrDefault("iron_ore");
        Check(delivered > 30, $"only {delivered} ore came through the tunnel");
        Check(miner.Produced == delivered + InTransit(w), $"items lost: produced {miner.Produced}, delivered {delivered}, in transit {InTransit(w)}");
        // too far apart: a lone entrance holds its items instead of losing them
        var lone = Sandbox();
        lone.Place("tunnel", new(0, 0), 0);
        lone.Place("tunnel", new(0 + Defs.TunnelRange + 1, 0), 0);
        Check(!lone.At(new(Defs.TunnelRange + 1, 0)).Exit, "an underground belt out of range must not pair");
    }

    static void Splitting()
    {
        var w = Sandbox();
        var src = (Chest)w.Place("chest", new(0, 0), 0);
        src.Inv["iron_plate"] = 90;
        w.Place("inserter", new(1, 0), 0);
        w.Place("splitter", new(2, 0), 0);
        var outs = new[] { (Chest)w.Place("chest", new(3, 0), 0), (Chest)w.Place("chest", new(2, 1), 0), (Chest)w.Place("chest", new(2, -1), 0) };
        for (int i = 0; i < 3000; i++) w.Step();
        Check(outs.All(c => c.Count == 30), $"uneven split: {string.Join(", ", outs.Select(c => c.Count))}");
        w.Remove(new(3, 0));
        src.Inv["iron_plate"] = 40;
        for (int i = 0; i < 3000; i++) w.Step();
        Check(outs[1].Count == 50 && outs[2].Count == 50, $"with the front gone both sides should get 20 more: {outs[1].Count}, {outs[2].Count}");
    }

    static void FluidConserved()
    {
        var w = Sandbox();
        w.Place("pump", new(0, 0), 0);
        for (int x = 1; x <= 5; x++) w.Place("pipe", new(x, 0), 0);
        w.Place("pipe_tunnel", new(6, 0), 0);
        w.Place("pipe_tunnel", new(12, 0), 0);
        w.Place("pipe", new(13, 0), 0);
        var first = (Pipe)w.At(new(1, 0)); var last = (Pipe)w.At(new(13, 0));
        Check(w.At(new(12, 0)).Exit && w.FluidOf(first) == w.FluidOf(last), "underground pipe did not join the network");
        Check(w.FluidOf(first).Cap == 800, $"network should hold 8 pipes, holds {w.FluidOf(first).Cap}");
        for (int i = 0; i < 200; i++) w.Step();
        w.Remove(new(0, 0));
        float total = w.FluidOf(first).Amount;
        Check(w.FluidOf(first).Fluid == "water" && Mathf.Abs(total - 200) < 0.01f, $"pump should have filled 200 water, got {total}");
        for (int k = 0; k < 5; k++) { w.Place("belt", new(20, 20 + k), 0); w.Step(); w.Remove(new(20, 20 + k)); w.Step(); }
        Check(Mathf.Abs(w.FluidOf(first).Amount - total) < 0.01f, $"rebuilds changed the amount: {total} -> {w.FluidOf(first).Amount}");
        var b = World.FromSave(Persist.Deserialize(Persist.Serialize(w.ToSave())));
        var loaded = b.FluidOf((Pipe)b.At(new(1, 0)));
        Check(loaded != null && Mathf.Abs(loaded.Amount - total) < 0.01f && loaded.Fluid == "water", "fluid lost in save/load");
        // cutting the line in two keeps each half's share
        w.Remove(new(3, 0));
        float left = w.FluidOf(first).Amount, right = w.FluidOf(last).Amount;
        Check(Mathf.Abs(left + right + total / 8 - total) < 0.01f, $"split lost fluid: {left} + {right} of {total}");
    }

    static void FluidMixing()
    {
        var w = Sandbox();
        var a = (Pipe)w.Place("pipe", new(0, 0), 0);
        var b = (Pipe)w.Place("pipe", new(2, 0), 0);
        w.FluidOf(a).Fill("water", 50);
        w.FluidOf(b).Fill("sparkgas", 20);
        w.Events.Clear();
        w.Place("pipe", new(1, 0), 0);
        var net = w.FluidOf(a);
        Check(net == w.FluidOf(b) && net.Fluid == "water" && Mathf.Abs(net.Amount - 50) < 0.01f, $"expected 50 water, got {net.Amount} {net.Fluid}");
        Check(w.Events.Any(e => e.Kind == "spilled" && e.Id == "sparkgas"), "no spill event");
    }

    // Water -> black goo -> sparkgas into a generator, and black goo -> plastic out through an inserter.
    static World FluidWorld()
    {
        var w = Sandbox();
        w.Place("pump", new(0, 0), 0);
        w.Place("pipe", new(1, 0), 0);
        w.SetRecipe((Assembler)w.Place("refinery", new(2, 0), 0), "black_goo");
        ((Chest)w.Place("chest", new(2, 2), 0)).Inv["coal"] = 200;
        w.Place("inserter", new(2, 1), 3);
        w.Place("pipe", new(3, 0), 0);
        w.SetRecipe((Assembler)w.Place("refinery", new(4, 0), 0), "sparkgas");
        w.Place("pipe", new(5, 0), 0);
        w.Place("generator", new(6, 0), 0);
        w.SetRecipe((Assembler)w.Place("refinery", new(3, 1), 1), "plastic");
        ((Chest)w.Place("chest", new(5, 1), 0)).Inv["coal"] = 200;
        w.Place("inserter", new(4, 1), 2);
        w.Place("inserter", new(3, 2), 1);
        w.Place("chest", new(3, 3), 0);
        w.ForcedOre[new(8, 0)] = "iron_ore";
        w.Place("miner", new(8, 0), 0);
        w.Place("chest", new(9, 0), 0);
        w.Place("pole", new(7, 1), 0);
        return w;
    }

    static void RefineryChain()
    {
        var w = FluidWorld();
        var gen = (Generator)w.At(new(6, 0));
        for (int i = 0; i < 3000; i++) w.Step();
        var plastic = ((Chest)w.At(new(3, 3))).Inv.GetValueOrDefault("plastic");
        Check(plastic >= 10, $"only {plastic} plastic made");
        Check(gen.Fuel == 0 && w.PowerBoost(w.At(new(8, 0))) > 1.9f, $"generator should run on sparkgas alone, boost {w.PowerBoost(w.At(new(8, 0)))}");
        var refunds = new List<string>();
        foreach (var e in w.Entities) e.Contents(refunds);
        Check(!refunds.Any(Defs.IsFluid), "fluids must never be refunded as items");
    }

    static void AlloysAndFuels()
    {
        // copper chest -> inserter -> refinery (molten metal) -> pipe -> refinery (bronze) <- inserter <- tin chest; bronze out to a chest
        var w = Sandbox();
        ((Chest)w.Place("chest", new(0, 2), 0)).Inv["copper_plate"] = 40;
        w.Place("inserter", new(0, 1), 3);
        w.SetRecipe((Assembler)w.Place("refinery", new(0, 0), 0), "molten_metal");
        w.Place("pipe", new(1, 0), 0);
        w.SetRecipe((Assembler)w.Place("refinery", new(2, 0), 0), "bronze_plate");
        ((Chest)w.Place("chest", new(2, 2), 0)).Inv["tin_plate"] = 40;
        w.Place("inserter", new(2, 1), 3);
        w.Place("inserter", new(2, -1), 3);
        w.Place("chest", new(2, -2), 0);
        for (int i = 0; i < 3000; i++) w.Step();
        var bronze = ((Chest)w.At(new(2, -2))).Inv.GetValueOrDefault("bronze_plate");
        Check(bronze >= 10, $"only {bronze} bronze made");

        // A generator with one fuel canister, and one next to a pipe of steam, each power a miner.
        foreach (bool steam in new[] { false, true })
        {
            var (pw, m, g) = PowerRig(true, 0);
            if (steam) { pw.Place("pipe", new(2, 2), 0); pw.FluidNetAt(new(2, 2), new(1, 2)).Fill("steam", 100); }
            else Check(g.CanAccept(pw, "fuel", g.Pos) && !g.CanAccept(pw, "plastic", g.Pos), "generators take fuel, not plastic");
            if (!steam) g.Accept(pw, "fuel", g.Pos);
            for (int i = 0; i < 200; i++) pw.Step();
            Check(pw.PowerBoost(m) > 1.9f, $"{(steam ? "steam" : "fuel")} should power the generator, boost {pw.PowerBoost(m)}");
            if (!steam) { var back = new List<string>(); g.Contents(back); Check(g.Cans == 0 && g.Energy > Defs.FuelKJ - 1000 && back.Count == 0, $"fuel canister not burnt: {g.Cans} left, {g.Energy} kJ"); }
        }
    }

    static void BigPoles()
    {
        var w = Sandbox();
        var a = (Pole)w.Place("super_pole", new(0, 0), 0);
        var b = (Pole)w.Place("super_pole", new(18, 0), 0);
        var big = (Pole)w.Place("big_pole", new(18, 12), 0);
        var small = (Pole)w.Place("pole", new(28, 0), 0);
        Check(w.NetOf(a) == w.NetOf(b), "two super poles 18 apart should share a network");
        Check(w.NetOf(big) == w.NetOf(b), "a big pole 12 from a super pole links (big range 12)");
        Check(w.NetOf(small) != w.NetOf(b), "a small pole 10 away from a super one must not link (small range 7 wins)");
        w.Place("pole", new(23, 0), 0);
        Check(w.NetOf(small) == w.NetOf(b), "a small pole 5 away links to a super one");
        // Better poles power a bigger square: super reaches 4 tiles, a small pole only 2.
        w.ForcedOre[new(4, 4)] = "iron_ore";
        w.ForcedOre[new(31, 3)] = "iron_ore";
        var near = w.Place("miner", new(4, 4), 0);
        var far = w.Place("miner", new(31, 3), 0);
        Check(w.NetOf(near) == w.NetOf(a), "a super pole powers a machine 4 tiles away");
        Check(w.NetOf(far) == null, "a small pole doesn't power a machine 3 tiles away");
    }

    static void CrossAndSplit()
    {
        var w = World.New("t", "normal", false, 1);
        w.AddStorage("iron_plate", 100);
        Belts(w, 1, (5, 0), (5, 1), (5, 2));
        long iron = w.Storage["iron_plate"];
        Check(w.MakeCrossing(new(5, 1)) && w.At(new(5, 1)) is Crossing, "belt swapped for a crossing");
        Check(w.Storage["iron_plate"] == iron + 1 - 4, "the belt is refunded and the crossing paid");
        Check(!w.MakeCrossing(new(9, 9)), "nothing to swap on an empty tile");
        // A splitter whose chosen output fills up switches to another one instead of jamming.
        var s = Sandbox();
        var src = (Chest)s.Place("chest", new(0, 0), 0);
        src.Inv["iron_plate"] = 60;
        s.Place("inserter", new(1, 0), 0);
        s.Place("splitter", new(2, 0), 0);
        var front = (Chest)s.Place("chest", new(3, 0), 0);
        front.Inv["stone"] = Chest.Cap - 1; // room for one more only
        var side = (Chest)s.Place("chest", new(2, 1), 0);
        for (int i = 0; i < 4000; i++) s.Step();
        Check(front.Count == Chest.Cap && side.Count == 59, $"overflow goes to the side: front {front.Count}, side {side.Count}");
        foreach (var sp in s.Entities.OfType<Splitter>()) Check(sp.Lane.Count == 0, "splitter jammed");
    }

    static void SideGlide()
    {
        var w = Sandbox();
        Belts(w, 0, (0, 0), (1, 0), (2, 0));
        Belts(w, 3, (1, 2), (1, 1)); // feeds (1,0) from below
        var main = (Belt)w.At(new(1, 0));
        ((Belt)w.At(new(1, 2))).Accept(w, "iron_ore", new(1, 3));
        Belt.Item arrived = null;
        for (int t = 0; t < 40 && arrived == null; t++) { w.Step(); arrived = main.Items.FirstOrDefault(); }
        Check(arrived != null && arrived.Side == new Vector2(0, 1) && arrived.SideLeft > 0.3f, $"a side item starts out at the side edge (side {arrived?.Side}, left {arrived?.SideLeft})");
        for (int t = 0; t < 10; t++) w.Step();
        Check(arrived.SideLeft == 0, "and reaches the middle within a few ticks");
    }

    static void CoinRush()
    {
        var w = World.New("t", "normal", false, 1);
        w.Coins = 1000;
        long cost = w.PotionCost("coin_rush", 2f, 2);
        Check(cost == 40 && w.BuyPotion("coin_rush", 2f, 2) && w.Coins == 1000 - 40, $"2 min x2 coin rush at the start costs 40 (got {cost}, coins {w.Coins})");
        w.Coins = 0;
        w.Storage["iron_plate"] = 60;
        w.AutoSell.Add("iron_plate");
        for (int i = 0; i < 20; i++) w.Step();
        Check(w.Coins == 10 * 3 * 2 && w.CoinsEarned == 60, $"coin rush should pay double: {w.Coins} coins");
        w.Events.Clear();
        for (int i = 0; i < 2 * World.TicksPerMinute; i++) w.Step();
        Check(!w.Has("coin_rush") && w.Events.Any(e => e.Kind == "potion_end" && e.Id == "coin_rush"), "coin rush should be over after 2 minutes");
        w.Storage["iron_plate"] = 60;
        long before = w.Coins;
        for (int i = 0; i < 20; i++) w.Step();
        Check(w.Coins - before == 10 * 3, "normal price again after the potion");
    }

    static void PotionPrices()
    {
        var w = World.New("t", "normal", false, 1);
        w.Coins = 1_000_000;
        Check(w.BuyPotion("overdrive", 1.5f, 100) && w.BuyPotion("overdrive", 1.5f, 100) && w.PotionMinutesLeft("overdrive", 1.5f) == Defs.PotionMaxMinutes, $"stacking should stop at {Defs.PotionMaxMinutes} min, got {w.PotionMinutesLeft("overdrive", 1.5f)}");
        Check(!w.BuyPotion("overdrive", 1.5f, 5), "no buying past the cap");
        Check(!w.BuyPotion("overdrive", 7f, 5), "only the listed strengths can be brewed");
        Check(Mathf.Abs(w.MachineSpeed - 1.5f) < 1e-4f, "x1.5 overdrive makes machines 50% faster");
        long x2 = w.PotionCost("coin_rush", 2f, 10), x3 = w.PotionCost("coin_rush", 3f, 10), x15 = w.PotionCost("coin_rush", 1.5f, 10);
        Check(x3 == 2 * x2 && x15 == x2 / 2, $"price follows the bonus: x1.5 {x15}, x2 {x2}, x3 {x3}");
        w.MilestoneIndex = 2;
        w.CoinsEarned = 10000;
        long late = w.PotionCost("coin_rush", 2f, 10);
        Check(late == (long)Math.Ceiling(x2 * 2.5 * 2), $"price should scale with milestones and coins earned: {x2} -> {late}");
        Check(w.PotionCost("coin_rush", 2f, 20) == 2 * late, "price scales with the duration");
    }

    static void PotionOrder()
    {
        var w = World.New("t", "normal", false, 1);
        w.Coins = 1_000_000;
        w.BuyPotion("coin_rush", 2f, 1);
        w.BuyPotion("coin_rush", 3f, 1);
        Check(w.Strength("coin_rush") == 3f && w.SellMultiplier == 3f, $"x3 and x2 together give x3, not x6 (got {w.SellMultiplier})");
        for (int i = 0; i < World.TicksPerMinute - 1; i++) w.Step();
        Check(w.Strength("coin_rush") == 3f && w.PotionMinutesLeft("coin_rush", 2f) == 1, "the x2 waits untouched while the x3 runs");
        w.Events.Clear();
        w.Step();
        Check(w.Strength("coin_rush") == 2f && w.Events.Any(e => e.Kind == "potion_next" && e.F == 2f), "after the x3 runs out the x2 takes over");
        for (int i = 0; i < World.TicksPerMinute; i++) w.Step();
        Check(!w.Has("coin_rush") && w.SellMultiplier == 1, "both used up after two minutes");
        // a save in the middle keeps every waiting strength
        w.BuyPotion("overdrive", 2f, 1); w.BuyPotion("overdrive", 1.25f, 3);
        var b = World.FromSave(Persist.Deserialize(Persist.Serialize(w.ToSave())));
        Check(b.Strength("overdrive") == 2f && b.PotionMinutesLeft("overdrive", 1.25f) == 3, "waiting potions survive save and load");
    }

    static void LegacyPotions()
    {
        var s = World.New("t", "normal", false, 1).ToSave();
        s.Potions["rock_crusher"] = World.TicksPerMinute; // how v0.0.3 stored a potion
        var w = World.FromSave(Persist.Deserialize(Persist.Serialize(s)));
        Check(w.Strength("rock_crusher") == 5f && w.PotionMinutesLeft("rock_crusher", 5f) == 1, "an old potion loads at its old strength (x5)");
    }

    static void NewUpgrades()
    {
        var w = World.New("t", "normal", false, 1);
        w.Upgrades["sell_price"] = 5;
        w.Storage["iron_plate"] = 150;
        w.AutoSell.Add("iron_plate");
        for (int i = 0; i < 20; i++) w.Step();
        Check(w.Coins == 450, $"+50% deals on 100 plates at 3c should pay 450, got {w.Coins}");
        w.Upgrades["power"] = 5;
        Check(Mathf.Abs(w.GeneratorKW - 2 * Defs.GeneratorKW) < 0.01f, "5 generator upgrades double the output");
        foreach (var u in Defs.Upgrades) Check(Defs.UpgradeCost(u, u.Max - 1) > 0, $"{u.Id} top level cost overflows");
    }

    // Pass "--legacy <dir>" with copies of real saves to check they still load (never the live save folder).
    static void LegacySaves()
    {
        var args = OS.GetCmdlineUserArgs();
        int i = Array.IndexOf(args, "--legacy");
        if (i < 0 || i + 1 >= args.Length) { GD.Print("       (no --legacy folder given, skipped)"); return; }
        int n = 0;
        foreach (var f in System.IO.Directory.GetFiles(args[i + 1], "*.json"))
        {
            var s = Persist.Deserialize(System.IO.File.ReadAllText(f));
            var w = World.FromSave(s);
            Check(w.Entities.Count == s.Entities.Count, $"{f}: lost buildings on load");
            for (int k = 0; k < 300; k++) w.Step();
            n++;
        }
        Check(n > 0, "no saves found in the --legacy folder");
    }
}
