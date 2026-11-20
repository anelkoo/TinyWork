using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TinyWorks.Sim;
using TinyWorks.View;

namespace TinyWorks.UI;

// In-game overlay at full resolution: storage, coins, milestone tracker, hotbar, panels, toasts, key hints.
public sealed partial class Hud : Control
{
    public WorldView View;
    public Sfx Sfx;
    public Settings S;
    public event Action OpenMenu, ToggleMini;

    World W => View.W;
    readonly Dictionary<string, (Control Row, Label Count, double Shown)> _rows = new();
    VBoxContainer _storage;
    PanelContainer _storagePanel, _tracker;
    HBoxContainer _bar, _coinRow;
    Label _coins, _toast, _hints, _trackerTitle;
    VBoxContainer _trackerRows;
    Button _miniBtn;
    ColorRect _flash;
    Control _panel;           // the open modal panel, if any
    Action _panelTick;        // refreshes the open panel's numbers each frame
    float _toastT;
    double _coinsShown;
    bool _mini;
    int _seenMilestone = -1;
    string _shownSel = "";    // "" forces the first restyle

    public bool HasPanel => _panel != null;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _storagePanel = new PanelContainer { Position = new Vector2(10, 10), MouseFilter = MouseFilterEnum.Ignore };
        _storage = new VBoxContainer();
        _storage.AddThemeConstantOverride("separation", 2);
        _storagePanel.AddChild(_storage);
        AddChild(_storagePanel);
        _coins = Ui.MakeLabel("0", 16, new Color("f7c948"));
        _coinRow = Ui.Row(Ui.Icon(Sprites.Coin), _coins);
        _coinRow.TooltipText = "Coins: turn on auto-sell in the Hub to earn them";
        _storage.AddChild(_coinRow);
        foreach (var id in Defs.Items.Keys)
        {
            var count = Ui.MakeLabel("0");
            var row = Ui.Row(Ui.Icon(Sprites.Items[id]), count);
            row.Visible = false;
            row.TooltipText = Defs.Items[id].Name;
            _storage.AddChild(row);
            _rows[id] = (row, count, 0);
        }

        _tracker = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        _tracker.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        _tracker.GrowHorizontal = GrowDirection.Both;
        _tracker.Position += new Vector2(0, 10);
        var tv = new VBoxContainer();
        tv.AddThemeConstantOverride("separation", 4);
        _trackerTitle = Ui.MakeLabel("", 14);
        _trackerTitle.HorizontalAlignment = HorizontalAlignment.Center;
        tv.AddChild(_trackerTitle);
        _trackerRows = new VBoxContainer();
        tv.AddChild(_trackerRows);
        _tracker.AddChild(tv);
        AddChild(_tracker);

        _bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _bar.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        _bar.GrowHorizontal = GrowDirection.Both;
        _bar.GrowVertical = GrowDirection.Begin;
        _bar.Position -= new Vector2(0, 10);
        AddChild(_bar);

        var top = new HBoxContainer();
        top.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        top.GrowHorizontal = GrowDirection.Begin;
        top.Position += new Vector2(-10, 10);
        AddChild(top);
        var build = Ui.MakeButton("Build", OpenBuildMenu, Sfx);
        build.TooltipText = "All buildings (Q)";
        _miniBtn = Ui.MakeButton("Mini", () => ToggleMini?.Invoke(), Sfx);
        var menu = Ui.MakeButton("Menu", () => OpenMenu?.Invoke(), Sfx);
        menu.TooltipText = "Save, settings, main menu, quit (Esc)";
        foreach (var b in new[] { build, _miniBtn, menu }) { b.FocusMode = FocusModeEnum.None; top.AddChild(b); }

        _toast = Ui.MakeLabel("", 16, Ui.Accent);
        _toast.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        _toast.GrowHorizontal = GrowDirection.Both;
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        _toast.Position -= new Vector2(0, 90);
        _toast.AddThemeColorOverride("font_outline_color", Ui.Ink);
        _toast.AddThemeConstantOverride("outline_size", 6);
        AddChild(_toast);

