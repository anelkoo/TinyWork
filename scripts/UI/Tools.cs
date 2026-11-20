using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TinyWorks.Sim;
using TinyWorks.View;

namespace TinyWorks.UI;

// Step 6 tools on the HUD: game speed buttons, the stats window, blueprints, the welcome-back card and the cosmetics shop.
public sealed partial class Hud
{
    void UpdateSpeed()
    {
        var key = W.Paused ? "pause" : W.Speed.ToString();
        if (key == _shownSpeed) return;
        _shownSpeed = key;
        int active = W.Paused ? 0 : W.Speed switch { 2 => 2, 4 => 3, _ => 1 };
        for (int i = 0; i < 4; i++)
            if (i == active) _speedBtns[i].AddThemeStyleboxOverride("normal", Ui.Box(Ui.BtnHover, Ui.Accent, 3));
            else _speedBtns[i].RemoveThemeStyleboxOverride("normal");
        _speedBtns[0].Text = W.Paused ? "Paused" : "Pause";
    }

    // ---------- Stats ----------

    // A bar per 10 s bucket over the last 10 minutes, scaled to its own highest bar.
    static Control Graph(World w, string key, Vector2 size, Color col)
    {
        var g = new Control { CustomMinimumSize = size, MouseFilter = MouseFilterEnum.Pass, TooltipText = "Last 10 minutes, one bar per 10 seconds" };
        g.Draw += () =>
        {
            var h = w.History;
            float bw = g.Size.X / World.StatBuckets, max = Math.Max(1, h.Count == 0 ? 0 : h.Max(b => b.GetValueOrDefault(key)));
            g.DrawRect(new Rect2(0, g.Size.Y - 1, g.Size.X, 1), Ui.Border);
            for (int i = 0; i < h.Count; i++)
            {
                float v = h[i].GetValueOrDefault(key) / max * (g.Size.Y - 2);
                if (v > 0) g.DrawRect(new Rect2(g.Size.X - (h.Count - i) * bw, g.Size.Y - 1 - v, Math.Max(1, bw - 1), v), col);
            }
        };
        return g;
    }

