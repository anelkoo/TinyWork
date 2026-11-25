using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TinyWorks.Sim;

public readonly record struct GameEvent(string Kind, Vector2I Tile, string Id, long N = 1, float F = 0);

// Save files hold plain strings and numbers only, so they survive new items and buildings.
// Fields added later just default when an older save is loaded.
public sealed class SaveData
{
    public int Version;
    public string Name, Mode, SavedAt;
    public bool Flat;
    public int Seed;
    public long Tick;
    public Dictionary<string, long> Storage = new();
    public List<EntityData> Entities = new();
    public float CamX, CamY, Zoom = 2;
    public int MilestoneIndex;
    public Dictionary<string, long> Delivered = new();
    public long Coins;
    public List<string> AutoSell = new();
    public Dictionary<string, int> Reserve = new();
    public Dictionary<string, int> Upgrades = new();
    public Dictionary<string, long> Potions = new(); // v0.0.3 format, read only: ticks left at the default strength
    public List<PotionSave> PotionStacks = new();
    public long CoinsEarned;
    public int Tutorial; // tutorial steps done; -1 = hidden
    public SessionStats LastSession; // the session that wrote this save, for the "welcome back" card
    public List<string> Cosmetics = new();
    public Dictionary<string, string> Equipped = new();
    public List<string> Researched = new();
    public string Researching;
    public int ResearchProgress;
    public Dictionary<string, List<string>> Recent = new();
}

public sealed class PotionSave { public string Id; public float Strength; public long Ticks; }

// What happened while the game was open. Nothing is ever earned while it is closed.
public sealed class SessionStats { public double Seconds; public long Coins, Made, Delivered, Built; public int Milestones; }

// One building in a copy or a blueprint, relative to the copied area's centre.
public sealed class BpPart
{
    public string Type, Config;
    public int X, Y, Rot;
    public bool Exit;
    public BpPart Turned() => new() { Type = Type, Config = Config, X = -Y, Y = X, Rot = (Rot + 1) % 4, Exit = Exit }; // 90° clockwise
}

public sealed class EntityData
{
    public string Type, A, B;
    public bool On; // was working last tick (power demand is based on it)
    public bool Exit; // underground pieces
    public int X, Y, Rot, N, An, Bn;
    public float P;
    public List<string> Items;
    public List<float> Pos;
    public List<int> Ints;
    public Dictionary<string, int> Inv;
}

public sealed class World
{
    public const int SaveVersion = 1;

    public string Name = "Factory", Mode = "normal";
    public bool Flat;
    public readonly int Seed;
    public long Tick;
    public int Version; // bumps whenever a building is added or removed
    public readonly Dictionary<string, long> Storage = new();
    public readonly Dictionary<Vector2I, Entity> Grid = new();
    public readonly List<Entity> Entities = new();
    public readonly Queue<GameEvent> Events = new();
    public readonly Dictionary<Vector2I, string> ForcedOre = new(); // self-test only

    public int MilestoneIndex;                                   // milestones finished so far
    public readonly Dictionary<string, long> Delivered = new();  // toward the current milestone
    public long Coins;
    public readonly HashSet<string> AutoSell = new();
    public readonly Dictionary<string, int> Reserve = new();     // kept back from auto-sell
    public readonly Dictionary<string, int> Upgrades = new();
    public const int DefaultReserve = 50;
    // "Recently used" per list (recipes, blueprints, items, builds): the last RecentMax keys clicked, newest first.
    public const int RecentMax = 5;
    public readonly Dictionary<string, List<string>> Recent = new();
    public void Used(string list, string key)
    {
        if (!Recent.TryGetValue(list, out var l)) Recent[list] = l = new();
        l.Remove(key);
        l.Insert(0, key);
        if (l.Count > RecentMax) l.RemoveAt(RecentMax);
    }
    public int Tutorial; // first-start tutorial: steps done, -1 = hidden

    // Game speed is not saved: every load starts at 1x and running.
    public bool Paused, StepOnce;
    public int Speed = 1;

    public SessionStats Session = new(), LastSession;

    // ---------- Stats ----------
    // Everything made, and the coins earned, is counted in 10 s buckets; the last 10 minutes stay for the graph.
    public const int TicksPerSecond = 20, StatBucketTicks = 10 * TicksPerSecond, StatBuckets = 60;
    public const string CoinKey = "$coins";
    public readonly List<Dictionary<string, long>> History = new(); // oldest first
    Dictionary<string, long> _bucket = new();

    public void Made(string item, long n)
    {
        _bucket[item] = _bucket.GetValueOrDefault(item) + n;
        if (item != CoinKey) Session.Made += n;
    }

    // Average over the last minute, or over what there is so far.
    public float PerMinute(string key)
    {
        int k = Math.Min(6, History.Count);
        long sum = 0;
        for (int i = History.Count - k; i < History.Count; i++) sum += History[i].GetValueOrDefault(key);
        return k == 0 ? 0 : sum * 6f / k;
    }

    // ---------- Cosmetics ----------
    public readonly HashSet<string> Owned = new();
    public readonly HashSet<string> Researched = new();
    public string Researching;   // the research the labs are working on, or null
    public int ResearchProgress; // lab-ticks done on it
    public readonly Dictionary<string, string> Equipped = new(); // slot -> cosmetic id
    public Cosmetic Worn(string slot) => Equipped.TryGetValue(slot, out var id) ? Defs.CosmeticById(id) : null;

    public bool BuyCosmetic(string id)
    {
        if (Defs.CosmeticById(id) is not { } c || Owned.Contains(id) || Coins < c.Cost) return false;
        Coins -= c.Cost;
        Owned.Add(id);
        Equipped[c.Slot] = id;
        Emit("upgraded", Vector2I.Zero, id);
        return true;
    }

    // null takes the slot back to plain.
    public void Wear(string slot, string id)
    {
        if (id == null) Equipped.Remove(slot);
        else if (Owned.Contains(id) && Defs.CosmeticById(id)?.Slot == slot) Equipped[slot] = id;
    }

