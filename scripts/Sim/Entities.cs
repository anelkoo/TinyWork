using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TinyWorks.Sim;

public abstract class Entity
{
    public static readonly Vector2I[] Dirs = { Vector2I.Right, Vector2I.Down, Vector2I.Left, Vector2I.Up };
    public const int StallTicks = 40; // 2 s of not working before a bottleneck icon shows

    public string Type;
    public Vector2I Pos; // top-left tile
    public int Rot;      // index into Dirs
    public bool Working; // drives the "busy" animation
    public int Idle;     // ticks since it last worked
    public bool Exit;    // underground pieces: true for the far end of a pair

    public Vector2I Dir => Dirs[Rot];
    public Vector2I Front => Pos + Dir;
    public Vector2I Back => Pos - Dir;
    public int Size => Defs.Buildings[Type].Size;

    public virtual void Tick(World w) { }
    public virtual bool CanAccept(World w, string item, Vector2I from) => false;
    public virtual bool OutputsTo(Vector2I t) => false; // pushes items into this tile (belts draw it as a join)
    public virtual void Accept(World w, string item, Vector2I from, float overflow = 0) { }
    public virtual string TakeAny(Func<string, bool> ok) => null; // inserters take output this way
    public virtual void Contents(List<string> into) { }            // items refunded on removal
    public virtual string Status => null;                           // "starved", "blocked", "norecipe" or null
    public virtual void Save(EntityData d) { }
    public virtual void Load(EntityData d) { }

    public static Entity Create(string type) => type switch
    {
        "belt" or "fast_belt" or "express_belt" => new Belt(),
        "miner" => new Miner(),
        "furnace" => new Furnace(),
        "assembler" => new Assembler(),
        "chest" => new Chest(),
        "inserter" => new Inserter(),
        "crossing" => new Crossing(),
        "generator" => new Generator(),
        "pole" => new Pole(),
        "splitter" => new Splitter(),
        "tunnel" => new Tunnel(),
        "pipe" or "pipe_tunnel" => new Pipe(),
        "pump" => new Pump(),
        "refinery" => new Refinery(),
        "hub" => new Hub(),
        _ => null,
    };

    protected static void AddInv(Dictionary<string, int> inv, string item, int n)
    {
        inv[item] = inv.GetValueOrDefault(item) + n;
        if (inv[item] <= 0) inv.Remove(item);
    }
}

// One lane. Item Pos runs 0..1 from the input edge through the centre to the front edge.
public sealed class Belt : Entity
{
    public const float Spacing = 0.25f;
    public sealed class Item { public string Id; public float Pos, Prev; public long Arrived = -1; }
    public readonly List<Item> Items = new(); // front first (Pos descending)

    // 2, 3 and 4 tiles/s at 20 ticks/s. Must stay below Spacing.
    public float Speed => Type switch { "fast_belt" => 0.15f, "express_belt" => 0.2f, _ => 0.1f };

    int _shapeVersion = -1;
    Vector2I _input;

    // The tile items enter from: the back, or one side when only that side feeds in (a curve).
    public Vector2I InputFrom(World w)
    {
        if (_shapeVersion == w.Version) return _input;
        _shapeVersion = w.Version;
        _input = Back;
        if (!FeedsMe(w, Back))
        {
            Vector2I l = Pos + Dirs[(Rot + 3) % 4], r = Pos + Dirs[(Rot + 1) % 4];
            bool fl = FeedsMe(w, l), fr = FeedsMe(w, r);
            if (fl ^ fr) _input = fl ? l : r;
        }
        return _input;
    }

    bool FeedsMe(World w, Vector2I t) => w.At(t)?.OutputsTo(Pos) == true;
    public override bool OutputsTo(Vector2I t) => t == Front;

    // Belts, crossings and splitters pushing into this belt from the sides, for drawing the joins.
    public IEnumerable<Vector2I> SideFeeds(World w)
    {
        var input = InputFrom(w);
        foreach (int s in new[] { (Rot + 1) % 4, (Rot + 3) % 4 })
        {
            var t = Pos + Dirs[s];
            if (t != input && FeedsMe(w, t)) yield return t;
        }
    }
    float EntryFor(World w, Vector2I from) => from == InputFrom(w) ? 0f : 0.5f;
    float Tail => Items.Count == 0 ? 1f : Items[^1].Pos;

    public override bool CanAccept(World w, string item, Vector2I from)
    {
        if (from == Front) return false;
        float e = EntryFor(w, from);
        foreach (var it in Items) if (Mathf.Abs(it.Pos - e) < Spacing - 0.0001f) return false;
        return true;
    }