    public void OpenStats()
    {
        var graphs = new List<Control>();
        var rates = new List<(Label Label, string Key)>();
        int shownBuckets = W.History.Count;
        var body = OpenPanel("Stats", 600, () =>
        {
            if (W.History.Count == shownBuckets) return;
            shownBuckets = W.History.Count;
            foreach (var g in graphs) g.QueueRedraw();
            foreach (var (l, k) in rates) l.Text = PerMin(k);
        });
        string PerMin(string k) => $"{Compact((long)Math.Round(W.PerMinute(k)))}/min";

        var coins = Ui.MakeLabel(PerMin(World.CoinKey), 20, new Color("f7c948"));
        rates.Add((coins, World.CoinKey));
        var coinGraph = Graph(W, World.CoinKey, new Vector2(_mini ? 150 : 260, 36), new Color("f7c948"));
        graphs.Add(coinGraph);
        body.AddChild(Ui.Row(Ui.Icon(Sprites.Coin, 20), Ui.Grow(Ui.MakeLabel("Coins earned", 15)), coins, coinGraph));

        body.AddChild(Ui.MakeLabel("Made per minute", 16));
        var made = Defs.Items.Keys.Where(id => W.History.Any(b => b.ContainsKey(id))).OrderByDescending(W.PerMinute).ToList();
        if (made.Count == 0)
        {
            var empty = Ui.MakeLabel("Nothing made yet. Miners, furnaces, assemblers and refineries show up here within 10 seconds of working.", 13, Ui.Muted);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            body.AddChild(empty);
        }
        foreach (var id in made)
        {
            var rate = Ui.MakeLabel(PerMin(id), 14, Ui.Muted);
            rate.CustomMinimumSize = new Vector2(70, 0);
            rate.HorizontalAlignment = HorizontalAlignment.Right;
            rates.Add((rate, id));
            var g = Graph(W, id, new Vector2(_mini ? 110 : 240, 24), Defs.Items[id].Color);
            graphs.Add(g);
            body.AddChild(Ui.Row(Ui.Icon(Sprites.Items[id]), Ui.Grow(Ui.MakeLabel(Defs.Items[id].Name, 14)), rate, g));
        }
        var note = Ui.MakeLabel("Counts everything machines and your hands make, while the game runs. Updates every 10 seconds.", 12, Ui.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(note);
    }

    // ---------- Blueprints ----------

    public void OpenBlueprints()
    {
        var all = Persist.LoadBlueprints();
        var body = OpenPanel("Blueprints", 520);
        var clip = View.Clip;
        if (clip is { Count: > 0 })
        {
            var name = new LineEdit { PlaceholderText = "Name this copy", SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 40 };
            var save = Ui.MakeButton("Save", null, Sfx);
            void Changed(string t) { save.Disabled = t.Trim().Length == 0; save.Text = all.ContainsKey(t.Trim()) ? "Replace" : "Save"; }
            void Save()
            {
                var key = name.Text.Trim();
                if (key.Length == 0) return;
                all[key] = clip;
                try { Persist.SaveBlueprints(all); Toast($"Blueprint \"{key}\" saved", Ui.Good, 2.5f); Sfx.Play("coin"); }
                catch (Exception e) { Toast("Couldn't save the blueprint: " + e.Message, Ui.Bad, 5); Sfx.Play("error"); }
                OpenBlueprints();
            }
            name.TextChanged += Changed;
            name.TextSubmitted += _ => Save();
            save.Pressed += Save;
            Changed("");
            body.AddChild(Ui.MakeLabel($"Current copy: {clip.Count} building{(clip.Count == 1 ? "" : "s")}", 14, Ui.Muted));
            body.AddChild(Ui.Row(name, save));
        }
        else body.AddChild(Ui.MakeLabel("Copy an area first: Ctrl+C, then drag a box over it.", 13, Ui.Muted));

        if (all.Count == 0) body.AddChild(Ui.MakeLabel("No blueprints yet. They're shared by every save.", 13, Ui.Muted));
        foreach (var (key, parts) in all)
        {
            var use = Ui.MakeButton("Use", () =>
            {
                ClosePanel();
                View.StartPaste(parts.Select(p => p).ToList());
                Toast($"{key}: click to place, R turns it, Esc stops");
            }, Sfx);
            use.Disabled = parts.Count == 0;
            Button del = null;
            bool armed = false;
            del = Ui.MakeButton("Delete", () =>
            {
                if (!armed) { armed = true; del.Text = "Delete?"; del.TooltipText = "Click again to delete it for good"; return; }
                all.Remove(key);
                try { Persist.SaveBlueprints(all); } catch (Exception e) { Toast("Couldn't delete: " + e.Message, Ui.Bad, 5); }
                OpenBlueprints();
            }, Sfx);
            body.AddChild(Ui.Row(Ui.Grow(Ui.MakeLabel(key, 15)), Ui.MakeLabel($"{parts.Count} buildings", 13, Ui.Muted), use, del));
        }
    }

    // ---------- Welcome back ----------

    PanelContainer _welcome;

    // Not a modal window, and not in the middle of the map: it must not take keys or clicks while you start playing.
    // It sits where the tutorial card goes (top right) and the tutorial waits until it is gone.
    void BuildWelcome()
    {
        if (W.LastSession is not { Seconds: >= 30 } last) return;
        var card = _welcome = new PanelContainer { CustomMinimumSize = new Vector2(_mini ? 0 : 320, 0) };
        card.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        card.GrowHorizontal = GrowDirection.Begin;
        card.Position += _mini ? new Vector2(-4, 46) : new Vector2(-10, 58);
        void Close()
        {
            if (!IsInstanceValid(card) || _welcome != card) return;
            card.QueueFree();
            _welcome = null;
            ShowTutorial();
        }
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        card.AddChild(v);
        var ok = Ui.MakeButton("Let's go", Close, Sfx);
        ok.FocusMode = FocusModeEnum.None;
        v.AddChild(Ui.MakeLabel($"Welcome back to {W.Name}", 18, Ui.Accent));
        long mins = (long)Math.Round(last.Seconds / 60);
        var lines = new List<string> { $"Last time you played for {(mins >= 60 ? $"{mins / 60} h {mins % 60} min" : $"{Math.Max(1, mins)} min")}." };
        static string Count(long n, string word) => $"{Compact(n)} {word}{(n == 1 ? "" : "s")}";
        if (last.Made > 0 || last.Delivered > 0) lines.Add($"Your factory made {Count(last.Made, "item")} and the Hub got {Count(last.Delivered, "item")}.");
        if (last.Coins > 0) lines.Add($"You earned {Count(last.Coins, "coin")}.");
        if (last.Built > 0) lines.Add($"You placed {Count(last.Built, "building")}.");
        if (last.Milestones > 0) lines.Add($"You reached {last.Milestones} milestone{(last.Milestones == 1 ? "" : "s")}.");
        foreach (var l in lines) v.AddChild(Wrapped(l));
        v.AddChild(Ui.MakeLabel("Nothing runs while the game is closed, so everything is where you left it.", 12, Ui.Muted));
        v.AddChild(ok);
        AddChild(card);
        MoveChild(card, 0); // under panels and toasts
        Ui.PopIn(card);
        GetTree().CreateTimer(30).Timeout += Close;
    }

    // ---------- Cosmetics ----------

    static Texture2D Swatch(Color c)
    {
        var img = Image.CreateEmpty(14, 14, false, Image.Format.Rgba8);
        img.Fill(Ui.Ink);
        img.FillRect(new Rect2I(1, 1, 12, 12), c);
        return ImageTexture.CreateFromImage(img);
    }

    void FillCosmetics(VBoxContainer body, List<Action> refresh)
    {
        body.AddChild(Ui.MakeLabel("Cosmetics", 16));
        foreach (var (slot, title) in new[] { ("belt", "Belt colour"), ("machine", "Machine colour"), ("hat", "Hub hat"), ("particle", "Particles") })
        {
            body.AddChild(Ui.MakeLabel(title, 13, Ui.Muted));
            var flow = new HFlowContainer();
            flow.AddThemeConstantOverride("h_separation", 6);
            flow.AddThemeConstantOverride("v_separation", 6);
            body.AddChild(flow);
            foreach (var c in Defs.Cosmetics.Where(x => x.Slot == slot).Prepend(null))
            {
                var b = Ui.MakeButton("", () =>
                {
                    if (c == null || W.Owned.Contains(c.Id)) W.Wear(slot, c?.Id);
                    else if (!W.BuyCosmetic(c.Id)) Sfx.Play("error");
                }, Sfx);
                b.Icon = Swatch(c?.Color ?? Sprites.MetalDark);
                b.TooltipText = c == null ? "Back to the plain look" : $"{c.Name}: {c.Cost} coins once, then free to switch";
                string shown = null;
                refresh.Add(() =>
                {
                    bool own = c == null || W.Owned.Contains(c.Id), on = W.Worn(slot) == c;
                    var state = $"{own}{on}{(own || W.Coins >= c.Cost)}";
                    if (state == shown) return;
                    shown = state;
                    b.Text = (c?.Name ?? "Plain") + (own ? "" : $" · {c.Cost}c");
                    b.Disabled = !own && W.Coins < c.Cost;
                    if (on) b.AddThemeStyleboxOverride("normal", Ui.Box(Ui.BtnHover, Ui.Accent, 3));
                    else b.RemoveThemeStyleboxOverride("normal");
                });
                flow.AddChild(b);
            }
        }
    }
}