    public bool Free => Mode == "sandbox";
    public Milestone CurrentMilestone => !Free && MilestoneIndex < Defs.Milestones.Length ? Defs.Milestones[MilestoneIndex] : null;
    public int Up(string id) => Upgrades.GetValueOrDefault(id);
    public float MachineSpeed => (1 + 0.1f * Up("machine_speed")) * Strength("overdrive");
    public float ArmSpeed => (1 + 0.15f * Up("arm_speed")) * Strength("quick_arms");
    public int HandYield => (int)Math.Round((1 + Up("hand")) * Strength("rock_crusher"));
    public float SellMultiplier => (1 + 0.1f * Up("sell_price")) * Strength("coin_rush");
    public float GeneratorKW => Defs.GeneratorKW * (1 + 0.2f * Up("power"));
    public float PumpRate => Defs.PumpRate * (1 + 0.25f * Up("pump"));

    // ---------- Potions ----------
    // Each potion can have several strengths waiting. Strengths never multiply: only the strongest one works and
    // counts down; when it runs out, the next strongest takes over where it was paused.
    public sealed class PotionStack { public float Strength; public long Ticks; }
    public readonly Dictionary<string, List<PotionStack>> Potions = new(); // strongest first
    public long CoinsEarned;                                                  // all coins ever made by selling: prices scale with it
    public const int TicksPerMinute = 20 * 60;
    public bool Has(string potion) => Potions.ContainsKey(potion);
    public float Strength(string potion) => Potions.TryGetValue(potion, out var l) ? l[0].Strength : 1;

    // How far along the game is, as a price multiplier: every milestone and every 10k coins earned add to it.
    public float PriceLevel => (1 + 0.75f * MilestoneIndex) * (1 + CoinsEarned / 10000f);

    public long PotionCost(string id, float strength, int minutes) =>
        Defs.PotionById(id) is { } p && Array.IndexOf(p.Strengths, strength) >= 0
            ? (long)Math.Ceiling(p.CoinsPerMinute * minutes * PriceLevel * (strength - 1) / (p.Default - 1))
            : long.MaxValue;

    public int PotionMinutesLeft(string id, float strength) =>
        (int)Math.Ceiling((Potions.GetValueOrDefault(id)?.Find(s => s.Strength == strength)?.Ticks ?? 0) / (double)TicksPerMinute);

    // Buying the same strength again adds time, up to Defs.PotionMaxMinutes.
    public bool BuyPotion(string id, float strength, int minutes)
    {
        minutes = Math.Min(minutes, Defs.PotionMaxMinutes - PotionMinutesLeft(id, strength));
        long cost = PotionCost(id, strength, minutes);
        if (minutes < 1 || Coins < cost) return false;
        Coins -= cost;
        AddPotion(id, strength, (long)minutes * TicksPerMinute);
        Emit("potion", Vector2I.Zero, id, minutes, strength);
        return true;
    }

    void AddPotion(string id, float strength, long ticks)
    {
        if (!Potions.TryGetValue(id, out var list)) Potions[id] = list = new();
        if (list.Find(s => s.Strength == strength) is { } same) same.Ticks += ticks;
        else list.Add(new PotionStack { Strength = strength, Ticks = ticks });
        list.Sort((a, b) => b.Strength.CompareTo(a.Strength));
    }

    void StepPotions()
    {
        foreach (var id in Potions.Keys.ToList())
        {
            var list = Potions[id];
            if (--list[0].Ticks > 0) continue;
            list.RemoveAt(0);
            if (list.Count > 0) { Emit("potion_next", Vector2I.Zero, id, 1, list[0].Strength); continue; }
            Potions.Remove(id);
            Emit("potion_end", Vector2I.Zero, id);
        }
    }

    // Buildings and recipes: the base set, plus whatever finished milestones unlocked. Sandbox has everything.
    public bool IsUnlocked(string key)
    {
        if (key == null || Free || Array.IndexOf(Defs.BaseUnlocks, key) >= 0 || Researched.Contains(key)) return true;
        for (int i = 0; i < MilestoneIndex && i < Defs.Milestones.Length; i++)
            if (Array.IndexOf(Defs.Milestones[i].Unlocks, key) >= 0) return true;
        return false;
    }

    public void Deliver(string item, Vector2I from)
    {
        AddStorage(item, 1);
        Session.Delivered++;
        Emit("delivered", from, item);
        if (CurrentMilestone is not { } m) return;
        foreach (var (i, n) in m.Need)
            if (i == item && Delivered.GetValueOrDefault(i) < n) Delivered[i] = Delivered.GetValueOrDefault(i) + 1;
        foreach (var (i, n) in m.Need) if (Delivered.GetValueOrDefault(i) < n) return;
        MilestoneIndex++;
        Session.Milestones++;
        Delivered.Clear();
        Emit("milestone", from, m.Name);
    }

    public long Reserved(string item) => Reserve.TryGetValue(item, out var r) ? r : DefaultReserve;

    void AutoSellPass()
    {
        foreach (var item in AutoSell)
        {
            long excess = Storage.GetValueOrDefault(item) - Reserved(item);
            if (excess <= 0 || !Defs.Items.TryGetValue(item, out var def)) continue;
            AddStorage(item, -excess);
            long paid = (long)Math.Round(excess * def.Price * SellMultiplier);
            Coins += paid;
            CoinsEarned += paid;
            Session.Coins += paid;
            Made(CoinKey, paid);
            Emit("sold", Vector2I.Zero, item, paid);
        }
    }

    public bool BuyUpgrade(string id)
    {
        var u = Array.Find(Defs.Upgrades, x => x.Id == id);
        int lvl = Up(id);
        if (u == null || lvl >= u.Max || Coins < Defs.UpgradeCost(u, lvl)) return false;
        Coins -= Defs.UpgradeCost(u, lvl);
        Upgrades[id] = lvl + 1;
        if (id == "pole_range") Version++; // poles rewire with the longer reach
        Emit("upgraded", Vector2I.Zero, id);
        return true;
    }

    public const int RawBatch = 10;
    public static long RawCost(string ore) => RawBatch * Defs.Items[ore].Price * 2;