    public override void Accept(World w, string item, Vector2I from, float overflow = 0)
    {
        float p = EntryFor(w, from), prev = p;
        // Entering from behind: Prev sits one tick back, over the edge, so the item glides on instead of pausing a tick.
        if (p == 0) { p = overflow; prev = overflow - Speed; }
        int i = 0;
        while (i < Items.Count && Items[i].Pos > p) i++;
        Items.Insert(i, new Item { Id = item, Pos = p, Prev = prev, Arrived = w.Tick });
    }

    public override void Tick(World w)
    {
        // An item handed over earlier in this same tick has already moved; it must not move twice.
        foreach (var it in Items) if (it.Arrived != w.Tick) it.Prev = it.Pos;
        Working = Items.Count > 0;
        if (Items.Count == 0) return;

        var next = w.At(Front);
        // Straight into another belt: keep spacing across the tile edge, not just at the handoff.
        float Cap() => next is Belt nb && nb.EntryFor(w, Pos) == 0 ? 1f + nb.Tail - Spacing : 1f;
        float cap = Cap();
        float speed = Speed;

        for (int i = 0; i < Items.Count; i++)
        {
            var it = Items[i];
            if (it.Arrived == w.Tick) continue;
            float limit = i == 0 ? cap : Items[i - 1].Pos - Spacing;
            it.Pos = Mathf.Max(it.Pos, Mathf.Min(it.Pos + speed, limit));
            if (i != 0 || it.Pos < 1f) continue;
            if (next != null && next.CanAccept(w, it.Id, Pos))
            {
                next.Accept(w, it.Id, Pos, it.Pos - 1f);
                Items.RemoveAt(0);
                i--;
                cap = Cap();
            }
            else it.Pos = 1f;
        }
    }

    public override void Contents(List<string> into) { foreach (var it in Items) into.Add(it.Id); }

    public override void Save(EntityData d)
    {
        d.Items = new List<string>();
        d.Pos = new List<float>();
        foreach (var it in Items) { d.Items.Add(it.Id); d.Pos.Add(it.Pos); }
    }

    public override void Load(EntityData d)
    {
        if (d.Items == null) return;
        for (int i = 0; i < d.Items.Count; i++) Items.Add(new Item { Id = d.Items[i], Pos = d.Pos[i], Prev = d.Pos[i] });
    }
}

public sealed class Miner : Entity
{
    public const int Interval = 40; // one ore every 2 s at base speed
    public float Progress;
    public string Buffer;
    public long Produced; // for the self-test

    public override void Tick(World w)
    {
        var ore = w.OreAt(Pos);
        if (Buffer == null && ore != null && (Progress += w.SpeedOf(this)) >= Interval) { Progress -= Interval; Buffer = ore; Produced++; }
        Working = Buffer == null && ore != null;
        if (Buffer != null && w.At(Front) is { } t && t.CanAccept(w, Buffer, Pos)) { t.Accept(w, Buffer, Pos); Buffer = null; }
    }

    public override string Status => Idle > StallTicks && Buffer != null ? "blocked" : null;
    public override void Contents(List<string> into) { if (Buffer != null) into.Add(Buffer); }
    public override void Save(EntityData d) { d.A = Buffer; d.P = Progress; }
    public override void Load(EntityData d) { Buffer = d.A; Progress = d.P; }
}

public sealed class Furnace : Entity
{
    public const int Cap = 10;
    public string In, Out;
    public int InN, OutN;
    public float Progress;

    // The recipe whose single input is this item, if that recipe is unlocked.
    static Recipe For(World w, string item) =>
        Defs.Recipes.FirstOrDefault(r => r.Machine == "furnace" && r.In[0].Item == item && w.IsUnlocked(r.Unlock));

    public override bool CanAccept(World w, string item, Vector2I from) =>
        (In == null || In == item) && InN < Cap && For(w, item) != null;

    public override void Accept(World w, string item, Vector2I from, float overflow = 0) { In = item; InN++; }

    public override void Tick(World w)
    {
        Working = false;
        if (In == null || For(w, In) is not { } r) return;
        int need = r.In[0].Count;
        if (InN < need || (Out != null && Out != r.Out) || OutN >= Cap) return;
        Working = true;
        if ((Progress += w.SpeedOf(this)) < r.Ticks) return;
        Progress -= r.Ticks;
        InN -= need;
        if (InN == 0) In = null;
        Out = r.Out;
        OutN += r.OutCount;
        w.Emit("crafted", Pos, Out);
    }

