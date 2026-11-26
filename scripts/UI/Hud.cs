using System;
using System.Collections.Generic;
using System.Globalization;
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
    readonly List<(Label Head, GridContainer Grid)> _itemGroups = new(); // storage rows per item group; more columns open when they would run past the hotbar
    PanelContainer _storagePanel, _tracker;
    HBoxContainer _bar, _coinRow;
    readonly PanelContainer _tierStrip = new() { Visible = false }; // the tiers of the building in hand, above its slot
    Label _coins, _coinRate, _toast, _hints, _trackerTitle;
    readonly List<Button> _speedBtns = new(); // pause, 1x, 2x, 4x, step
    readonly List<Control> _bigOnly = new();  // top-row buttons hidden in the mini window
    string _shownSpeed;
    VBoxContainer _trackerRows;
    Button _miniBtn;
    ColorRect _flash;
    Control _panel;           // the open modal panel, if any
    Action _panelTick;        // refreshes the open panel's numbers each frame
    float _toastT;
    double _scareT;
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
        _coinRate = Ui.MakeLabel("", 12, Ui.Muted);
        _coinRow = Ui.Row(Ui.Icon(Sprites.Coin), _coins, _coinRate);
        _coinRow.TooltipText = "Coins: turn on auto-sell in the Hub to earn them";
        _storage.AddChild(_coinRow);
        _potionRows = new VBoxContainer();
        _potionRows.AddThemeConstantOverride("separation", 2);
        _storage.AddChild(_potionRows);
        foreach (var g in Defs.Groups)
        {
            var head = Ui.MakeLabel(g, 11, Ui.Muted);
            var grid = new GridContainer { Columns = 1 };
            grid.AddThemeConstantOverride("h_separation", 14);
            grid.AddThemeConstantOverride("v_separation", 2);
            _storage.AddChild(head);
            _storage.AddChild(grid);
            _itemGroups.Add((head, grid));
        }
        foreach (var id in Defs.Items.Keys)
        {
            var count = Ui.MakeLabel("0");
            var row = Ui.Row(Ui.Icon(Sprites.Items[id]), count);
            row.Visible = false;
            row.TooltipText = HowToMake(id) + "\nClick for details";
            row.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { OpenItem(id); row.AcceptEvent(); } };
            _itemGroups[Defs.GroupIndex(id)].Grid.AddChild(row);
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
        _tierStrip.AddChild(new HBoxContainer());
        AddChild(_tierStrip);

        var top = new HBoxContainer();
        top.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        top.GrowHorizontal = GrowDirection.Begin;
        top.Position += new Vector2(-10, 10);
        AddChild(top);
        foreach (var (text, tip, act) in new (string, string, Action)[]
        {
            ("Pause", "Pause and resume (Space)", () => W.Paused = !W.Paused),
            ("1x", "Normal speed", () => { W.Speed = 1; W.Paused = false; }),
            ("2x", "Double speed", () => { W.Speed = 2; W.Paused = false; }),
            ("4x", "Four times as fast", () => { W.Speed = 4; W.Paused = false; }),
            ("Step", "One tick forward while paused (.)", () => { W.Paused = true; W.StepOnce = true; }),
        })
        {
            var b = Ui.MakeButton(text, act, Sfx);
            b.TooltipText = tip;
            b.CustomMinimumSize = new Vector2(text.Length > 2 ? 0 : 40, 0);
            _speedBtns.Add(b);
        }
        var gap = new Control { CustomMinimumSize = new Vector2(10, 0) };
        var build = Ui.MakeButton("Build", OpenBuildMenu, Sfx);
        build.TooltipText = "All buildings (Q)";
        var stats = Ui.MakeButton("Stats", OpenStats, Sfx);
        stats.TooltipText = "Production per item and coins per minute (P)";
        var items = Ui.MakeButton("Items", OpenItems, Sfx);
        items.TooltipText = "Every item: price, how to get it, what uses it (I)";
        var gamble = Ui.MakeButton("Gamble", OpenGamble, Sfx); // stays in the mini window
        gamble.TooltipText = "Bet items for other items";
        var bps = Ui.MakeButton("Blueprints", OpenBlueprints, Sfx);
        bps.TooltipText = "Saved copies, shared by every save (B). Ctrl+C copies an area";
        _miniBtn = Ui.MakeButton("Mini", () => ToggleMini?.Invoke(), Sfx);
        var menu = Ui.MakeButton("Menu", () => OpenMenu?.Invoke(), Sfx);
        menu.TooltipText = "Save, settings, main menu, quit (Esc)";
        _bigOnly.AddRange(_speedBtns);
        _bigOnly.AddRange(new Control[] { gap, stats, items, bps });
        foreach (var b in _speedBtns.Concat(new Control[] { gap, build, stats, items, gamble, bps, _miniBtn, menu })) { b.FocusMode = FocusModeEnum.None; top.AddChild(b); }

        _toast = Ui.MakeLabel("", 16, Ui.Accent);
        _toast.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        _toast.GrowHorizontal = GrowDirection.Both;
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        _toast.VerticalAlignment = VerticalAlignment.Bottom; // a toast of several lines grows upward, away from the hotbar
        _toast.GrowVertical = GrowDirection.Begin;
        _toast.Position -= new Vector2(0, 90);
        _toast.AddThemeColorOverride("font_outline_color", Ui.Ink);
        _toast.AddThemeConstantOverride("outline_size", 6);
        AddChild(_toast);

      _hints = Ui.MakeLabel("Click ore to mine  Click a building to open it  1-0 build (Tab: next tier)  Q all buildings  I items  H Hub  R rotate  Right-click remove  Ctrl+Z undo  Ctrl+C copy  V underground  Space pause  F7 mini  F8 hide", 13, Ui.Text);
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
        BuildHoverCard();
        BuildTutorial();
        BuildWelcome();
        Resized += FitHotbar;
        _seenMilestone = W.MilestoneIndex;
        RebuildHotbar();
        RebuildTracker();
    }

    public void Toast(string text) => Toast(text, Ui.Accent, 2.5f);

    public void Toast(string text, Color color, float seconds)
    {
        if (string.IsNullOrEmpty(text)) return;
        _toast.Text = text;
        _toast.AddThemeColorOverride("font_color", color);
        _toastT = seconds;
        if (Ui.Enabled) { _toast.PivotOffset = _toast.Size / 2; _toast.Scale = Vector2.One * 1.25f; CreateTween().TweenProperty(_toast, "scale", Vector2.One, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out); }
    }

    // Runs on every settings change too, so it only rebuilds when the mode really switched
    // (rebuilding closes the open panel, which would close the settings window under your mouse).
    public void SetMini(bool mini)
    {
        if (mini == _mini) return;
        _mini = mini;
        _miniBtn.Text = mini ? "Big" : "Mini";
        foreach (var c in _bigOnly) c.Visible = !mini;
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

    // Shrink the tracker to its content, then pin it: bottom right above the hotbar line normally (the top
    // centre belongs to the hover card), under the buttons in mini.
    void PlaceTracker()
    {
        _tracker.ResetSize();
        _tracker.SetAnchorsAndOffsetsPreset(_mini ? LayoutPreset.TopRight : LayoutPreset.BottomRight);
        _tracker.GrowHorizontal = GrowDirection.Begin;
        _tracker.GrowVertical = _mini ? GrowDirection.End : GrowDirection.Begin;
        _tracker.Position += _mini ? new Vector2(-4, 46) : new Vector2(-10, -122); // clear of the hotbar, key hints and toasts
    }

    // Which HUD pieces show is a setting (each "stat" can be turned off).
    public void ApplyStats()
    {
        _storagePanel.Visible = S.StatOn("storage") || S.StatOn("coins");
        _coinRow.Visible = S.StatOn("coins");
        _hints.Visible = !_mini && S.StatOn("hints");
        _tracker.Visible = S.StatOn("milestone") && !W.Free;
        ShowTutorial();
    }

    void RebuildHotbar()
    {
        foreach (var c in _bar.GetChildren()) { _bar.RemoveChild(c); c.QueueFree(); }
        var list = View.Hotbar();
        for (int i = 0; i < list.Count; i++)
        {
            var id = list[i];
            var def = Defs.Buildings[id];
            bool open = W.IsUnlocked(id);
            string keyName = i < 10 ? ((i + 1) % 10).ToString() : null;
            var b = new Button { Icon = Sprites.Icons[id], ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, FocusMode = FocusModeEnum.None };
            b.TooltipText = $"{def.Name}{(keyName != null ? $"  ({keyName})" : "")}\n" + (open ? $"Costs {Ui.Cost(def.Cost)}" : $"Unlocks with milestone {Defs.UnlockTier(id)}");
            if (!open) b.Modulate = new Color(1, 1, 1, 0.4f); // still clickable: it says when it unlocks
            b.Pressed += () => View.Select(id);
            var key = Ui.MakeLabel(keyName ?? "", _mini ? 9 : 11, Ui.Muted);
            key.Position = new Vector2(4, 1);
            key.MouseFilter = MouseFilterEnum.Ignore;
            b.AddChild(key);
            b.SetMeta("id", id);
            Ui.Squishy(b);
            _bar.AddChild(b);
        }
        _shownSel = "";
        FitHotbar();
    }

    // All slots in one row: they shrink when the window is too narrow (mini mode, and while it grows on hover).
    void FitHotbar()
    {
        int n = _bar.GetChildCount();
        if (n == 0) return;
        _bar.AddThemeConstantOverride("separation", _mini ? 2 : 4);
        float s = Mathf.Floor(Mathf.Min(_mini ? 30 : 48, (Size.X - 12) / n - (_mini ? 2 : 4)));
        foreach (var c in _bar.GetChildren())
            if (c is Button b)
            {
                b.CustomMinimumSize = new Vector2(s, s);
                b.AddThemeConstantOverride("icon_max_width", (int)Mathf.Min(s * 0.66f, s - 2 * SlotPad - 2));
                b.AddThemeStyleboxOverride("hover", Ui.Box(Ui.BtnHover, Ui.Accent with { A = 0.7f }, 2, 6, SlotPad));
                b.AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Accent, Ui.Accent, 2, 6, SlotPad));
            }
        _shownSel = ""; // re-applies the "normal" style with the small padding
        Callable.From(() =>
        {
            _bar.ResetSize();
            _bar.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
            _bar.GrowHorizontal = GrowDirection.Both;
            _bar.GrowVertical = GrowDirection.Begin;
            _bar.Position -= new Vector2(0, _mini ? 4 : 10);
        }).CallDeferred();
    }

    // Every tier of the family in hand, in a row above its hotbar slot: locked ones dimmed, the one in hand outlined.
    void ShowTierStrip()
    {
        var row = _tierStrip.GetChild<HBoxContainer>(0);
        foreach (var c in row.GetChildren()) { row.RemoveChild(c); c.QueueFree(); }
        var family = Defs.TiersOf(View.Selected);
        _tierStrip.Visible = family != null;
        if (family == null) return;
        row.AddThemeConstantOverride("separation", _mini ? 2 : 4);
        var slot = _bar.GetChildren().OfType<Button>().FirstOrDefault(b => b.HasMeta("id") && (string)b.GetMeta("id") == family[0]);
        float s = slot?.CustomMinimumSize.X ?? 40;
        foreach (var id in family)
        {
            var def = Defs.Buildings[id];
            bool open = W.IsUnlocked(id), sel = id == View.Selected;
            var b = new Button { Icon = Sprites.Icons[id], ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(s, s) };
            b.AddThemeConstantOverride("icon_max_width", (int)Mathf.Min(s * 0.66f, s - 2 * SlotPad - 2));
            b.AddThemeStyleboxOverride("normal", Ui.Box(sel ? Ui.BtnHover : Ui.Btn, sel ? Ui.Accent : Ui.Border, sel ? 3 : 2, 6, SlotPad));
            b.AddThemeStyleboxOverride("hover", Ui.Box(Ui.BtnHover, Ui.Accent with { A = 0.7f }, 2, 6, SlotPad));
            b.AddThemeStyleboxOverride("pressed", Ui.Box(Ui.Accent, Ui.Accent, 2, 6, SlotPad));
            b.TooltipText = $"{def.Name}  (Tab: next tier)\n{Defs.TierInfo(id)}\n" + (open ? $"Costs {Ui.Cost(def.Cost)}" : $"Unlocks with milestone {Defs.UnlockTier(id)}");
            if (!open) b.Modulate = new Color(1, 1, 1, 0.4f);
            b.Pressed += () => View.PickTier(id);
            Ui.Squishy(b);
            row.AddChild(b);
        }
        Callable.From(() =>
        {
            _tierStrip.ResetSize();
            float x = slot != null ? slot.GlobalPosition.X + slot.Size.X / 2 - _tierStrip.Size.X / 2 : (Size.X - _tierStrip.Size.X) / 2;
            float above = _hints.Visible ? Mathf.Min(_bar.Position.Y, _hints.GetGlobalRect().Position.Y) : _bar.Position.Y; // clear of the key hints
            _tierStrip.Position = new Vector2(Mathf.Clamp(x, 4, Mathf.Max(4, Size.X - _tierStrip.Size.X - 4)), above - _tierStrip.Size.Y - (_mini ? 2 : 6));
        }).CallDeferred();
    }

    const int SlotPad = 3; // hotbar slots use small padding so 15 of them fit in the mini window

    void RebuildTracker()
    {
        foreach (var c in _trackerRows.GetChildren()) { _trackerRows.RemoveChild(c); c.QueueFree(); } // gone now, so the size shrinks now
        Callable.From(PlaceTracker).CallDeferred();
        if (W.CurrentMilestone is not { } m)
        {
            _trackerTitle.Text = _mini ? "All milestones done!" : "All milestones done! Sky Beacons sell for the most.";
            return;
        }
        _trackerTitle.Text = $"Milestone {W.MilestoneIndex + 1}/{Defs.Milestones.Length}: {m.Name}";
        if (_mini) return; // mini shows the title with a percentage only
        foreach (var (item, n) in m.Need)
        {
            var bar = new ProgressBar { MaxValue = n, ShowPercentage = false, CustomMinimumSize = new Vector2(140, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore }; // the row shows the tooltip
            bar.SetMeta("item", item);
            var label = Ui.MakeLabel($"0/{n}", 13, Ui.Muted);
            label.CustomMinimumSize = new Vector2(52, 0); // same width on every row, so the bars line up
            var name = Ui.MakeLabel(Defs.Items[item].Name, 13);
            name.CustomMinimumSize = new Vector2(96, 0);
            var row = Ui.Row(Ui.Icon(Sprites.Items[item]), name, bar, label);
            row.TooltipText = HowToMake(item);
            _trackerRows.AddChild(row);
        }
        _trackerRows.AddChild(Ui.MakeLabel("Unlocks " + UnlockNames(m), 12, Ui.Muted));
    }

    // Tooltip for an item: its name and where it comes from.
    static string HowToMake(string item)
    {
        string N(string id) => Defs.Items[id].Name;
        var name = N(item);
        if (Array.IndexOf(Defs.Ores, item) >= 0) return $"{name}\nPut a miner on its ore patch, or click the ore to mine by hand.";
        if (item == "water") return $"{name}\nA pump pushes it into the pipe in front of it.";
        var r = Defs.Recipes.FirstOrDefault(x => x.Out == item);
        if (r == null) return name;
        var ins = string.Join(" + ", r.In.Select(x => $"{x.Count} {N(x.Item)}"));
        return $"{name}\n{Defs.Buildings[r.Machine].Name}: {ins} makes {r.OutCount} {name}" + (r.Unlock == null ? "" : r.Unlock.StartsWith("research_") ? "\nUnlocked by research" : $"\nUnlocks with milestone {Defs.UnlockTier(r.Unlock)}");
    }

    static string UnlockNames(Milestone m) =>
        string.Join(", ", m.Unlocks.Select(u => Defs.Buildings.TryGetValue(u, out var b) ? b.Name : Defs.Items.TryGetValue(u, out var i) ? i.Name : u == "steel" ? "Steel smelting" : u));

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
            if (row.GetChildCount() < 4 || row.GetChild(2) is not ProgressBar bar) continue;
            var item = (string)bar.GetMeta("item");
            long have = Math.Min(W.Delivered.GetValueOrDefault(item), (long)bar.MaxValue);
            bar.Value = Ui.Enabled ? Mathf.Lerp(bar.Value, have, 0.2) : have;
            ((Label)row.GetChild(3)).Text = $"{have}/{bar.MaxValue}";
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
        UpdateTutorial();
        UpdatePotionTimers();
        UpdateHoverCard();
        UpdateSpeed();
        for (_scareT += delta; _scareT >= 1; _scareT--) // one roll per real second
            if (S.ScareUnlocked && S.ScareOutOf > 0 && GD.Randi() % (uint)S.ScareOutOf < S.ScareHits) Jumpscare.Show(Sfx, S.ScareVolume, S.ScareMaxDevice);
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
        var perGroup = _itemGroups.Select(g => g.Grid.GetChildren().Count(c => ((Control)c).Visible)).ToArray();
        int fit = Math.Max(4, (int)((Size.Y - (_mini ? 90 : 230)) / (_mini ? 18 : 25))), cols = 1;
        while (cols < 3 && perGroup.Sum(n => n == 0 ? 0 : (n + cols - 1) / cols + 0.6) > fit) cols++; // a heading takes about 0.6 of a row
        bool changed = false;
        for (int i = 0; i < _itemGroups.Count; i++)
        {
            var (head, grid) = _itemGroups[i];
            changed |= grid.Columns != cols || head.Visible != perGroup[i] > 0;
            grid.Columns = cols;
            head.Visible = grid.Visible = perGroup[i] > 0;
        }
        if (changed) _storagePanel.ResetSize();
        double coins = Ui.Enabled ? Mathf.Lerp(_coinsShown, W.Coins, 1 - Mathf.Exp(-8 * dt)) : W.Coins;
        if (Math.Abs(coins - W.Coins) < 0.5) coins = W.Coins;
        _coinsShown = coins;
        _coins.Text = Compact((long)Math.Round(coins));
        float rate = W.PerMinute(World.CoinKey);
        _coinRate.Visible = S.StatOn("coinrate") && rate > 0;
        _coinRate.Text = $"+{Compact((long)Math.Round(rate))}/min";

        if (_shownSel != View.Selected)
        {
            _shownSel = View.Selected;
            foreach (var n in _bar.GetChildren())
                if (n is Button b && b.HasMeta("id"))
                {
                    var id = (string)b.GetMeta("id");
                    var family = Defs.TiersOf(id);
                    bool sel = id == View.Selected || family != null && family == Defs.TiersOf(View.Selected);
                    if (family != null) b.Icon = Sprites.Icons[sel ? View.Selected : id]; // a tiered slot shows the tier in hand
                    b.AddThemeStyleboxOverride("normal", Ui.Box(sel ? Ui.BtnHover : Ui.Btn, sel ? Ui.Accent : Ui.Border, sel ? 3 : 2, 6, SlotPad));
                }
            ShowTierStrip();
        }
        _toastT -= dt;
        _toast.Modulate = Colors.White with { A = Mathf.Clamp(_toastT, 0, 1) };
        _panelTick?.Invoke();
    }

    public static string Compact(long n) => n >= 1_000_000 ? (n / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + "M"
        : n >= 10_000 ? (n / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "k" : n.ToString();

    // ---------- Panels ----------

    // A dimmed overlay with a centred panel. Sized to fit the current window, scrolls when content is long.
    // header stays above the scrolling body, always visible.
    public VBoxContainer OpenPanel(string title, float width = 520, Action tick = null, Control header = null)
    {
        ClosePanel();
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.35f) };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // A click outside closes the window; the wheel does not (it scrolls past the end of a list up to here).
        dim.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right }) ClosePanel(); };
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
        if (header != null) v.AddChild(header);
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
            case Hub: OpenHub(SellTab); break;
            case Assembler a: OpenRecipePicker(a); break;
            case Inserter i: OpenFilterPicker(i); break;
            case Splitter sp: PickItem("Splitter filter", "This item goes out to the sides, everything else straight on. Without a filter items take turns on all three outputs.", sp.Filter, true, id => sp.Filter = id); break;
            case Chest c: OpenChest(c); break;
            case Lab: OpenLab(); break;
            case Pipe p: OpenPipe(p); break;
            case Pole or Generator or Solar or Accumulator: OpenPower(e); break;
            case ItemSource src: PickItem("Item source", "Makes this item out of nothing and hands it to the tile in front.", src.Item, false, id => src.Item = id); break;
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
                status.Text = $"Not connected. Put a power pole within 2 tiles of this {Defs.Buildings[clicked.Type].Name.ToLower()}.";
                use.Text = gens.Text = speed.Text = "";
                bar.Value = 0;
                return;
            }
            static string N(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
            status.Text = $"{N(net.Poles.Count, "pole")}, {N(net.Gens.Count, "generator")}, {N(net.Solars.Count, "solar array")}, {N(net.Users.Count, "machine")}"
                + (net.Accs.Count > 0 ? $", {N(net.Accs.Count, "accumulator")} holding {net.Accs.Sum(a => a.Stored):0} of {net.Accs.Count * Defs.AccumulatorKJ:0} kJ" : "");
            use.Text = $"Using {net.Demand:0} kW of {net.Capacity:0} kW";
            gens.Text = $"Coal in generators: {net.Gens.Sum(g => g.Fuel)}, fuel: {net.Gens.Sum(g => g.Cans)}" + (net.Gens.Count == 0 ? "  (no generator on this network yet)" : "");
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
        var info = Ui.MakeLabel("Power is a bonus: machines work without it, and run up to twice as fast with enough power. Generators burn coal; feed them with an inserter or a belt. Solar arrays give 100 kW each with no fuel, and are used first. Accumulators store free power nobody uses and give it back before generators burn fuel.", 12, Ui.Muted);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(info);
    }

    // Asks first: what the run costs, then Upgrade. Short on material at that point: a long toast, nothing changes.
    void OpenUpgrade(bool beltsOnly)
    {
        var plan = W.PlanUpgrade(beltsOnly);
        string noun = beltsOnly ? "belt" : "building";
        if (plan.Targets.Count == 0) { Toast($"Every {noun} is already on the best unlocked tier"); return; }
        string count = $"{plan.Targets.Count} {noun}{(plan.Targets.Count == 1 ? "" : "s")}";
        var refresh = new List<Action>();
        var body = OpenPanel(beltsOnly ? "Upgrade all belts" : "Upgrade everything", 440, () => { foreach (var r in refresh) r(); });
        var info = Ui.MakeLabel($"{count} will go to the best unlocked tier. {(beltsOnly ? "Items on them move" : "What's on or inside them moves")} to storage. Ctrl+Z undoes it.", 14);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(info);
        var need = plan.Net.Where(x => x.Value > 0).ToList();
        body.AddChild(Ui.MakeLabel(need.Count == 0 ? "Needs no materials" : "Needs", 16));
        foreach (var (item, n) in need)
        {
            var amount = Ui.MakeLabel("", 14);
            refresh.Add(() =>
            {
                long have = W.Storage.GetValueOrDefault(item);
                amount.Text = $"{n}  (have {have}{(have < n ? $", {n - have} short" : "")})";
                amount.AddThemeColorOverride("font_color", have < n ? Ui.Bad : Ui.Text);
            });
            body.AddChild(Ui.Row(Ui.Icon(Sprites.Items[item], 20), Ui.Grow(Ui.MakeLabel(Defs.Items[item].Name, 14)), amount));
        }
        var back = plan.Net.Where(x => x.Value < 0).Select(x => $"{-x.Value} {Defs.Items[x.Key].Name}").ToList();
        if (back.Count > 0)
        {
            var gets = Ui.MakeLabel("You get back: " + string.Join(", ", back), 13, Ui.Muted);
            gets.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            body.AddChild(gets);
        }
        foreach (var r in refresh) r();
        body.AddChild(Ui.Row(Ui.MakeButton("Cancel", ClosePanel, Sfx), Ui.MakeButton($"Upgrade {count}", () => { ClosePanel(); ConfirmUpgrade(beltsOnly, noun); }, Sfx)));
    }

    // Plans again: the factory kept running while the question was open.
    void ConfirmUpgrade(bool beltsOnly, string noun)
    {
        var plan = W.PlanUpgrade(beltsOnly);
        if (W.Upgrade(plan)) { Toast($"Upgraded {plan.Targets.Count} {noun}{(plan.Targets.Count == 1 ? "" : "s")}. Ctrl+Z undoes it", Ui.Good, 2.5f); return; }
        var lines = plan.Net.Where(x => x.Value > 0)
            .Select(x => $"{x.Value} {Defs.Items[x.Key].Name} (have {W.Storage.GetValueOrDefault(x.Key)})")
            .Chunk(_mini ? 1 : 2).Select(c => string.Join(",   ", c));
        Toast("Not enough materials to upgrade. You need:\n" + string.Join("\n", lines), Ui.Bad, 7);
        Sfx.Play("error");
    }

    public void OpenBuildMenu()
    {
        var upgradeBelts = Ui.MakeButton("Upgrade all belts", () => OpenUpgrade(true), Sfx);
        upgradeBelts.TooltipText = "Every belt on the map to the best unlocked belt. Asks first";
        var upgradeAll = Ui.MakeButton("Upgrade everything", () => OpenUpgrade(false), Sfx);
        upgradeAll.TooltipText = "Every building with tiers, belts included, to its best unlocked tier. Asks first";
        var body = OpenPanel("All Buildings", 660, header: Ui.Row(upgradeBelts, upgradeAll));
        int cols = _mini ? 2 : 3; // three columns: room for "Hyper Underground Belt" and its icon
        var ids = Defs.BuildOrder.Concat(W.Free ? Defs.SandboxOnly : Array.Empty<string>()).ToList();
        Control Make(string id)
        {
            var def = Defs.Buildings[id];
            bool open = W.IsUnlocked(id);
            var b = new Button { Text = def.Name, Icon = Sprites.Icons[id], ExpandIcon = true, Disabled = !open, CustomMinimumSize = new Vector2(_mini ? 150 : 206, 44), Alignment = HorizontalAlignment.Left };
            b.AddThemeConstantOverride("icon_max_width", 24);
            if (_mini) // two columns share the width and long names wrap, or the grid pushes the panel off screen
            {
                b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            }
            b.TooltipText = open ? $"Costs {Ui.Cost(def.Cost)}" : $"Unlocks with milestone {Defs.UnlockTier(id)}";
            b.Pressed += () => { W.Used("builds", id); View.StopClip(); View.Selected = id; if (Defs.TiersOf(id) != null) S.LastTier[Defs.BaseOf(id)] = id; Sfx.Play("click"); ClosePanel(); };
            Ui.Squishy(b);
            return b;
        }
        var add = SearchBox(body, "builds", ids, Make, cols);
        var grid = new GridContainer { Columns = cols };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        body.AddChild(grid);
        foreach (var id in ids)
        {
            var b = Make(id);
            grid.AddChild(b);
            add(b, Defs.Buildings[id].Name, null);
        }
        var note = Ui.MakeLabel("Locked buildings unlock with milestones. Hover one to see which.", 13, Ui.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(note);
    }

    public const int SellTab = 0, ShopTab = 1, PotionTab = 2, SlotsTab = 3;

    public void OpenHub(int tab)
    {
        var refresh = new List<Action>();
        var coins = Ui.MakeLabel("", 16, new Color("f7c948"));
        var tabs = new[] { Ui.MakeButton("Sell", () => OpenHub(SellTab), Sfx), Ui.MakeButton("Shop", () => OpenHub(ShopTab), Sfx), Ui.MakeButton("Potions", () => OpenHub(PotionTab), Sfx), Ui.MakeButton("Slots", () => OpenHub(SlotsTab), Sfx) };
        tabs[tab].AddThemeStyleboxOverride("normal", Ui.Box(Ui.BtnHover, Ui.Accent, 3));
        var body = OpenPanel("Hub", 620, () => { coins.Text = _mini ? Compact(W.Coins) : $"{W.Coins} coins"; /* four tabs leave little room in mini */ foreach (var r in refresh) r(); }, Ui.Row(tabs[0], tabs[1], tabs[2], tabs[3], Ui.Icon(Sprites.Coin), coins));

        if (tab == PotionTab) { FillPotions(body, refresh); return; }
        if (tab == SlotsTab) { FillSlots(body, refresh); return; }
        if (tab == SellTab)
        {
            var info = Ui.MakeLabel("Auto-sell turns items delivered here into coins. The reserve always stays in storage for building.", 13, Ui.Muted);
            info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            body.AddChild(info);
            string group = null;
            foreach (var (id, def) in Defs.Items.OrderBy(x => Defs.GroupIndex(x.Key)))
            {
                if (W.Storage.GetValueOrDefault(id) <= 0 && !W.AutoSell.Contains(id)) continue;
                GroupHead(body, ref group, id);
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
                level.Text = u.Max == int.MaxValue ? $"Lv {lvl}" : $"Lv {lvl}/{u.Max}";
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
        FillCosmetics(body, refresh);
    }

    void OpenRecipePicker(Assembler a)
    {
        var body = OpenPanel($"{Defs.Buildings[a.Type].Name}: pick what it makes", 640);
        var recipes = Defs.Recipes.Where(r => r.Machine == Defs.BaseOf(a.Type)).OrderBy(r => Defs.GroupIndex(r.Out)).ToList();
        Control Make(string id)
        {
            var r = recipes.First(x => x.Id == id);
            bool open = W.IsUnlocked(r.Unlock);
            var ins = string.Join(" + ", r.In.Select(x => $"{x.Count} {Defs.Items[x.Item].Name}"));
            var b = new Button { Text = $"{(r.OutCount > 1 ? r.OutCount + " " : "")}{Defs.Items[r.Out].Name}   from {ins}", Icon = Sprites.Items[r.Out], ExpandIcon = true, Disabled = !open, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 40), ClipText = true };
            b.TooltipText = b.Text.Trim(); // the button cuts long recipes in a small window
            b.AddThemeConstantOverride("icon_max_width", 20);
            if (a.RecipeId == r.Id) b.AddThemeStyleboxOverride("normal", Ui.Box(Ui.BtnHover, Ui.Accent, 3));
            b.Pressed += () => { W.Used("recipes", r.Id); if (a.RecipeId != r.Id) W.SetRecipe(a, r.Id); Sfx.Play("click"); ClosePanel(); };
            Ui.Squishy(b);
            return b;
        }
        var add = SearchBox(body, "recipes", recipes.Select(r => r.Id).ToList(), Make);
        string group = null;
        Label head = null;
        foreach (var r in recipes)
        {
            head = GroupHead(body, ref group, r.Out) ?? head;
            var b = Make(r.Id);
            body.AddChild(b);
            add(b, Defs.Items[r.Out].Name, head);
        }
        if (a.RecipeId != null)
            body.AddChild(Ui.MakeButton("Stop (clear recipe)", () => { W.SetRecipe(a, null); ClosePanel(); }, Sfx));
        var note = Ui.MakeLabel(a is Refinery
            ? "Fluid comes in from the pipe behind it and goes out to the pipe in front. Items go in and out with inserters. Switching recipe moves held items into storage."
            : "Switching recipe moves what it holds into storage.", 12, Ui.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(note);
    }

    // A heading before the first item of each group in a list.
    // Returns the heading, or null when id is still in the same group.
    static Label GroupHead(VBoxContainer body, ref string last, string id)
    {
        if (Defs.GroupOf(id) == last) return null;
        last = Defs.GroupOf(id);
        var head = Ui.MakeLabel(last, 13, Ui.Muted);
        body.AddChild(head);
        return head;
    }

    void OpenFilterPicker(Inserter ins) =>
        PickItem("Inserter filter", "Only pick up this item. Without a filter it takes anything the target accepts.", ins.Filter, true, id => W.SetFilter(ins, id));

    // A grid of every item; allowNone adds a "No filter" button that sets null.
    void PickItem(string title, string infoText, string current, bool allowNone, Action<string> set)
    {
        var body = OpenPanel(title, 460);
        var info = Ui.MakeLabel(infoText, 13, Ui.Muted);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(info);
        if (allowNone)
        {
            var none = Ui.MakeButton("No filter", () => { set(null); ClosePanel(); }, Sfx);
            if (current == null) none.AddThemeStyleboxOverride("normal", Ui.Box(Ui.BtnHover, Ui.Accent, 3));
            body.AddChild(none);
        }
        var grid = new GridContainer { Columns = _mini ? 5 : 8 };
        body.AddChild(grid);
        foreach (var (id, def) in Defs.Items.Where(x => !x.Value.Fluid))
        {
            var b = new Button { Icon = Sprites.Items[id], ExpandIcon = true, CustomMinimumSize = new Vector2(40, 40), TooltipText = def.Name };
            b.AddThemeConstantOverride("icon_max_width", 22);
            if (current == id) b.AddThemeStyleboxOverride("normal", Ui.Box(Ui.BtnHover, Ui.Accent, 3));
            b.Pressed += () => { set(id); Sfx.Play("click"); ClosePanel(); };
            Ui.Squishy(b);
            grid.AddChild(b);
        }
    }

    // Research is paid from storage. Each finished one unlocks a recipe variant in the assembler or refinery.
    void OpenLab()
    {
        Label status = null;
        var buttons = new List<(Research R, Button B)>();
        var body = OpenPanel("Research Lab", 460, () =>
        {
            var cur = W.Researching == null ? null : Array.Find(Defs.Researches, x => x.Id == W.Researching);
            status.Text = cur == null ? "Idle. Pick a research." : $"Researching {cur.Name}: {Math.Min(100, 100 * W.ResearchProgress / cur.Ticks)}%";
            foreach (var (r, b) in buttons)
            {
                bool done = W.Researched.Contains(r.Id), afford = r.Cost.All(c => W.Storage.GetValueOrDefault(c.Item) >= c.Count);
                b.Text = done ? "Done" : W.Researching == r.Id ? "Working" : "Start";
                b.Disabled = done || W.Researching != null || !afford;
            }
        });
        status = Ui.MakeLabel("", 14, Ui.Muted);
        body.AddChild(status);
        foreach (var r in Defs.Researches)
        {
            var b = Ui.MakeButton("Start", () => { if (!W.StartResearch(r.Id)) Sfx.Play("error"); }, Sfx);
            b.CustomMinimumSize = new Vector2(90, 36);
            buttons.Add((r, b));
            var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            info.AddChild(Ui.MakeLabel(r.Name, 15));
            var desc = Ui.MakeLabel($"{r.Description}\nCosts {string.Join(", ", r.Cost.Select(c => $"{c.Count} {Defs.Items[c.Item].Name}"))}, takes {r.Ticks / 20}s with one lab", 12, Ui.Muted);
            desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            info.AddChild(desc);
            body.AddChild(Ui.Row(info, b));
        }
        body.AddChild(Ui.MakeLabel("More labs share the work and finish sooner.", 12, Ui.Muted));
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
        var limits = new HBoxContainer();
        foreach (int n in new[] { 0, 10, 50, 100 })
        {
            var b = Ui.MakeButton(n == 0 ? "No limit" : $"{n} each", () => { c.Limit = n; ClosePanel(); OpenChest(c); }, Sfx);
            if (c.Limit == n) b.AddThemeStyleboxOverride("normal", Ui.Box(Ui.BtnHover, Ui.Accent, 3));
            limits.AddChild(b);
        }
        body.AddChild(Ui.MakeLabel("Limit per item", 13, Ui.Muted));
        body.AddChild(limits);
        body.AddChild(Ui.MakeLabel("Inserters fill and empty chests. With a limit it takes no more of an item once it holds that many.", 12, Ui.Muted));
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
        var info = Ui.MakeLabel("Pipes that touch form one network holding a single fluid. Pumps push water into the pipe in front of them. Refineries take fluid from behind and put it out in front. Generators next to a sparkgas or steam pipe burn it. Press V to see underground pipes.", 12, Ui.Muted);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        foreach (var c in new Control[] { what, bar, info }) body.AddChild(c);
    }
}