    public int LabCount => Entities.Count(e => e is Lab);

    // Takes the cost from storage and starts the research. One at a time, and it needs a lab.
    public bool StartResearch(string id)
    {
        var r = Array.Find(Defs.Researches, x => x.Id == id);
        if (r == null || Researching != null || Researched.Contains(id) || LabCount == 0) return false;
        foreach (var (item, n) in r.Cost) if (Storage.GetValueOrDefault(item) < n) return false;
        foreach (var (item, n) in r.Cost) AddStorage(item, -n);
        Researching = id;
        ResearchProgress = 0;
        Emit("research_start", Vector2I.Zero, id);
        return true;
    }

    void StepResearch()
    {
        if (Researching == null) return;
        var r = Array.Find(Defs.Researches, x => x.Id == Researching);
        if (r == null) { Researching = null; return; }
        ResearchProgress += LabCount;
        if (ResearchProgress < r.Ticks) return;
        Researched.Add(r.Id);
        Researching = null;
        ResearchProgress = 0;
        Emit("researched", Vector2I.Zero, r.Id);
    }

    public bool BuyRaw(string ore)
    {
        if (Array.IndexOf(Defs.Ores, ore) < 0 || Coins < RawCost(ore)) return false;
        Coins -= RawCost(ore);
        AddStorage(ore, RawBatch);
        Emit("bought", Vector2I.Zero, ore, RawBatch);
        return true;
    }

    // Switching recipe hands back whatever the assembler held.
    public void SetRecipe(Assembler a, string recipeId)
    {
        var back = new List<string>();
        a.Contents(back);
        foreach (var i in back) AddStorage(i, 1);
        a.Inv.Clear();
        a.OutN = 0;
        a.Progress = 0;
        a.RecipeId = recipeId;
    }

    readonly FastNoiseLite[] _noise = new FastNoiseLite[Defs.Ores.Length];