    public override string Status => Idle <= StallTicks ? null : OutN >= Cap ? "blocked" : "starved";

    public override string TakeAny(Func<string, bool> ok)
    {
        if (OutN == 0 || !ok(Out)) return null;
        var o = Out;
        if (--OutN == 0) Out = null;
        return o;
    }

    public override void Contents(List<string> into)
    {
        for (int i = 0; i < InN; i++) into.Add(In);
        for (int i = 0; i < OutN; i++) into.Add(Out);
    }

    public override void Save(EntityData d) { d.A = In; d.An = InN; d.B = Out; d.Bn = OutN; d.P = Progress; }
    public override void Load(EntityData d) { In = d.A; InN = d.An; Out = d.B; OutN = d.Bn; Progress = d.P; }
}

// Makes one chosen recipe. Holds up to two batches of each ingredient and 10 outputs.
public class Assembler : Entity
{
    protected virtual int OutCap => 10;
    public string RecipeId;
    public readonly Dictionary<string, int> Inv = new();
    public int OutN;
    public float Progress;

    public Recipe Recipe => RecipeId == null ? null : Defs.RecipeById(RecipeId);

    public override bool CanAccept(World w, string item, Vector2I from)
    {
        if (Recipe is not { } r) return false;
        foreach (var (i, n) in r.In) if (i == item) return Inv.GetValueOrDefault(item) < n * 2;
        return false;
    }

    public override void Accept(World w, string item, Vector2I from, float overflow = 0) => AddInv(Inv, item, 1);

    public override void Tick(World w)
    {
        Working = false;
        if (Recipe is not { } r || OutN + r.OutCount > OutCap) return;
        foreach (var (i, n) in r.In) if (Inv.GetValueOrDefault(i) < n) return;
        Working = true;
        if ((Progress += w.SpeedOf(this)) < r.Ticks) return;
        Progress -= r.Ticks;
        foreach (var (i, n) in r.In) AddInv(Inv, i, -n);
        OutN += r.OutCount;
        w.Emit("crafted", Pos, r.Out);
    }

    public override string Status => RecipeId == null ? "norecipe" : Idle <= StallTicks ? null : OutN + Recipe.OutCount > OutCap ? "blocked" : "starved";

    public override string TakeAny(Func<string, bool> ok)
    {
        if (OutN == 0 || !ok(Recipe.Out)) return null;
        OutN--;
        return Recipe.Out;
    }

    public override void Contents(List<string> into)
    {
        foreach (var (i, n) in Inv) for (int k = 0; k < n; k++) into.Add(i);
        for (int k = 0; k < OutN; k++) into.Add(Recipe.Out);
    }

    public override void Save(EntityData d) { d.A = RecipeId; d.Inv = new(Inv); d.Bn = OutN; d.P = Progress; }

    public override void Load(EntityData d)
    {
        RecipeId = Defs.RecipeById(d.A)?.Machine == Type ? d.A : null;
        if (d.Inv != null) foreach (var (i, n) in d.Inv) Inv[i] = n;
        OutN = RecipeId == null ? 0 : d.Bn;
        Progress = d.P;
    }
}

public sealed class Chest : Entity
{
    public const int Cap = 200;
    public readonly Dictionary<string, int> Inv = new();
    public int Count => Inv.Values.Sum();

    public override bool CanAccept(World w, string item, Vector2I from) => Count < Cap;
    public override void Accept(World w, string item, Vector2I from, float overflow = 0) => AddInv(Inv, item, 1);

    public override string TakeAny(Func<string, bool> ok)
    {
        foreach (var i in Inv.Keys.OrderBy(k => k)) // fixed order keeps the simulation deterministic
            if (ok(i)) { AddInv(Inv, i, -1); return i; }
        return null;
    }

    public override void Contents(List<string> into) { foreach (var (i, n) in Inv) for (int k = 0; k < n; k++) into.Add(i); }
    public override void Save(EntityData d) => d.Inv = new(Inv);
    public override void Load(EntityData d) { if (d.Inv != null) foreach (var (i, n) in d.Inv) Inv[i] = n; }
}

// Takes from the tile behind, drops in front. Only picks up what the target can take (and the filter allows).
public sealed class Inserter : Entity
{
    public const float Swing = 0.1f; // 10 ticks each way at base speed
    public string Held, Filter;
    public float Arm, PrevArm; // 0 = over the back tile, 1 = over the front tile