        _hints = Ui.MakeLabel("Click ore to mine   Click a building to open it   1-9 build (1 again: faster belts)   Q all buildings   R rotate   Right-click remove   V underground   F7 mini   F8 hide", 13, Ui.Text);
        _hints.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        _hints.GrowVertical = GrowDirection.Begin;
        _hints.Position += new Vector2(10, -66);
        _hints.AddThemeColorOverride("font_outline_color", Ui.Ink);
        _hints.AddThemeConstantOverride("outline_size", 5);
        AddChild(_hints);

        _flash = new ColorRect { Color = Colors.White with { A = 0 }, MouseFilter = MouseFilterEnum.Ignore };
        _flash.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_flash);

        View.Inspect = Inspect;
        _seenMilestone = W.MilestoneIndex;
        RebuildHotbar();
        RebuildTracker();
    }

    public void Toast(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _toast.Text = text;
        _toastT = 2.5f;
        if (Ui.Enabled) { _toast.PivotOffset = _toast.Size / 2; _toast.Scale = Vector2.One * 1.25f; CreateTween().TweenProperty(_toast, "scale", Vector2.One, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out); }
    }

    public void SetMini(bool mini)
    {
        _mini = mini;
        _miniBtn.Text = mini ? "Big" : "Mini";
        _miniBtn.TooltipText = mini ? "Back to the normal window (F7)" : "Switch to the small always-on-top window (F7)";
        foreach (var r in _rows.Values) r.Count.AddThemeFontSizeOverride("font_size", mini ? 12 : 16);
        _coins.AddThemeFontSizeOverride("font_size", mini ? 12 : 16);
        _storagePanel.Position = mini ? new Vector2(4, 4) : new Vector2(10, 10);
        _trackerTitle.AddThemeFontSizeOverride("font_size", mini ? 12 : 14);
        ClosePanel(); // panels are sized for the window they opened in
        RebuildHotbar();
        RebuildTracker();
        ApplyStats();
    }

    // Shrink the tracker to its content, then pin it: top centre normally, under the buttons in mini
    // (the buttons take the top centre there).
    void PlaceTracker()
    {
        _tracker.ResetSize();
        _tracker.SetAnchorsAndOffsetsPreset(_mini ? LayoutPreset.TopRight : LayoutPreset.CenterTop);
        _tracker.GrowHorizontal = _mini ? GrowDirection.Begin : GrowDirection.Both;
        _tracker.Position += _mini ? new Vector2(-4, 46) : new Vector2(0, 10);
    }

    // Which HUD pieces show is a setting (each "stat" can be turned off).
    public void ApplyStats()
    {
        _storagePanel.Visible = S.StatOn("storage") || S.StatOn("coins");
        _coinRow.Visible = S.StatOn("coins");
        _hints.Visible = !_mini && S.StatOn("hints");
        _tracker.Visible = S.StatOn("milestone") && !W.Free;
    }

    void RebuildHotbar()
    {
        foreach (var c in _bar.GetChildren()) c.QueueFree();
        var list = View.Hotbar();
        float s = _mini ? 30 : 48;
        for (int i = 0; i < list.Count && i < 9; i++)
        {
            var id = list[i];
            var def = Defs.Buildings[id];
            var b = new Button { Icon = Sprites.Icons[id], ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(s, s) };
            b.TooltipText = $"{def.Name}  ({i + 1})\nCosts {Ui.Cost(def.Cost)}";
            b.AddThemeConstantOverride("icon_max_width", _mini ? 20 : 32);
            b.Pressed += () => View.Select(id);
            var key = Ui.MakeLabel((i + 1).ToString(), 11, Ui.Muted);
            key.Position = new Vector2(4, 1);
            key.MouseFilter = MouseFilterEnum.Ignore;
            b.AddChild(key);
            b.SetMeta("id", id);
            Ui.Squishy(b);
            _bar.AddChild(b);
        }
        _shownSel = "";
    }

    void RebuildTracker()
    {
        foreach (var c in _trackerRows.GetChildren()) { _trackerRows.RemoveChild(c); c.QueueFree(); } // gone now, so the size shrinks now
        Callable.From(PlaceTracker).CallDeferred();
        if (W.CurrentMilestone is not { } m)
        {
            _trackerTitle.Text = "All milestones done. More coming soon!";
            return;
        }
        _trackerTitle.Text = $"Milestone {W.MilestoneIndex + 1}/{Defs.Milestones.Length}: {m.Name}";
        if (_mini) return; // mini shows the title with a percentage only
        foreach (var (item, n) in m.Need)
        {
            var bar = new ProgressBar { MaxValue = n, ShowPercentage = false, CustomMinimumSize = new Vector2(140, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            bar.SetMeta("item", item);
            var label = Ui.MakeLabel($"0/{n}", 13, Ui.Muted);
            _trackerRows.AddChild(Ui.Row(Ui.Icon(Sprites.Items[item]), bar, label));
        }
        _trackerRows.AddChild(Ui.MakeLabel("Unlocks " + UnlockNames(m), 12, Ui.Muted));
    }

    static string UnlockNames(Milestone m) =>
        string.Join(", ", m.Unlocks.Select(u => Defs.Buildings.TryGetValue(u, out var b) ? b.Name : u == "steel" ? "Steel smelting" : u));

    void UpdateTracker()
    {
        if (W.CurrentMilestone is not { } m) return;
        if (_mini)
        {
            double done = m.Need.Sum(x => Math.Min(W.Delivered.GetValueOrDefault(x.Item), x.Count)), all = m.Need.Sum(x => x.Count);
            _trackerTitle.Text = $"{m.Name}  {done / all:P0}";
            return;
        }
        foreach (var row in _trackerRows.GetChildren())
        {
            if (row.GetChildCount() < 3 || row.GetChild(1) is not ProgressBar bar) continue;
            var item = (string)bar.GetMeta("item");
            long have = Math.Min(W.Delivered.GetValueOrDefault(item), (long)bar.MaxValue);
            bar.Value = Ui.Enabled ? Mathf.Lerp(bar.Value, have, 0.2) : have;
            ((Label)row.GetChild(2)).Text = $"{have}/{bar.MaxValue}";
        }
    }

    // Milestones are noticed from world state, so one reached while hidden still unlocks here.
    void CheckMilestone()
    {
        if (W.MilestoneIndex == _seenMilestone) return;
        var reached = Defs.Milestones[Math.Clamp(_seenMilestone, 0, Defs.Milestones.Length - 1)];
        _seenMilestone = W.MilestoneIndex;
        RebuildHotbar();
        RebuildTracker();
        Toast($"Milestone reached: {reached.Name}!  Unlocked {UnlockNames(reached)}");
        Sfx.Play("milestone", 0);
        View.Celebrate();
        if (S.JuiceOn("screen", _mini))
        {
            _flash.Color = Colors.White with { A = 0.7f };
            CreateTween().TweenProperty(_flash, "color:a", 0f, 0.6);
            Engine.TimeScale = 0.3; // slow motion; a timer that ignores time scale puts it back
            GetTree().CreateTimer(0.7, true, false, true).Timeout += () => Engine.TimeScale = 1;
        }
    }

    public override void _Process(double delta)
    {
        if (View?.W == null) return;
        float dt = (float)delta;
        CheckMilestone();
        UpdateTracker();
        bool showStorage = S.StatOn("storage");
        foreach (var id in Defs.Items.Keys)
        {
            var (row, label, shown) = _rows[id];
            long real = W.Storage.GetValueOrDefault(id);
            row.Visible = showStorage && (real > 0 || shown >= 0.5);
            if (!row.Visible) continue;
            double next = Ui.Enabled ? Mathf.Lerp(shown, real, 1 - Mathf.Exp(-10 * dt)) : real; // counters tick up
            if (Math.Abs(next - real) < 0.5) next = real;
            if ((long)Math.Round(next) > (long)Math.Round(shown) && Ui.Enabled) label.Modulate = Ui.Accent.Lightened(0.3f); // flash on gain
            label.Modulate = label.Modulate.Lerp(Colors.White, 1 - Mathf.Exp(-6 * dt));
            label.Text = Compact((long)Math.Round(next));
            _rows[id] = (row, label, next);
        }
        double coins = Ui.Enabled ? Mathf.Lerp(_coinsShown, W.Coins, 1 - Mathf.Exp(-8 * dt)) : W.Coins;
        if (Math.Abs(coins - W.Coins) < 0.5) coins = W.Coins;
        _coinsShown = coins;
        _coins.Text = Compact((long)Math.Round(coins));

        if (_shownSel != View.Selected)
        {
            _shownSel = View.Selected;
            foreach (var n in _bar.GetChildren())
                if (n is Button b && b.HasMeta("id"))
                {
                    var id = (string)b.GetMeta("id");
                    bool sel = id == View.Selected || id == "belt" && Defs.IsBelt(View.Selected);
                    if (id == "belt" && Defs.IsBelt(View.Selected)) b.Icon = Sprites.Icons[View.Selected]; // slot 1 shows the belt tier in hand
                    b.AddThemeStyleboxOverride("normal", Ui.Box(sel ? Ui.BtnHover : Ui.Btn, sel ? Ui.Accent : Ui.Border, sel ? 3 : 2));
                }
        }
        _toastT -= dt;
        _toast.Modulate = Colors.White with { A = Mathf.Clamp(_toastT, 0, 1) };
        _panelTick?.Invoke();
    }

    public static string Compact(long n) => n >= 1_000_000 ? $"{n / 1e6:0.#}M" : n >= 10_000 ? $"{n / 1e3:0.#}k" : n.ToString();

    // ---------- Panels ----------

    // A dimmed overlay with a centred panel. Sized to fit the current window, scrolls when content is long.
    public VBoxContainer OpenPanel(string title, float width = 520, Action tick = null)
    {
        ClosePanel();
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.35f) };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) ClosePanel(); };
        AddChild(dim);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(Mathf.Min(width, Size.X - 16), Mathf.Min(_mini ? 280 : 480, Size.Y - 16)) };
        center.AddChild(panel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        panel.AddChild(v);
        var close = Ui.MakeButton("Close", ClosePanel, Sfx);
        close.TooltipText = "Close (Esc)";
        v.AddChild(Ui.Row(Ui.Grow(Ui.MakeLabel(title, _mini ? 16 : 20)), close));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(body);
        _panel = dim;
        _panelTick = tick;
        Ui.PopIn(panel);
        return body;
    }

    public void ClosePanel()
    {
        _panel?.QueueFree();
        _panel = null;
        _panelTick = null;
    }

    public void Inspect(Entity e)
    {
        switch (e)
        {
            case Hub: OpenHub(false); break;
            case Assembler a: OpenRecipePicker(a); break;
            case Inserter i: OpenFilterPicker(i); break;
            case Chest c: OpenChest(c); break;
            case Pipe p: OpenPipe(p); break;
            case Pole or Generator: OpenPower(e); break;
        }
    }

    void OpenPower(Entity clicked)
    {
        Label status = null, use = null, gens = null, speed = null;
        ProgressBar bar = null;
        var body = OpenPanel("Power", 440, () =>
        {
            var net = W.NetOf(clicked);
            if (net == null || net.Poles.Count == 0)
            {
                status.Text = "Not connected. Put a power pole within 2 tiles of this generator.";
                use.Text = gens.Text = speed.Text = "";
                bar.Value = 0;
                return;
            }
            static string N(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
            status.Text = $"{N(net.Poles.Count, "pole")}, {N(net.Gens.Count, "generator")}, {N(net.Users.Count, "machine")}";
            use.Text = $"Using {net.Demand:0} kW of {net.Capacity:0} kW";
            gens.Text = $"Coal in generators: {net.Gens.Sum(g => g.Fuel)}" + (net.Gens.Count == 0 ? "  (no generator on this network yet)" : "");
            speed.Text = $"Machines here run at {100 * (1 + net.Satisfaction):0}% speed";
            bar.Value = Ui.Enabled ? Mathf.Lerp(bar.Value, net.Satisfaction, 0.2) : net.Satisfaction;
        });
        status = Ui.MakeLabel("", 14);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        use = Ui.MakeLabel("", 14, Ui.Muted);
        gens = Ui.MakeLabel("", 14, Ui.Muted);
        speed = Ui.MakeLabel("", 16, Ui.Accent);
        bar = new ProgressBar { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 12) };
        foreach (var c in new Control[] { status, speed, bar, use, gens }) body.AddChild(c);
        var info = Ui.MakeLabel("Power is a bonus: machines work without it, and run up to twice as fast with enough power. Generators burn coal; feed them with an inserter or a belt.", 12, Ui.Muted);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(info);
    }

    public void OpenBuildMenu()
    {
        var body = OpenPanel("All Buildings", 560);
        var grid = new GridContainer { Columns = _mini ? 2 : 4 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        body.AddChild(grid);
        foreach (var id in Defs.BuildOrder)
        {
            var def = Defs.Buildings[id];
            bool open = W.IsUnlocked(id);
            var b = new Button { Text = def.Name, Icon = Sprites.Icons[id], ExpandIcon = true, Disabled = !open, CustomMinimumSize = new Vector2(_mini ? 150 : 124, 44), Alignment = HorizontalAlignment.Left };
            b.AddThemeConstantOverride("icon_max_width", 24);
            b.TooltipText = open ? $"Costs {Ui.Cost(def.Cost)}" : $"Unlocks with milestone {Defs.UnlockTier(id)}";
            b.Pressed += () => { View.Selected = id; Sfx.Play("click"); ClosePanel(); };
            Ui.Squishy(b);
            grid.AddChild(b);
        }
        body.AddChild(Ui.MakeLabel("Locked buildings unlock with milestones. Hover one to see which.", 13, Ui.Muted));
    }

    public void OpenHub(bool shop)
    {
        Label coins = null;
        var refresh = new List<Action>();
        var body = OpenPanel("Hub", 560, () => { coins.Text = $"{W.Coins} coins"; foreach (var r in refresh) r(); });
        coins = Ui.MakeLabel("", 16, new Color("f7c948"));
        var sellTab = Ui.MakeButton("Sell", () => OpenHub(false), Sfx);
        var shopTab = Ui.MakeButton("Shop", () => OpenHub(true), Sfx);
        (shop ? shopTab : sellTab).AddThemeStyleboxOverride("normal", Ui.Box(Ui.BtnHover, Ui.Accent, 3));
        body.AddChild(Ui.Row(sellTab, shopTab, Ui.Icon(Sprites.Coin), coins));

        if (!shop)
        {
            var info = Ui.MakeLabel("Auto-sell turns items delivered here into coins. The reserve always stays in storage for building.", 13, Ui.Muted);
            info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            body.AddChild(info);
            foreach (var (id, def) in Defs.Items)
            {
                if (W.Storage.GetValueOrDefault(id) <= 0 && !W.AutoSell.Contains(id)) continue;
                var count = Ui.MakeLabel("", 14, Ui.Muted);
                var sell = new CheckButton { Text = "Auto-sell", ButtonPressed = W.AutoSell.Contains(id), FocusMode = FocusModeEnum.None };
                sell.Toggled += on => { if (on) W.AutoSell.Add(id); else W.AutoSell.Remove(id); Sfx.Play("click"); };
                var reserve = new SpinBox { MinValue = 0, MaxValue = 100000, Step = 10, Value = W.Reserved(id), Prefix = "keep", CustomMinimumSize = new Vector2(_mini ? 90 : 110, 0), TooltipText = "How many to keep in storage" };
                reserve.ValueChanged += v => W.Reserve[id] = (int)v;
                var name = Ui.Grow(Ui.MakeLabel($"{def.Name}  ({def.Price}c)", 14));
                refresh.Add(() => count.Text = Compact(W.Storage.GetValueOrDefault(id)));
                if (_mini)
                {
                    body.AddChild(Ui.Row(Ui.Icon(Sprites.Items[id]), name, count));
                    body.AddChild(Ui.Row(sell, reserve));
                }
                else body.AddChild(Ui.Row(Ui.Icon(Sprites.Items[id]), name, count, sell, reserve));
            }
            return;
        }

        body.AddChild(Ui.MakeLabel("Upgrades", 16));
        foreach (var u in Defs.Upgrades)
        {
            var buy = Ui.MakeButton("", () => { if (!W.BuyUpgrade(u.Id)) Sfx.Play("error"); }, Sfx);
            buy.CustomMinimumSize = new Vector2(_mini ? 96 : 120, 36);
            var level = Ui.MakeLabel("", 14, Ui.Muted);
            var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            info.AddChild(Ui.MakeLabel(u.Name, 15));
            var desc = Ui.MakeLabel(u.Description, 12, Ui.Muted);
            desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            info.AddChild(desc);
            refresh.Add(() =>
            {
                int lvl = W.Up(u.Id);
                level.Text = $"Lv {lvl}/{u.Max}";
                bool maxed = lvl >= u.Max;
                long cost = maxed ? 0 : Defs.UpgradeCost(u, lvl);
                buy.Text = maxed ? "Maxed" : $"Buy · {cost}c";
                buy.Disabled = maxed || W.Coins < cost;
            });
            body.AddChild(Ui.Row(info, level, buy));
        }
        body.AddChild(Ui.MakeLabel($"Raw materials ({World.RawBatch} at a time)", 16));
        foreach (var ore in Defs.Ores)
        {
            var buy = Ui.MakeButton($"Buy · {World.RawCost(ore)}c", () => { if (!W.BuyRaw(ore)) Sfx.Play("error"); }, Sfx);
            buy.CustomMinimumSize = new Vector2(_mini ? 96 : 120, 36);
            refresh.Add(() => buy.Disabled = W.Coins < World.RawCost(ore));
            body.AddChild(Ui.Row(Ui.Icon(Sprites.Items[ore], 20), Ui.Grow(Ui.MakeLabel(Defs.Items[ore].Name, 14)), buy));
        }
        body.AddChild(Ui.MakeLabel("Cosmetics arrive in a later update.", 12, Ui.Muted));
    }

    void OpenRecipePicker(Assembler a)
    {
        var body = OpenPanel($"{Defs.Buildings[a.Type].Name}: pick what it makes", 460);
        foreach (var r in Defs.Recipes.Where(r => r.Machine == a.Type))
        {
            bool open = W.IsUnlocked(r.Unlock);
            var ins = string.Join(" + ", r.In.Select(x => $"{x.Count} {Defs.Items[x.Item].Name}"));
            var b = new Button { Text = $"{(r.OutCount > 1 ? r.OutCount + " " : "")}{Defs.Items[r.Out].Name}   from {ins}", Icon = Sprites.Items[r.Out], ExpandIcon = true, Disabled = !open, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 40), ClipText = true };
            b.AddThemeConstantOverride("icon_max_width", 20);
            if (a.RecipeId == r.Id) b.AddThemeStyleboxOverride("normal", Ui.Box(Ui.BtnHover, Ui.Accent, 3));
            b.Pressed += () => { if (a.RecipeId != r.Id) W.SetRecipe(a, r.Id); Sfx.Play("click"); ClosePanel(); };
            Ui.Squishy(b);
            body.AddChild(b);
        }
        if (a.RecipeId != null)
            body.AddChild(Ui.MakeButton("Stop (clear recipe)", () => { W.SetRecipe(a, null); ClosePanel(); }, Sfx));
        var note = Ui.MakeLabel(a is Refinery
            ? "Fluid comes in from the pipe behind it and goes out to the pipe in front. Items go in and out with inserters. Switching recipe moves held items into storage."
            : "Switching recipe moves what it holds into storage.", 12, Ui.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(note);
    }

    void OpenFilterPicker(Inserter ins)
    {
        var body = OpenPanel("Inserter filter", 460);
        var info = Ui.MakeLabel("Only pick up this item. Without a filter it takes anything the target accepts.", 13, Ui.Muted);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(info);
        var none = Ui.MakeButton("No filter", () => { W.SetFilter(ins, null); ClosePanel(); }, Sfx);
        if (ins.Filter == null) none.AddThemeStyleboxOverride("normal", Ui.Box(Ui.BtnHover, Ui.Accent, 3));
        body.AddChild(none);
        var grid = new GridContainer { Columns = _mini ? 5 : 8 };
        body.AddChild(grid);
        foreach (var (id, def) in Defs.Items.Where(x => !x.Value.Fluid))
        {
            var b = new Button { Icon = Sprites.Items[id], ExpandIcon = true, CustomMinimumSize = new Vector2(40, 40), TooltipText = def.Name };
            b.AddThemeConstantOverride("icon_max_width", 22);
            if (ins.Filter == id) b.AddThemeStyleboxOverride("normal", Ui.Box(Ui.BtnHover, Ui.Accent, 3));
            b.Pressed += () => { W.SetFilter(ins, id); Sfx.Play("click"); ClosePanel(); };
            Ui.Squishy(b);
            grid.AddChild(b);
        }
    }

    void OpenChest(Chest c)
    {
        Label total = null;
        VBoxContainer list = null;
        int shownCount = -1;
        var body = OpenPanel("Chest", 380, () =>
        {
            if (c.Count == shownCount) return;
            shownCount = c.Count;
            total.Text = $"{c.Count}/{Chest.Cap} items";
            foreach (var n in list.GetChildren()) n.QueueFree();
            foreach (var (id, n) in c.Inv.OrderBy(x => x.Key)) list.AddChild(Ui.Row(Ui.Icon(Sprites.Items[id]), Ui.Grow(Ui.MakeLabel(Defs.Items[id].Name, 14)), Ui.MakeLabel(n.ToString(), 14)));
        });
        total = Ui.MakeLabel("", 14, Ui.Muted);
        list = new VBoxContainer();
        body.AddChild(total);
        body.AddChild(list);
        body.AddChild(Ui.MakeLabel("Inserters fill and empty chests.", 12, Ui.Muted));
    }

    void OpenPipe(Pipe p)
    {
        Label what = null;
        ProgressBar bar = null;
        var body = OpenPanel(Defs.Buildings[p.Type].Name, 420, () =>
        {
            var net = W.FluidOf(p);
            what.Text = net?.Fluid is { } f ? $"{Defs.Items[f].Name}: {net.Amount:0} / {net.Cap:0}" : $"Empty (holds {net?.Cap ?? 0:0})";
            bar.Value = net == null || net.Cap <= 0 ? 0 : net.Amount / net.Cap;
        });
        what = Ui.MakeLabel("", 16, Ui.Accent);
        bar = new ProgressBar { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 12) };
        var info = Ui.MakeLabel("Pipes that touch form one network holding a single fluid. Pumps push water into the pipe in front of them. Refineries take fluid from behind and put it out in front. Generators next to a sparkgas pipe burn it. Press V to see underground pipes.", 12, Ui.Muted);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        foreach (var c in new Control[] { what, bar, info }) body.AddChild(c);
    }
}