    public World(int seed)
    {
        Seed = seed;
        for (int i = 0; i < _noise.Length; i++)
            _noise[i] = new FastNoiseLite { Seed = seed + i * 101, Frequency = 0.035f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
    }

    public static World New(string name, string mode, bool flat, int seed)
    {
        var w = new World(seed) { Name = name, Mode = mode, Flat = flat };
        foreach (var (item, n) in Defs.StarterKit) w.AddStorage(item, n);
        w.Place("hub", new Vector2I(-1, -1), 0, force: true);
        w.Events.Clear();
        return w;
    }

    public string OreAt(Vector2I t)
    {
        if (Flat || ForcedOre.Count > 0) return ForcedOre.GetValueOrDefault(t);
        float d = ((Vector2)t).Length();
        if (d < 6) return null;
        // The first ores first, exactly as before newer ones came, so an old save keeps every ore tile it had:
        // a starter patch each on the inner ring around the Hub, rotated per seed, then the far fields.
        int n = Defs.FirstOres;
        for (int i = 0; i < n; i++)
            if (InPatch(t, i, i, n, 13f, 4.2f)) return Defs.Ores[i];
        if (d >= 22)
            for (int i = 0; i < n; i++)
                if (_noise[i].GetNoise2D(t.X, t.Y) > 0.5f) return Defs.Ores[i];
        // Newer ores only take tiles that had none: their starter patches on an outer ring, then their far fields.
        for (int i = n; i < Defs.Ores.Length; i++)
            if (InPatch(t, i, i - n, Defs.Ores.Length - n, 19f, 3.4f)) return Defs.Ores[i];
        if (d >= 22)
            for (int i = n; i < Defs.Ores.Length; i++)
                if (_noise[i].GetNoise2D(t.X, t.Y) > 0.5f) return Defs.Ores[i];
        return null;
    }

    // Ore i's starter patch: slot k of a ring of count patches at radius r around the Hub, size wobbling with the noise.
    bool InPatch(Vector2I t, int i, int k, int count, float r, float size)
    {
        float a = Mathf.Tau * (k + 0.5f) / count + Seed % 7;
        var c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        return (t - c).Length() < size + 1.3f * _noise[i].GetNoise2D(t.X * 3, t.Y * 3);
    }

    public Entity At(Vector2I t) => Grid.GetValueOrDefault(t);
    public void AddStorage(string item, long n) => Storage[item] = Storage.GetValueOrDefault(item) + n;
    // Whether lightning strikes this tile this tick. Worked out from the seed, tick and tile, so a loaded save sees the same strikes.
    public bool LightningAt(Vector2I t)
    {
        unchecked
        {
            ulong h = (ulong)Seed * 0x9E3779B97F4A7C15UL ^ (ulong)Tick * 0xC2B2AE3D27D4EB4FUL ^ (uint)t.X * 0x165667B19E3779F9UL ^ (uint)t.Y * 0x27D4EB2F165667C5UL;
            h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 29;
            return h % Defs.LightningEvery == 0;
        }
    }

    public void Emit(string kind, Vector2I tile, string id, long n = 1, float f = 0) { if (Events.Count < 512) Events.Enqueue(new GameEvent(kind, tile, id, n, f)); }

    // ---------- Power ----------
    // Poles wire up to their 3 nearest poles in range; each connected group is one network that powers
    // every machine and generator within reach of its poles. Power is a bonus: full power doubles a
    // machine's speed, too little power gives part of the bonus, no power means normal speed.

    public sealed class PowerNet
    {
        public readonly List<Pole> Poles = new();
        public readonly List<Generator> Gens = new();
        public readonly List<Solar> Solars = new();
        public readonly List<LightningRod> Rods = new();
        public readonly List<Accumulator> Accs = new();
        public readonly List<Entity> Users = new();
        public readonly List<(Pole A, Pole B)> Wires = new();
        public float Demand, Capacity, Satisfaction; // kW, kW, 0..1
    }

    readonly List<PowerNet> _nets = new();
    readonly Dictionary<Entity, PowerNet> _netOf = new();
    int _netsVersion = -1;

    public IReadOnlyList<PowerNet> Nets { get { EnsureNets(); return _nets; } }
    public PowerNet NetOf(Entity e) { EnsureNets(); return _netOf.GetValueOrDefault(e); }
    public float PowerBoost(Entity e) => _netOf.TryGetValue(e, out var n) ? 1 + n.Satisfaction : 1;
    public float SpeedOf(Entity e) => MachineSpeed * PowerBoost(e) * Defs.SpeedOf(e.Type);

    // Long Wires adds 2 tiles per level to every pole type.
    public int WireRange(string pole) => Defs.WireRangeOf(pole) + 2 * Up("pole_range");
    public bool InWireRange(Entity a, string bType, Vector2I bPos) =>
        ((Vector2)(a.Pos - bPos)).Length() <= Math.Min(WireRange(a.Type), WireRange(bType));
    bool InWireRange(Pole a, Pole b) => InWireRange(a, b.Type, b.Pos);

    void EnsureNets()
    {
        if (_netsVersion == Version) return;
        _netsVersion = Version;
        _nets.Clear();
        _netOf.Clear();
        var poles = Entities.OfType<Pole>().ToList();
        var links = poles.ToDictionary(p => p, _ => new List<Pole>());
        var order = new Dictionary<Pole, int>();
        for (int i = 0; i < poles.Count; i++) order[poles[i]] = i;
        var wires = new HashSet<(Pole, Pole)>();
        foreach (var p in poles)
            foreach (var q in poles.Where(q => q != p && InWireRange(p, q)).OrderBy(q => (q.Pos - p.Pos).LengthSquared()).ThenBy(q => q.Pos.X).ThenBy(q => q.Pos.Y).Take(3))
            {
                links[p].Add(q);
                links[q].Add(p);
                wires.Add(order[p] < order[q] ? (p, q) : (q, p));
            }
        var seen = new HashSet<Pole>();
        foreach (var start in poles)
        {
            if (!seen.Add(start)) continue;
            var net = new PowerNet();
            var todo = new Queue<Pole>();
            todo.Enqueue(start);
            while (todo.Count > 0)
            {
                var p = todo.Dequeue();
                net.Poles.Add(p);
                _netOf[p] = net;
                foreach (var q in links[p]) if (seen.Add(q)) todo.Enqueue(q);
            }
            net.Wires.AddRange(wires.Where(w => net.Poles.Contains(w.Item1)));
            foreach (var p in net.Poles)
                for (int dx = -Defs.PoleReachOf(p.Type); dx <= Defs.PoleReachOf(p.Type); dx++)
                    for (int dy = -Defs.PoleReachOf(p.Type); dy <= Defs.PoleReachOf(p.Type); dy++)
                    {
                        if (At(p.Pos + new Vector2I(dx, dy)) is not { } e || _netOf.ContainsKey(e)) continue;
                        if (e is Generator g) { net.Gens.Add(g); _netOf[g] = net; }
                        else if (e is Solar so) { net.Solars.Add(so); _netOf[so] = net; }
                        else if (e is LightningRod lr) { net.Rods.Add(lr); _netOf[lr] = net; }
                        else if (e is Accumulator ac) { net.Accs.Add(ac); _netOf[ac] = net; }
                        else if (Defs.UsesPower(e.Type)) { net.Users.Add(e); _netOf[e] = net; }
                    }
            _nets.Add(net);
        }
    }

    void StepPower()
    {
        EnsureNets();
        foreach (var net in _nets)
        {
            float demand = 0, cap = 0;
            foreach (var u in net.Users) if (u.Working) demand += Defs.PowerOf(u.Type);
            int capable = 0;
            foreach (var g in net.Gens) { g.Gas = GasNextTo(g); g.Working = false; if (g.CanSupply) { cap += GeneratorKW; capable++; } }
            float solar = net.Solars.Count * Defs.SolarKW + net.Rods.Count(r => r.Charge > 0) * Defs.LightningKW; // free (sun, lightning), so used before any coal
            float stored = 0;
            foreach (var a in net.Accs) { a.Working = false; stored += Mathf.Min(Defs.AccumulatorKW, a.Stored * TicksPerSecond); }
            cap += solar + stored;
            net.Demand = demand;
            net.Capacity = cap;
            net.Satisfaction = cap <= 0 ? 0 : demand <= 0 ? 1 : Mathf.Min(1, cap / demand);
            // Free power first, then accumulators, then generators. Free power nobody uses charges the accumulators.
            float need = demand * net.Satisfaction, fromAcc = Mathf.Clamp(need - solar, 0, stored), left = fromAcc, spare = solar - need;
            foreach (var a in net.Accs)
            {
                float kw = left > 0 ? -Mathf.Min(left, Mathf.Min(Defs.AccumulatorKW, a.Stored * TicksPerSecond))
                    : Mathf.Max(0, Mathf.Min(spare, Mathf.Min(Defs.AccumulatorKW, (Defs.AccumulatorKJ - a.Stored) * TicksPerSecond)));
                if (kw < 0) left += kw; else spare -= kw;
                a.Stored = Mathf.Clamp(a.Stored + kw / TicksPerSecond, 0, Defs.AccumulatorKJ);
                a.Working = kw != 0;
            }
            float fromGens = need - solar - fromAcc;
            if (fromGens <= 0 || capable == 0) continue;
            float perGenKJ = fromGens / capable / World.TicksPerSecond;
            foreach (var g in net.Gens) if (g.CanSupply) g.Draw(perGenKJ);
        }
    }

    // Carriers (belts, splitters, undergrounds, crossings) tick downstream first, so each sees the one in front of it
    // after that one moved. Built front to back, every tile edge used to cost a tick and a line carried far less than
    // its rated speed. Everything else keeps its build order; carriers only swap among the slots they already hold.
    List<Entity> _tickOrder;
    int _tickOrderVersion = -1;

    static bool IsCarrier(Entity e) => e is Belt or Splitter or Tunnel or Crossing;

    IEnumerable<Entity> Downstream(Entity e)
    {
        foreach (var d in Entity.Dirs)
            if (e.OutputsTo(e.Pos + d) && At(e.Pos + d) is { } n && n != e && IsCarrier(n) && !n.OutputsTo(e.Pos)) yield return n; // a crossing "outputs" all round; skip what feeds it
        if (e is Tunnel { Exit: false } && Partner(e) is { } p) yield return p;
    }

    // Miners that touch form a group. Every non-miner next to any member is an exit for the whole group.
    public sealed class MinerGroupInfo { public List<Miner> Members = new(); public List<(Entity Target, Vector2I From)> Exits = new(); }
    Dictionary<Miner, MinerGroupInfo> _minerGroups = new();
    int _minerGroupsVersion = -1;

    public MinerGroupInfo MinerGroup(Miner m)
    {
        if (_minerGroupsVersion != Version) { _minerGroups = new(); _minerGroupsVersion = Version; }
        if (_minerGroups.TryGetValue(m, out var g)) return g;
        g = new MinerGroupInfo();
        var stack = new Stack<Miner>();
        stack.Push(m); _minerGroups[m] = g;
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            g.Members.Add(cur);
            foreach (var d in Entity.Dirs)
            {
                var n = At(cur.Pos + d);
                if (n is Miner nm) { if (_minerGroups.TryAdd(nm, g)) stack.Push(nm); }
                else if (n != null) g.Exits.Add((n, cur.Pos));
            }
        }
        return g;
    }