    public override void Tick(World w)
    {
        PrevArm = Arm;
        Working = true;
        float swing = Swing * w.ArmSpeed * w.PowerBoost(this);
        if (Held == null && Arm > 0) { Arm = Mathf.Max(0, Arm - swing); return; }
        if (Held != null && Arm < 1) { Arm = Mathf.Min(1, Arm + swing); return; }
        Working = false;
        var dst = w.At(Front);
        if (dst == null) return;
        if (Held != null)
        {
            if (dst.CanAccept(w, Held, Pos)) { dst.Accept(w, Held, Pos); Held = null; }
            return;
        }
        bool Ok(string i) => (Filter == null || i == Filter) && dst.CanAccept(w, i, Pos);
        var src = w.At(Back);
        if (src is Belt b)
        {
            for (int i = 0; i < b.Items.Count; i++)
                if (Ok(b.Items[i].Id)) { Held = b.Items[i].Id; b.Items.RemoveAt(i); return; }
        }
        else if (src != null) Held = src.TakeAny(Ok);
    }

    public override void Contents(List<string> into) { if (Held != null) into.Add(Held); }
    public override void Save(EntityData d) { d.A = Held; d.P = Arm; d.B = Filter; }
    public override void Load(EntityData d) { Held = d.A; Arm = d.P; Filter = d.B; }
}

// Lets two belt lines cross: whatever enters from one side leaves from the opposite side.
// Each travel direction has its own little lane, so crossing lines never mix.
public sealed class Crossing : Entity
{
    public const float Speed = 0.2f, Spacing = 0.25f; // 16 items/s per lane, faster than any belt
    public sealed class Item { public string Id; public float Pos, Prev; }
    public readonly List<Item>[] Lanes = { new(), new(), new(), new() }; // by travel direction, front first

    static int DirIndex(Vector2I d) => Array.IndexOf(Dirs, d);

    public override bool OutputsTo(Vector2I t) => (t - Pos).LengthSquared() == 1;

    public override bool CanAccept(World w, string item, Vector2I from)
    {
        int d = DirIndex(Pos - from);
        return d >= 0 && (Lanes[d].Count == 0 || Lanes[d][^1].Pos >= Spacing);
    }

    public override void Accept(World w, string item, Vector2I from, float overflow = 0) =>
        Lanes[DirIndex(Pos - from)].Add(new Item { Id = item, Prev = -Speed });

    public override void Tick(World w)
    {
        Working = false;
        for (int d = 0; d < 4; d++)
        {
            var lane = Lanes[d];
            for (int i = 0; i < lane.Count; i++)
            {
                var it = lane[i];
                it.Prev = it.Pos;
                float limit = i == 0 ? 1f : lane[i - 1].Pos - Spacing;
                it.Pos = Mathf.Max(it.Pos, Mathf.Min(it.Pos + Speed, limit));
            }
            if (lane.Count == 0) continue;
            Working = true;
            if (lane[0].Pos >= 1f && w.At(Pos + Dirs[d]) is { } next && next.CanAccept(w, lane[0].Id, Pos))
            {
                next.Accept(w, lane[0].Id, Pos);
                lane.RemoveAt(0);
            }
        }
    }

    public override void Contents(List<string> into) { foreach (var lane in Lanes) foreach (var it in lane) into.Add(it.Id); }

    public override void Save(EntityData d)
    {
        d.Items = new List<string>();
        d.Pos = new List<float>();
        for (int i = 0; i < 4; i++) foreach (var it in Lanes[i]) { d.Items.Add($"{i}|{it.Id}"); d.Pos.Add(it.Pos); }
    }

    public override void Load(EntityData d)
    {
        if (d.Items == null) return;
        for (int k = 0; k < d.Items.Count; k++)
        {
            var parts = d.Items[k].Split('|', 2);
            if (parts.Length == 2 && int.TryParse(parts[0], out int i) && i is >= 0 and < 4)
                Lanes[i].Add(new Item { Id = parts[1], Pos = d.Pos[k], Prev = d.Pos[k] });
        }
    }
}

// Burns coal for its power network. Fed by inserters or straight from a belt.
public sealed class Generator : Entity
{
    public const int FuelCap = 10;
    public int Fuel;      // coal waiting to burn
    public float Energy;  // kJ left from the coal that is burning now

    public World.FluidNet Gas; // a sparkgas pipe next to it, looked up by the world each tick

