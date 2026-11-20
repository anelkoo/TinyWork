using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TinyWorks.Sim;

public readonly record struct GameEvent(string Kind, Vector2I Tile, string Id, long N = 1);

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
    public Dictionary<string, long> Potions = new(); // ticks left per active potion
    public long CoinsEarned;
    public int Tutorial; // tutorial steps done; -1 = hidden
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
    public int Tutorial; // first-start tutorial: steps done, -1 = hidden

    public bool Free => Mode == "sandbox";
    public Milestone CurrentMilestone => !Free && MilestoneIndex < Defs.Milestones.Length ? Defs.Milestones[MilestoneIndex] : null;
    public int Up(string id) => Upgrades.GetValueOrDefault(id);
    public float MachineSpeed => (1 + 0.1f * Up("machine_speed")) * (Has("overdrive") ? 1.5f : 1);
    public float ArmSpeed => (1 + 0.15f * Up("arm_speed")) * (Has("quick_arms") ? 1.5f : 1);
    public int HandYield => (1 + Up("hand")) * (Has("rock_crusher") ? 5 : 1);
    public float SellMultiplier => (1 + 0.1f * Up("sell_price")) * (Has("coin_rush") ? 2 : 1);
    public float GeneratorKW => Defs.GeneratorKW * (1 + 0.2f * Up("power"));
    public float PumpRate => Defs.PumpRate * (1 + 0.25f * Up("pump"));

    // ---------- Potions ----------
    public readonly Dictionary<string, long> Potions = new(); // ticks left per active potion
    public long CoinsEarned;                                     // all coins ever made by selling: prices scale with it
    public const int TicksPerMinute = 20 * 60;
    public bool Has(string potion) => Potions.ContainsKey(potion);

    // How far along the game is, as a price multiplier: every milestone and every 10k coins earned add to it.
    public float PriceLevel => (1 + 0.75f * MilestoneIndex) * (1 + CoinsEarned / 10000f);
    public long PotionCost(string id, int minutes) =>
        Array.Find(Defs.Potions, p => p.Id == id) is { } p ? (long)Math.Ceiling(p.CoinsPerMinute * minutes * PriceLevel) : long.MaxValue;
    public int PotionMinutesLeft(string id) => (int)Math.Ceiling(Potions.GetValueOrDefault(id) / (double)TicksPerMinute);

    // Buying while one is active adds time, up to Defs.PotionMaxMinutes.
    public bool BuyPotion(string id, int minutes)
    {
        minutes = Math.Min(minutes, Defs.PotionMaxMinutes - PotionMinutesLeft(id));
        if (minutes < 1 || Coins < PotionCost(id, minutes)) return false;
        Coins -= PotionCost(id, minutes);
        Potions[id] = Potions.GetValueOrDefault(id) + (long)minutes * TicksPerMinute;
        Emit("potion", Vector2I.Zero, id, minutes);
        return true;
    }

    void StepPotions()
    {
        foreach (var id in Potions.Keys.ToList())
            if (--Potions[id] <= 0) { Potions.Remove(id); Emit("potion_end", Vector2I.Zero, id); }
    }

    // Buildings and recipes: the base set, plus whatever finished milestones unlocked. Sandbox has everything.
    public bool IsUnlocked(string key)
    {
        if (key == null || Free || Array.IndexOf(Defs.BaseUnlocks, key) >= 0) return true;
        for (int i = 0; i < MilestoneIndex && i < Defs.Milestones.Length; i++)
            if (Array.IndexOf(Defs.Milestones[i].Unlocks, key) >= 0) return true;
        return false;
    }

    public void Deliver(string item, Vector2I from)
    {
        AddStorage(item, 1);
        Emit("delivered", from, item);
        if (CurrentMilestone is not { } m) return;
        foreach (var (i, n) in m.Need)
            if (i == item && Delivered.GetValueOrDefault(i) < n) Delivered[i] = Delivered.GetValueOrDefault(i) + 1;
        foreach (var (i, n) in m.Need) if (Delivered.GetValueOrDefault(i) < n) return;
        MilestoneIndex++;
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
        Emit("upgraded", Vector2I.Zero, id);
        return true;
    }

    public const int RawBatch = 10;
    public static long RawCost(string ore) => RawBatch * Defs.Items[ore].Price * 2;

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
        // A guaranteed starter patch of every ore around the Hub, rotated per seed.
        for (int i = 0; i < Defs.Ores.Length; i++)
        {
            float a = Mathf.Tau * (i + 0.5f) / Defs.Ores.Length + Seed % 7;
            var c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 13f;
            if ((t - c).Length() < 4.2f + 1.3f * _noise[i].GetNoise2D(t.X * 3, t.Y * 3)) return Defs.Ores[i];
        }
        if (d < 22) return null;
        for (int i = 0; i < Defs.Ores.Length; i++)
            if (_noise[i].GetNoise2D(t.X, t.Y) > 0.5f) return Defs.Ores[i];
        return null;
    }

    public Entity At(Vector2I t) => Grid.GetValueOrDefault(t);
    public void AddStorage(string item, long n) => Storage[item] = Storage.GetValueOrDefault(item) + n;
    public void Emit(string kind, Vector2I tile, string id, long n = 1) { if (Events.Count < 512) Events.Enqueue(new GameEvent(kind, tile, id, n)); }

    // ---------- Power ----------
    // Poles wire up to their 3 nearest poles in range; each connected group is one network that powers
    // every machine and generator within reach of its poles. Power is a bonus: full power doubles a
    // machine's speed, too little power gives part of the bonus, no power means normal speed.

    public sealed class PowerNet
    {
        public readonly List<Pole> Poles = new();
        public readonly List<Generator> Gens = new();
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
    public float SpeedOf(Entity e) => MachineSpeed * PowerBoost(e);

    public static bool InWireRange(Entity a, string bType, Vector2I bPos) =>
        ((Vector2)(a.Pos - bPos)).Length() <= Math.Min(Defs.WireRangeOf(a.Type), Defs.WireRangeOf(bType));
    static bool InWireRange(Pole a, Pole b) => InWireRange(a, b.Type, b.Pos);

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
                for (int dx = -Defs.PoleReach; dx <= Defs.PoleReach; dx++)
                    for (int dy = -Defs.PoleReach; dy <= Defs.PoleReach; dy++)
                    {
                        if (At(p.Pos + new Vector2I(dx, dy)) is not { } e || _netOf.ContainsKey(e)) continue;
                        if (e is Generator g) { net.Gens.Add(g); _netOf[g] = net; }
                        else if (Defs.PowerUse.ContainsKey(e.Type)) { net.Users.Add(e); _netOf[e] = net; }
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
            foreach (var u in net.Users) if (u.Working) demand += Defs.PowerUse[u.Type];
            int capable = 0;
            foreach (var g in net.Gens) { g.Gas = GasNextTo(g); g.Working = false; if (g.CanSupply) { cap += GeneratorKW; capable++; } }
            net.Demand = demand;
            net.Capacity = cap;
            net.Satisfaction = cap <= 0 ? 0 : demand <= 0 ? 1 : Mathf.Min(1, cap / demand);
            if (demand <= 0 || capable == 0) continue;
            float perGenKJ = demand * net.Satisfaction / capable / Engine.PhysicsTicksPerSecond;
            foreach (var g in net.Gens) if (g.CanSupply) g.Draw(perGenKJ);
        }
    }

    public void Step()
    {
        Tick++;
        EnsureFluids();
        StepPower();
        foreach (var e in Entities)
        {
            e.Tick(this);
            e.Idle = e.Working ? 0 : e.Idle + 1;
        }
        if (Tick % 20 == 0) AutoSellPass();
        StepPotions();
    }

    // ---------- Undergrounds ----------
    // An entrance pairs with the first piece of the same type and direction ahead of it, if that one is an exit.

    public Entity Partner(Entity u) => Partner(u, u.Exit);

    // The unpaired entrance a new underground piece here would become the exit of, if any.
    public Entity EntranceFor(string type, Vector2I pos, int rot)
    {
        if (type is not ("tunnel" or "pipe_tunnel")) return null;
        var probe = Entity.Create(type);
        probe.Type = type; probe.Pos = pos; probe.Rot = rot;
        return Partner(probe, true) is { } entrance && Partner(entrance) == null ? entrance : null;
    }

    // Looks backwards when lookBack, otherwise forwards; returns the first matching piece if it is the other kind.
    Entity Partner(Entity u, bool lookBack)
    {
        int range = u.Type == "tunnel" ? Defs.TunnelRange : Defs.PipeTunnelRange;
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
        public float Cap => Pipes.Count * Defs.PipeCap;
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
            if (FluidNetAt(e.Pos + d, e.Pos) is { Fluid: "sparkgas" } n) return n;
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
            float share = net.Amount / net.Pipes.Count, total = 0;
            foreach (var p in net.Pipes) { p.Fluid = net.Fluid; p.Amount = net.Fluid == null ? 0 : share; total += p.Amount; }
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
        if (!IsUnlocked(type)) { why = $"Unlocks with milestone {Defs.UnlockTier(type)}"; return false; }
        for (int x = 0; x < def.Size; x++)
            for (int y = 0; y < def.Size; y++)
                if (Grid.ContainsKey(pos + new Vector2I(x, y))) { why = "Something is in the way"; return false; }
        if (type == "miner" && OreAt(pos) == null) { why = "Miners go on ore"; return false; }
        if (!Free)
            foreach (var (item, n) in def.Cost)
                if (Storage.GetValueOrDefault(item) < n) { why = $"Needs {n} {Defs.Items[item].Name}"; return false; }
        why = null;
        return true;
    }

    public Entity Place(string type, Vector2I pos, int rot, bool force = false)
    {
        if (!force && !CanPlace(type, pos, out _)) return null;
        if (!force && !Free)
            foreach (var (item, n) in Defs.Buildings[type].Cost) AddStorage(item, -n);
        var e = Entity.Create(type);
        e.Type = type;
        e.Pos = pos;
        e.Rot = rot;
        // An underground piece becomes the exit of the nearest unpaired entrance behind it, if there is one.
        e.Exit = EntranceFor(type, pos, rot) != null;
        Add(e);
        Emit("placed", pos, type);
        return e;
    }

    public bool Remove(Vector2I tile)
    {
        if (!Grid.TryGetValue(tile, out var e) || e is Hub) return false;
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

    // Turns a building in place, keeping its contents.
    public void Rotate(Vector2I tile)
    {
        if (At(tile) is { } e) SetRot(tile, (e.Rot + 1) % 4);
    }

    public void SetRot(Vector2I tile, int rot)
    {
        if (At(tile) is not { } e || e is Hub || e.Rot == rot) return;
        e.Rot = rot;
        Version++;
        Emit("rotated", e.Pos, e.Type);
    }

    public bool HandMine(Vector2I t)
    {
        if (Grid.ContainsKey(t) || OreAt(t) is not { } ore) return false;
        AddStorage(ore, HandYield);
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
        foreach (var kv in Potions) s.Potions[kv.Key] = kv.Value;
        s.Coins = Coins;
        foreach (var kv in Delivered) s.Delivered[kv.Key] = kv.Value;
        s.AutoSell.AddRange(AutoSell);
        s.AutoSell.Sort(StringComparer.Ordinal);
        foreach (var kv in Reserve) s.Reserve[kv.Key] = kv.Value;
        foreach (var kv in Upgrades) s.Upgrades[kv.Key] = kv.Value;
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
        foreach (var kv in s.Potions ?? new()) if (Array.Exists(Defs.Potions, p => p.Id == kv.Key) && kv.Value > 0) w.Potions[kv.Key] = kv.Value;
        foreach (var kv in s.Storage) w.Storage[kv.Key] = kv.Value;
        foreach (var kv in s.Delivered ?? new()) w.Delivered[kv.Key] = kv.Value;
        foreach (var i in s.AutoSell ?? new()) w.AutoSell.Add(i);
        foreach (var kv in s.Reserve ?? new()) w.Reserve[kv.Key] = kv.Value;
        foreach (var kv in s.Upgrades ?? new()) w.Upgrades[kv.Key] = kv.Value;
        foreach (var d in s.Entities)
        {
            if (!Defs.Buildings.ContainsKey(d.Type) || Entity.Create(d.Type) is not { } e) { GD.PushWarning($"Unknown building '{d.Type}' skipped"); continue; }
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