    List<Entity> TickOrder()
    {
        if (_tickOrder != null && _tickOrderVersion == Version && _tickOrder.Count == Entities.Count) return _tickOrder;
        var seen = new HashSet<Entity>();
        var post = new List<Entity>();
        var stack = new Stack<(Entity E, bool Done)>();
        foreach (var root in Entities)
        {
            if (!IsCarrier(root) || seen.Contains(root)) continue;
            stack.Push((root, false));
            while (stack.Count > 0)
            {
                var (e, done) = stack.Pop();
                if (done) { post.Add(e); continue; }
                if (!seen.Add(e)) continue;
                stack.Push((e, true));
                foreach (var n in Downstream(e)) if (!seen.Contains(n)) stack.Push((n, false));
            }
        }
        int k = 0;
        _tickOrder = Entities.Select(e => IsCarrier(e) ? post[k++] : e).ToList();
        _tickOrderVersion = Version;
        return _tickOrder;
    }

    public long CarrierPass; // counts every carrier pass, so a belt knows which items it was handed during this one
    public int BeltPass;     // 0 on the normal pass, 1.. on the extra passes Belt Rush adds

    public void Step()
    {
        Tick++;
        EnsureFluids();
        StepPower();
        CarrierPass++;
        foreach (var e in TickOrder())
        {
            e.Tick(this);
            e.Idle = e.Working ? 0 : e.Idle + 1;
        }
        // Belt Rush moves everything that carries items again, as a whole, so items cross several tiles in one tick.
        for (BeltPass = 1; BeltPass < (int)Strength("belt_rush"); BeltPass++, CarrierPass++)
            foreach (var e in TickOrder()) if (IsCarrier(e)) e.Tick(this);
        BeltPass = 0;
        if (Tick % 20 == 0) AutoSellPass();
        StepPotions();
        StepResearch();
        if (Tick % StatBucketTicks == 0)
        {
            History.Add(_bucket);
            _bucket = new();
            if (History.Count > StatBuckets) History.RemoveAt(0);
        }
    }

    // ---------- Undo ----------
    // While an action is open (one mouse drag, one key, one paste), every place, remove and turn is recorded,
    // so the whole action undoes in one step. Contents are never restored: removing already refunded them.
    public sealed record Op(string Kind, string Type, Vector2I Pos, int Rot, string Config = null, bool Exit = false, int From = 0)
    {
        public Op Inverse() => Kind switch { "placed" => this with { Kind = "removed" }, "removed" => this with { Kind = "placed" }, _ => this with { Rot = From, From = Rot } };
    }

    public const int UndoLimit = 100;
    List<Op> _recording;
    readonly List<List<Op>> _undo = new(), _redo = new();

    public void BeginAction() { EndAction(); _recording = new(); }

    public void EndAction()
    {
        if (_recording is { Count: > 0 })
        {
            _undo.Add(_recording);
            _redo.Clear();
            if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
        }
        _recording = null;
    }

    // Both return how many buildings couldn't come back (tile taken or not enough materials), or -1 if there was nothing.
    public int Undo() => Replay(_undo, _redo, true);
    public int Redo() => Replay(_redo, _undo, false);

    int Replay(List<List<Op>> from, List<List<Op>> to, bool back)
    {
        EndAction();
        if (from.Count == 0) return -1;
        var ops = from[^1];
        from.RemoveAt(from.Count - 1);
        var steps = back ? ops.Select(o => o.Inverse()).Reverse().ToList() : ops;
        int failed = 0;
        // Removals first so a tile is free again, then placements with underground entrances before their exits.
        foreach (var o in steps.Where(o => o.Kind == "removed"))
            if (At(o.Pos) is { } e && e.Pos == o.Pos && e.Type == o.Type) Remove(o.Pos);
        foreach (var o in steps.Where(o => o.Kind == "placed").OrderBy(o => o.Exit))
            if (Place(o.Type, o.Pos, o.Rot, config: o.Config, exit: o.Exit) == null) failed++;
        foreach (var o in steps.Where(o => o.Kind == "rotated")) SetRot(o.Pos, o.Rot);
        to.Add(ops);
        return failed;
    }

    // ---------- Copy and paste ----------
    public List<BpPart> Copy(Rect2I area)
    {
        var center = area.Position + area.Size / 2;
        var parts = new List<BpPart>();
        foreach (var e in Entities)
            if (e is not Hub && area.HasPoint(e.Pos))
                parts.Add(new BpPart { Type = e.Type, Config = e.Config, X = e.Pos.X - center.X, Y = e.Pos.Y - center.Y, Rot = e.Rot, Exit = e.Exit });
        return parts;
    }