    public bool CanSupply => Energy > 0 || Fuel > 0 || Gas is { Fluid: "sparkgas", Amount: >= 1 };
    public override bool CanAccept(World w, string item, Vector2I from) => item == "coal" && Fuel < FuelCap;
    public override void Accept(World w, string item, Vector2I from, float overflow = 0) => Fuel++;

    // Takes kJ from the burning coal, lighting the next piece when needed. Returns what it could give.
    public float Draw(float kj)
    {
        if (Energy < kj && Fuel > 0) { Fuel--; Energy += Defs.CoalKJ; }
        if (Energy < kj && Gas != null) Energy += Gas.Drain("sparkgas", Mathf.CeilToInt((kj - Energy) / Defs.SparkgasKJ)) * Defs.SparkgasKJ;
        float got = Mathf.Min(Energy, kj);
        Energy -= got;
        Working = got > 0;
        return got;
    }

    public override string Status => Idle > StallTicks && !CanSupply ? "starved" : null;
    public override void Contents(List<string> into) { for (int i = 0; i < Fuel; i++) into.Add("coal"); }
    public override void Save(EntityData d) { d.An = Fuel; d.P = Energy; }
    public override void Load(EntityData d) { Fuel = d.An; Energy = d.P; }
}

// Links to poles up to Defs.WireRange away and powers machines within Defs.PoleReach tiles.
public sealed class Pole : Entity { }

// Everything delivered here goes into central storage and counts toward the current milestone.
public sealed class Hub : Entity
{
    public override bool CanAccept(World w, string item, Vector2I from) => true;
    public override void Accept(World w, string item, Vector2I from, float overflow = 0) => w.Deliver(item, from);
}

// Takes items from behind and deals them out in turn to the front, right and left, skipping any side that can't take one.
public sealed class Splitter : Entity
{
    public const float Speed = 0.2f, Spacing = 0.25f;
    public sealed class Item { public string Id; public float Pos, Prev; }
    public readonly List<Item> Lane = new(); // front first
    public int Next; // the output that gets the next item: 0 front, 1 right, 2 left
    static readonly int[] Turn = { 0, 1, 3 };

    public Vector2I OutTile(int k) => Pos + Dirs[(Rot + Turn[k]) % 4];
    public override bool OutputsTo(Vector2I t) => t != Back && (t - Pos).LengthSquared() == 1;
    public override bool CanAccept(World w, string item, Vector2I from) => from == Back && (Lane.Count == 0 || Lane[^1].Pos >= Spacing);
    public override void Accept(World w, string item, Vector2I from, float overflow = 0) => Lane.Add(new Item { Id = item, Prev = -Speed });

    public override void Tick(World w)
    {
        Working = Lane.Count > 0;
        for (int i = 0; i < Lane.Count; i++)
        {
            var it = Lane[i];
            it.Prev = it.Pos;
            float limit = i == 0 ? 1f : Lane[i - 1].Pos - Spacing;
            it.Pos = Mathf.Max(it.Pos, Mathf.Min(it.Pos + Speed, limit));
        }
        if (Lane.Count == 0 || Lane[0].Pos < 1f) return;
        for (int k = 0; k < 3; k++)
        {
            int o = (Next + k) % 3;
            if (w.At(OutTile(o)) is not { } next || !next.CanAccept(w, Lane[0].Id, Pos)) continue;
            next.Accept(w, Lane[0].Id, Pos);
            Lane.RemoveAt(0);
            Next = (o + 1) % 3;
            return;
        }
    }

    public override string Status => Idle > StallTicks && Lane.Count > 0 ? "blocked" : null;
    public override void Contents(List<string> into) { foreach (var it in Lane) into.Add(it.Id); }

    public override void Save(EntityData d)
    {
        d.Items = Lane.Select(i => i.Id).ToList();
        d.Pos = Lane.Select(i => i.Pos).ToList();
        d.Bn = Next;
    }

    public override void Load(EntityData d)
    {
        Next = d.Bn % 3;
        if (d.Items != null) for (int i = 0; i < d.Items.Count; i++) Lane.Add(new Item { Id = d.Items[i], Pos = d.Pos[i], Prev = d.Pos[i] });
    }
}

// Underground belt. The entrance takes items from behind and carries them under the ground to its exit,
// which drops them in front of itself. Only the entrance holds items: Pos runs 0 (entrance back edge)
// to distance + 1 (exit front edge).
public sealed class Tunnel : Entity
{
    public const float Speed = 0.15f, Spacing = 0.25f;
    public sealed class Item { public string Id; public float Pos, Prev; }
    public readonly List<Item> Lane = new(); // front first