    // Places what fits and returns how many parts didn't (tile taken, locked or not enough materials).
    public int Paste(List<BpPart> parts, Vector2I at)
    {
        int failed = 0;
        foreach (var p in parts.OrderBy(p => p.Exit))
            if (!Defs.Buildings.ContainsKey(p.Type) || Place(p.Type, at + new Vector2I(p.X, p.Y), p.Rot, config: p.Config) == null) failed++;
        return failed;
    }

    // ---------- Undergrounds ----------
    // An entrance pairs with the first piece of the same type and direction ahead of it, if that one is an exit.

    public Entity Partner(Entity u) => Partner(u, u.Exit);

    // The unpaired entrance a new underground piece here would become the exit of, if any.
    public Entity EntranceFor(string type, Vector2I pos, int rot)
    {
        if (Defs.BaseOf(type) is not ("tunnel" or "pipe_tunnel")) return null;
        var probe = Entity.Create(type);
        probe.Type = type; probe.Pos = pos; probe.Rot = rot;
        return Partner(probe, true) is { } entrance && Partner(entrance) == null ? entrance : null;
    }

    // Looks backwards when lookBack, otherwise forwards; returns the first matching piece if it is the other kind.
    Entity Partner(Entity u, bool lookBack)
    {
        int range = Defs.BaseOf(u.Type) == "tunnel" ? Defs.TunnelRange : Defs.PipeTunnelRange;
        var step = lookBack ? -u.Dir : u.Dir;
        for (int k = 1; k <= range; k++)
            if (At(u.Pos + step * k) is { } e && e.Type == u.Type && e.Rot == u.Rot)
                return e.Exit == !lookBack ? e : null;
        return null;
    }

    // ---------- Fluids ----------
    // Connected pipes form one network holding a single fluid. Networks rebuild whenever a building changes;
    // each pipe keeps its share so nothing is lost. Joining two different fluids keeps the larger one.

    public sealed class FluidNet
    {
        public readonly List<Pipe> Pipes = new();
        public string Fluid;
        public float Amount;
        public float Cap; // the sum of Defs.CapOf over its pipes, set when the network is built
        public float Room(string f) => Fluid == null || Fluid == f ? Cap - Amount : 0;

        public float Fill(string f, float amount)
        {
            float take = Mathf.Min(amount, Room(f));
            if (take <= 0) return 0;
            Fluid = f;
            Amount += take;
            return take;
        }

        // Whole units only, so machines count fluid in integers.
        public int Drain(string f, int max)
        {
            if (Fluid != f) return 0;
            int take = Mathf.Min(max, (int)Amount);
            Amount -= take;
            if (Amount < 0.01f) { Amount = 0; Fluid = null; }
            return take;
        }
    }

    readonly List<FluidNet> _fluidNets = new();
    readonly Dictionary<Pipe, FluidNet> _fluidOf = new();
    int _fluidVersion = -1;

    public FluidNet FluidOf(Pipe p) { EnsureFluids(); return _fluidOf.GetValueOrDefault(p); }

    // The network a machine at `from` reaches through the pipe on `tile`.
    public FluidNet FluidNetAt(Vector2I tile, Vector2I from) =>
        At(tile) is Pipe p && p.Opens(from) ? FluidOf(p) : null;

    FluidNet GasNextTo(Entity e)
    {
        foreach (var d in Entity.Dirs)
            if (FluidNetAt(e.Pos + d, e.Pos) is { Fluid: { } f } n && Defs.GasKJ.ContainsKey(f)) return n;
        return null;
    }

    IEnumerable<Pipe> PipeLinks(Pipe p)
    {
        foreach (var d in Entity.Dirs)
            if (At(p.Pos + d) is Pipe q && p.Opens(q.Pos) && q.Opens(p.Pos)) yield return q;
        if (p.Type == "pipe_tunnel" && Partner(p) is Pipe partner) yield return partner;
    }

    // Writes each network's amount back into its pipes, evenly. The total is re-added in pipe order, the same
    // order a rebuild sums it in, so a loaded save carries on with exactly the same numbers.
    void SpreadFluids()
    {
        foreach (var net in _fluidNets)
        {
            float share = net.Cap > 0 ? net.Amount / net.Cap : 0, total = 0; // per unit of room, so a tank keeps its bigger share
            foreach (var p in net.Pipes) { p.Fluid = net.Fluid; p.Amount = net.Fluid == null ? 0 : share * Defs.CapOf(p.Type); total += p.Amount; }
            net.Amount = total;
        }
    }

    void EnsureFluids()
    {
        if (_fluidVersion == Version) return;
        SpreadFluids();
        _fluidVersion = Version;
        _fluidNets.Clear();
        _fluidOf.Clear();
        foreach (var start in Entities.OfType<Pipe>())
        {
            if (_fluidOf.ContainsKey(start)) continue;
            var net = new FluidNet();
            var todo = new Queue<Pipe>();
            todo.Enqueue(start);
            _fluidOf[start] = net;
            while (todo.Count > 0)
            {
                var p = todo.Dequeue();
                net.Pipes.Add(p);
                net.Cap += Defs.CapOf(p.Type);
                foreach (var q in PipeLinks(p)) if (_fluidOf.TryAdd(q, net)) todo.Enqueue(q);
            }
            var totals = new Dictionary<string, float>();
            foreach (var p in net.Pipes) if (p.Fluid != null && p.Amount > 0) totals[p.Fluid] = totals.GetValueOrDefault(p.Fluid) + p.Amount;
            if (totals.Count > 0)
            {
                var best = totals.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
                net.Fluid = best.Key;
                net.Amount = Mathf.Min(best.Value, net.Cap);
                if (totals.Count > 1) Emit("spilled", start.Pos, totals.Keys.First(k => k != best.Key));
            }
            _fluidNets.Add(net);
        }
    }

    public bool CanPlace(string type, Vector2I pos, out string why)
    {
        var def = Defs.Buildings[type];
        if (!IsUnlocked(type)) { why = Defs.LockedWhy(type); return false; }
        for (int x = 0; x < def.Size; x++)
            for (int y = 0; y < def.Size; y++)
                if (Grid.ContainsKey(pos + new Vector2I(x, y))) { why = "Something is in the way"; return false; }
        if (Defs.BaseOf(type) == "miner" && OreAt(pos) == null) { why = "Miners go on ore"; return false; }
        if (!Free)
            foreach (var (item, n) in def.Cost)
                if (Storage.GetValueOrDefault(item) < n) { why = $"Needs {n} {Defs.Items[item].Name}"; return false; }
        why = null;
        return true;
    }

    // config: a recipe, filter or source item to start with (copies, undo).
    // exit: undo and bulk upgrades put an underground end back as it was instead of working it out again.
    public Entity Place(string type, Vector2I pos, int rot, bool force = false, string config = null, bool? exit = null)
    {
        if (!force && !CanPlace(type, pos, out _)) return null;
        if (!force && !Free)
            foreach (var (item, n) in Defs.Buildings[type].Cost) AddStorage(item, -n);
        var e = Entity.Create(type);
        e.Type = type;
        e.Pos = pos;
        e.Rot = rot;
        e.Config = config;
        // An underground piece becomes the exit of the nearest unpaired entrance behind it, if there is one.
        e.Exit = exit ?? EntranceFor(type, pos, rot) != null;
        Add(e);
        if (!force) Session.Built++;
        _recording?.Add(new Op("placed", type, pos, rot, e.Config, e.Exit));
        Emit("placed", pos, type);
        return e;
    }

    public bool Remove(Vector2I tile)
    {
        if (!Grid.TryGetValue(tile, out var e) || e is Hub) return false;
        _recording?.Add(new Op("removed", e.Type, e.Pos, e.Rot, e.Config, e.Exit));
        var refund = new List<string>();
        e.Contents(refund);
        foreach (var i in refund) AddStorage(i, 1);
        if (!Free)
            foreach (var (item, n) in Defs.Buildings[e.Type].Cost) AddStorage(item, n);
        Entities.Remove(e);
        ForTiles(e, t => Grid.Remove(t));
        Version++;
        Emit("removed", e.Pos, e.Type);
        return true;
    }

    // Upgrading in place: another tier of the same family goes onto a building (a downgrade works too). The old one is
    // refunded with its contents and the new one keeps its recipe or filter. An underground pair swaps both ends.
    public bool CanReplace(string type, Vector2I tile)
    {
        if (At(tile) is not { } old || old.Type == type || Defs.TiersOf(type) is not { } f || Defs.TiersOf(old.Type) != f || !IsUnlocked(type)) return false;
        if (Free) return true;
        var olds = old is Tunnel && Partner(old) is { } p ? new[] { old, p } : new[] { old };
        foreach (var (item, n) in Defs.Buildings[type].Cost)
            if (Storage.GetValueOrDefault(item) + olds.Sum(o => Defs.Buildings[o.Type].Cost.FirstOrDefault(c => c.Item == item).Count) < n * olds.Length) return false;
        return true;
    }

    public Entity Replace(string type, Vector2I tile, int rot)
    {
        if (!CanReplace(type, tile)) return null;
        var old = At(tile);
        if (old is Tunnel && Partner(old) is { } exit)
        {
            var entrance = old.Exit ? exit : old;
            if (old.Exit) exit = old;
            var (a, b, r) = (entrance.Pos, exit.Pos, entrance.Rot);
            Remove(a); Remove(b);
            var e = Place(type, a, r);
            Place(type, b, r);
            return e;
        }
        var (pos, config) = (old.Pos, old.Config);
        Remove(tile);
        return Place(type, pos, old is Tunnel ? old.Rot : rot, config: config);
    }

    // ---------- Upgrade everything ----------
    // Every building below the best unlocked tier of its family (belts only: just the belt family). Net is the
    // material the whole run takes per item, new costs minus refunds; negative means that much comes back.
    public sealed record UpgradePlan(List<(Entity Old, string To)> Targets, Dictionary<string, long> Net);

    public UpgradePlan PlanUpgrade(bool beltsOnly)
    {
        var targets = new List<(Entity Old, string To)>();
        var net = new Dictionary<string, long>();
        foreach (var e in Entities)
        {
            if (Defs.TiersOf(e.Type) is not { } f || beltsOnly && f != Defs.BeltTiers) continue;
            var to = f.LastOrDefault(IsUnlocked);
            if (to == null || Defs.TierOf(to) <= Defs.TierOf(e.Type)) continue; // never a downgrade
            targets.Add((e, to));
            if (Free) continue;
            foreach (var (item, n) in Defs.Buildings[to].Cost) net[item] = net.GetValueOrDefault(item) + n;
            foreach (var (item, n) in Defs.Buildings[e.Type].Cost) net[item] = net.GetValueOrDefault(item) - n;
        }
        return new(targets, net);
    }

    public bool CanAfford(UpgradePlan plan) => plan.Net.All(x => x.Value <= 0 || Storage.GetValueOrDefault(x.Key) >= x.Value);

    // All or nothing, and one undo step. Everything comes down before anything goes up, so every refund is in
    // storage when the new tiers are paid. Each underground end keeps its Exit flag, so pairs stay as they were.
    public bool Upgrade(UpgradePlan plan)
    {
        if (!CanAfford(plan)) return false;
        BeginAction();
        var olds = plan.Targets.Select(t => (t.To, t.Old.Pos, t.Old.Rot, t.Old.Config, t.Old.Exit)).ToList();
        foreach (var o in olds) Remove(o.Pos);
        foreach (var o in olds) Place(o.To, o.Pos, o.Rot, config: o.Config, exit: o.Exit);
        EndAction();
        return true;
    }

    // Swaps a belt for a crossing (a belt line being drawn straight across it). Items on the belt go to storage.
    public bool MakeCrossing(Vector2I tile)
    {
        if (At(tile) is not Belt || !IsUnlocked("crossing")) return false;
        if (!Free)
            foreach (var (item, n) in Defs.Buildings["crossing"].Cost)
                if (Storage.GetValueOrDefault(item) + (Defs.Buildings[At(tile).Type].Cost.FirstOrDefault(c => c.Item == item).Count) < n) return false;
        Remove(tile);
        return Place("crossing", tile, 0) != null;
    }