    public override bool OutputsTo(Vector2I t) => Exit && t == Front;
    public override bool CanAccept(World w, string item, Vector2I from) => !Exit && from == Back && (Lane.Count == 0 || Lane[^1].Pos >= Spacing);
    public override void Accept(World w, string item, Vector2I from, float overflow = 0) => Lane.Add(new Item { Id = item, Prev = -Speed });

    public override void Tick(World w)
    {
        Working = Lane.Count > 0;
        if (Exit || Lane.Count == 0) return;
        var exit = w.Partner(this);
        float end = exit == null ? 0.5f : Length(exit) + 1; // without an exit, items wait at the entrance
        for (int i = 0; i < Lane.Count; i++)
        {
            var it = Lane[i];
            it.Prev = it.Pos;
            float limit = i == 0 ? end : Lane[i - 1].Pos - Spacing;
            it.Pos = Mathf.Max(it.Pos, Mathf.Min(it.Pos + Speed, limit));
        }
        if (exit != null && Lane[0].Pos >= end && w.At(exit.Front) is { } next && next.CanAccept(w, Lane[0].Id, exit.Pos))
        {
            next.Accept(w, Lane[0].Id, exit.Pos);
            Lane.RemoveAt(0);
        }
    }

    public int Length(Entity exit) => Mathf.Abs(exit.Pos.X - Pos.X) + Mathf.Abs(exit.Pos.Y - Pos.Y);
    public override void Contents(List<string> into) { foreach (var it in Lane) into.Add(it.Id); }

    public override void Save(EntityData d)
    {
        d.Items = Lane.Select(i => i.Id).ToList();
        d.Pos = Lane.Select(i => i.Pos).ToList();
    }

    public override void Load(EntityData d)
    {
        if (d.Items != null) for (int i = 0; i < d.Items.Count; i++) Lane.Add(new Item { Id = d.Items[i], Pos = d.Pos[i], Prev = d.Pos[i] });
    }
}

// Plain pipes and underground pipes. Connected pipes share one World.FluidNet; Fluid and Amount are this
// tile's share of it, written back by World.SpreadFluids before a save or a network rebuild.
public sealed class Pipe : Entity
{
    public string Fluid;
    public float Amount;

    // Plain pipes join on all four sides; an underground pipe only on its open end (and to its partner).
    public bool Opens(Vector2I t) => Type == "pipe" || t == (Exit ? Front : Back);
    public override void Save(EntityData d) { d.A = Fluid; d.P = Amount; }
    public override void Load(EntityData d) { Fluid = Defs.IsFluid(d.A) ? d.A : null; Amount = Fluid == null ? 0 : d.P; }
}

// Pumps water from the ground into the pipe in front of it. Works on any tile.
public sealed class Pump : Entity
{
    public override void Tick(World w) =>
        Working = (w.FluidNetAt(Front, Pos)?.Fill("water", Defs.PumpRate * w.SpeedOf(this)) ?? 0) > 0;

    public override string Status => Idle > StallTicks ? "blocked" : null;
}

// An assembler for fluids: its fluid ingredient comes from the pipe behind it, item ingredients from inserters.
// A fluid product goes into the pipe in front; an item product waits for an inserter.
public sealed class Refinery : Assembler
{
    protected override int OutCap => Recipe is { } r && Defs.IsFluid(r.Out) ? 100 : 10;

    public override void Tick(World w)
    {
        if (Recipe is { } r)
            foreach (var (i, n) in r.In)
                if (Defs.IsFluid(i) && n * 2 - Inv.GetValueOrDefault(i) is > 0 and var want && w.FluidNetAt(Back, Pos) is { } src)
                    AddInv(Inv, i, src.Drain(i, want));
        base.Tick(w);
        if (Recipe is { } o && Defs.IsFluid(o.Out) && OutN > 0 && w.FluidNetAt(Front, Pos) is { } dst)
            OutN -= (int)dst.Fill(o.Out, Mathf.Min(OutN, Mathf.Floor(dst.Room(o.Out))));
    }

    public override string TakeAny(Func<string, bool> ok) => Recipe is { } r && Defs.IsFluid(r.Out) ? null : base.TakeAny(ok);

    // Fluids are not refunded: there is nowhere to put them.
    public override void Contents(List<string> into)
    {
        base.Contents(into);
        into.RemoveAll(Defs.IsFluid);
    }
}