    // Turns a building in place, keeping its contents.
    public void Rotate(Vector2I tile)
    {
        if (At(tile) is { } e) SetRot(tile, (e.Rot + 1) % 4);
    }

    public void SetRot(Vector2I tile, int rot)
    {
        if (At(tile) is not { } e || e is Hub || e.Rot == rot) return;
        _recording?.Add(new Op("rotated", e.Type, e.Pos, rot, From: e.Rot));
        e.Rot = rot;
        Version++;
        Emit("rotated", e.Pos, e.Type);
    }

    public bool HandMine(Vector2I t)
    {
        if (Grid.ContainsKey(t) || OreAt(t) is not { } ore) return false;
        AddStorage(ore, HandYield);
        Made(ore, HandYield);
        Emit("mined", t, ore, HandYield);
        return true;
    }

    public void SetFilter(Inserter ins, string item) => ins.Filter = item;

    void Add(Entity e)
    {
        Entities.Add(e);
        ForTiles(e, t => Grid[t] = e);
        Version++;
    }

    static void ForTiles(Entity e, Action<Vector2I> f)
    {
        for (int x = 0; x < e.Size; x++)
            for (int y = 0; y < e.Size; y++)
                f(e.Pos + new Vector2I(x, y));
    }

    public SaveData ToSave()
    {
        var s = new SaveData { Version = SaveVersion, Name = Name, Mode = Mode, Flat = Flat, Seed = Seed, Tick = Tick, SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        foreach (var kv in Storage) s.Storage[kv.Key] = kv.Value;
        s.MilestoneIndex = MilestoneIndex;
        s.Tutorial = Tutorial;
        s.CoinsEarned = CoinsEarned;
        foreach (var p in Defs.Potions)
            if (Potions.TryGetValue(p.Id, out var list))
                foreach (var st in list) s.PotionStacks.Add(new PotionSave { Id = p.Id, Strength = st.Strength, Ticks = st.Ticks });
        s.Coins = Coins;
        foreach (var kv in Delivered) s.Delivered[kv.Key] = kv.Value;
        s.AutoSell.AddRange(AutoSell);
        s.AutoSell.Sort(StringComparer.Ordinal);
        foreach (var kv in Reserve) s.Reserve[kv.Key] = kv.Value;
        foreach (var kv in Upgrades) s.Upgrades[kv.Key] = kv.Value;
        s.LastSession = Session;
        s.Cosmetics.AddRange(Owned.OrderBy(x => x, StringComparer.Ordinal));
        foreach (var kv in Equipped) s.Equipped[kv.Key] = kv.Value;
        s.Researched.AddRange(Researched.OrderBy(x => x, StringComparer.Ordinal));
        s.Researching = Researching;
        s.ResearchProgress = ResearchProgress;
        foreach (var kv in Recent) s.Recent[kv.Key] = new(kv.Value);
        EnsureFluids();
        SpreadFluids();
        foreach (var e in Entities)
        {
            var d = new EntityData { Type = e.Type, X = e.Pos.X, Y = e.Pos.Y, Rot = e.Rot, On = e.Working, Exit = e.Exit };
            e.Save(d);
            s.Entities.Add(d);
        }
        return s;
    }

    public static World FromSave(SaveData s)
    {
        if (s.Version > SaveVersion) throw new InvalidOperationException("This save was made by a newer version of Tiny Works.");
        var w = new World(s.Seed) { Name = s.Name, Mode = s.Mode ?? "normal", Flat = s.Flat, Tick = s.Tick, MilestoneIndex = s.MilestoneIndex, Coins = s.Coins, Tutorial = s.Tutorial, CoinsEarned = s.CoinsEarned };
        foreach (var kv in s.Potions ?? new()) // v0.0.3 saves: one potion at its default strength
            if (Defs.PotionById(kv.Key) is { } p && kv.Value > 0) w.AddPotion(p.Id, p.Default, kv.Value);
        foreach (var ps in s.PotionStacks ?? new())
            if (Defs.PotionById(ps.Id) is { } p && Array.IndexOf(p.Strengths, ps.Strength) >= 0 && ps.Ticks > 0) w.AddPotion(ps.Id, ps.Strength, ps.Ticks);
        foreach (var kv in s.Storage ?? new()) w.Storage[kv.Key] = kv.Value;
        foreach (var kv in s.Delivered ?? new()) w.Delivered[kv.Key] = kv.Value;
        foreach (var i in s.AutoSell ?? new()) w.AutoSell.Add(i);
        foreach (var kv in s.Reserve ?? new()) w.Reserve[kv.Key] = kv.Value;
        foreach (var kv in s.Upgrades ?? new()) w.Upgrades[kv.Key] = kv.Value;
        w.LastSession = s.LastSession;
        foreach (var id in s.Cosmetics ?? new()) if (Defs.CosmeticById(id) != null) w.Owned.Add(id);
        foreach (var kv in s.Equipped ?? new()) w.Wear(kv.Key, kv.Value);
        foreach (var id in s.Researched ?? new()) if (Array.Exists(Defs.Researches, x => x.Id == id)) w.Researched.Add(id);
        if (s.Researching != null && Array.Exists(Defs.Researches, x => x.Id == s.Researching) && !w.Researched.Contains(s.Researching))
        {
            w.Researching = s.Researching;
            w.ResearchProgress = Math.Max(0, s.ResearchProgress);
        }
        foreach (var kv in s.Recent ?? new()) foreach (var key in Enumerable.Reverse(kv.Value ?? new())) w.Used(kv.Key, key);
        foreach (var d in s.Entities ?? new())
        {
            if (d?.Type == null || !Defs.Buildings.ContainsKey(d.Type) || Entity.Create(d.Type) is not { } e) { GD.PushWarning($"Unknown building '{d?.Type}' skipped"); continue; }
            e.Type = d.Type;
            e.Pos = new Vector2I(d.X, d.Y);
            e.Rot = d.Rot;
            e.Working = d.On;
            e.Exit = d.Exit;
            e.Load(d);
            w.Add(e);
        }
        return w;
    }
}
