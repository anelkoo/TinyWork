using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using TinyWorks.Sim;
using TinyWorks.UI;

namespace TinyWorks;

// Dev check: "-- --data <dir> --uitest <outdir>" loads the last save in <dir>, opens every window,
// sends key events through the engine and saves screenshots of the game's own render.
// Needs no mouse and no focus, so it can run while someone uses the PC.
public partial class Main
{
    int _uiFails;

    static bool InScroll(Node n) { for (var p = n.GetParent(); p != null; p = p.GetParent()) if (p is ScrollContainer) return true; return false; }

    static System.Collections.Generic.IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        foreach (var c in root.GetChildren())
        {
            if (c is T t) yield return t;
            foreach (var d in FindAll<T>(c)) yield return d;
        }
    }

    async void RunUiTest(string outDir)
    {
        Directory.CreateDirectory(outDir);
        async Task Wait(double s) => await ToSignal(GetTree().CreateTimer(s, true, false, true), SceneTreeTimer.SignalName.Timeout);
        void Shot(string name) => GetViewport().GetTexture().GetImage().SavePng(Path.Combine(outDir, name + ".png"));
        void Check(bool ok, string what) { if (!ok) _uiFails++; GD.Print($"UITEST {(ok ? "ok  " : "FAIL")} {what}"); }
        void Key(Key k) { Input.ParseInputEvent(new InputEventKey { Keycode = k, Pressed = true }); Input.ParseInputEvent(new InputEventKey { Keycode = k, Pressed = false }); }
        T Find<T>() where T : Entity => _world.Entities.OfType<T>().First();
        void CtrlKey(Key k, bool shift = false) { Input.ParseInputEvent(new InputEventKey { Keycode = k, CtrlPressed = true, ShiftPressed = shift, Pressed = true }); Input.ParseInputEvent(new InputEventKey { Keycode = k, CtrlPressed = true, Pressed = false }); }

        _s.ScareChance = 0; // no surprise jumpscare in the middle of the screenshots
        LoadGame(_s.LastSlot);
        await Wait(0.35);
        Shot("01_celebration");
        Check(FindAll<Label>(_hud).Any(l => l.IsVisibleInTree() && l.Text.StartsWith("Welcome back to")), "the welcome-back card shows last session's stats");
        await Wait(1.5);
        Shot("01b_welcome");
        FindAll<Button>(_hud).First(b => b.Text == "Let's go").EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(0.1);
        Check(!FindAll<Label>(_hud).Any(l => l.IsVisibleInTree() && l.Text.StartsWith("Welcome back to")), "Let's go puts the card away");
        Check(FindAll<AudioStreamPlayer>(_sfx).Any(p => p.Playing && p.Stream is AudioStreamWav { LoopMode: AudioStreamWav.LoopModeEnum.Forward }), "the music loop plays");
        await Wait(2.5);
        Shot("02_game");
        Check(_world.MilestoneIndex == 1, "milestone 1 reached from the prepared save");
        int expected = Defs.Buildings.Keys.Count(k => k != "hub" && System.Array.IndexOf(Defs.SandboxOnly, k) < 0 && (Defs.TiersOf(k)?[0] ?? k) == k); // a tier family shares one slot; no Hub, no sandbox tools
        Check(_view.Hotbar().Count == expected, $"every building is on the hotbar ({_view.Hotbar().Count} of {expected} slots)");
        Key(Godot.Key.Key0);
        await Wait(0.1);
        Check(_view.Selected == "pole", $"0 picks the tenth slot (got {_view.Selected})");
        _view.Selected = null;
        _view.Select("pipe");
        Check(_view.Selected == null, "a locked building can't be picked from the hotbar");
        Shot("02a_hotbar");
        Check(Engine.TimeScale == 1, "slow motion ended");
        var gearRow = FindAll<HBoxContainer>(_hud).FirstOrDefault(r => FindAll<ProgressBar>(r).Any() && FindAll<Label>(r).Any(l => l.Text == "Gear"));
        Check(gearRow != null && gearRow.TooltipText.Contains("Assembler: 2 Iron Plate"),
            $"milestone row names the item and says how to make it ({gearRow?.TooltipText})");

        _hud.Inspect(Find<Hub>());
        await Wait(0.5);
        Shot("03_hub_sell");
        Key(Godot.Key.Key1);
        await Wait(0.1);
        Check(_view.Selected == null && _hud.HasPanel, "typing 1 with a window open picks no building");
        Key(Godot.Key.Escape);
        await Wait(0.25);
        Check(!_hud.HasPanel, "Esc closes the window");

        // Tutorial: restart it on this save, then mine by hand; the factory here ticks off later steps by itself.
        _world.Tutorial = 0;
        bool CardShows(string text) => FindAll<Label>(_hud).Any(l => l.IsVisibleInTree() && l.Text.Contains(text));
        await Wait(0.3);
        Check(CardShows("Getting started  1/7"), "tutorial card shows step 1");
        Shot("02b_tutorial");
        var ore = Enumerable.Range(-30, 60).SelectMany(x => Enumerable.Range(-30, 60).Select(y => new Vector2I(x, y))).First(t => _world.OreAt(t) != null && _world.At(t) == null);
        _world.HandMine(ore);
        await Wait(4);
        Check(_world.Tutorial == 1 && CardShows("Build a miner"), $"mining by hand ticks step 1; this save has no miner, so it waits there (step {_world.Tutorial})");
        Shot("02c_tutorial_progress");
        FindAll<Button>(_hud).First(b => b.Text == "Hide").EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(0.2);
        Check(_world.Tutorial == -1 && !CardShows("Getting started"), "Hide puts the tutorial away");

        _world.AutoSell.Add("iron_plate");
        _world.Reserve["iron_plate"] = 400;
        long coins = _world.Coins;
        await Wait(1.5);
        // Sells once a second; plates still arriving in between may sit just above the reserve.
        Check(_world.Coins > coins && _world.Storage["iron_plate"] is >= 400 and <= 410, $"auto-sell paid out ({_world.Coins - coins} coins) and kept the reserve ({_world.Storage["iron_plate"]})");
        Shot("04_selling");

        _hud.OpenHub(Hud.ShopTab);
        await Wait(0.5);
        Shot("05_hub_shop");
        Check(_world.BuyUpgrade("machine_speed"), "bought an upgrade");
        _hud.OpenHub(Hud.PotionTab);
        await Wait(0.5);
        Shot("05b_potions");
        Check(FindAll<HSlider>(_hud).Count(sl => sl.IsVisibleInTree()) == 2, "brewing has two sliders: strength and length");
        var sellTab = FindAll<Button>(_hud).First(b => b.Text == "Sell");
        Check(!InScroll(sellTab), "the Hub tabs sit above the scrolling list");
        var rushButton = FindAll<Button>(_hud).First(b => b.Text.StartsWith("2 min"));
        long beforeCoins = _world.Coins, earnedBefore = _world.CoinsEarned, rushCost = _world.PotionCost("coin_rush", 2f, 2);
        rushButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(0.3);
        Check(_world.Has("coin_rush") && _world.Coins == beforeCoins - rushCost + (_world.CoinsEarned - earnedBefore), $"bought a 2 min Coin Rush for {rushCost} coins");
        Check(FindAll<Button>(_hud).Any(b => b.Text.StartsWith("Brew") && !b.Disabled), "custom potion brew button is ready");
        _world.Coins += 10000;
        var strengthPick = FindAll<OptionButton>(_hud).First(o => o.ItemCount == 3 && o.GetItemText(2) == "x3");
        strengthPick.Selected = 2;
        await Wait(0.2);
        FindAll<Button>(_hud).First(b => b.Text.StartsWith("2 min")).EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(0.2);
        Check(_world.Strength("coin_rush") == 3f && _world.PotionMinutesLeft("coin_rush", 2f) == 2, "picking x3 runs it first; the x2 waits");
        Shot("05b2_potion_strengths");
        _hud.ClosePanel();
        await Wait(0.3);
        Check(FindAll<Label>(_hud).Any(l => l.IsVisibleInTree() && l.Text.StartsWith("x3 1:5") && l.Text.EndsWith("+1 waiting")), "the potion timer counts down under the coins");
        Shot("05c_potion_active");
        _hud.ClosePanel();

        Key(Godot.Key.Q);
        await Wait(0.5);
        Check(_hud.HasPanel, "Q opens the build menu");
        Shot("06_build_menu");
        Key(Godot.Key.Escape);
        await Wait(0.1);
        Key(Godot.Key.Key1);
        await Wait(0.1);
        Check(_view.Selected == "belt", "1 picks the belt with no window open");
        Key(Godot.Key.Escape);
        await Wait(0.1);
        Check(_view.Selected == null, "Esc puts the building away");
        Key(Godot.Key.Key2);
        await Wait(0.1);
        Check(_view.Selected == "inserter", "2 is still the inserter after the unlock");
        Key(Godot.Key.Escape);
        await Wait(0.1);

        // Rebinding: Esc closing the whole panel mid-rebind must un-pause the hotkeys.
        Button HideKeyButton() => FindAll<Button>(_hud).FirstOrDefault(b => b.ToggleMode && b.Text == _s.HideKey);
        OpenSettings((t, w) => _hud.OpenPanel(t, w));
        await Wait(0.3);
        var kb = HideKeyButton();
        kb.ButtonPressed = true;
        Check(_keys.Paused, "hotkeys pause while listening for a new key");
        Key(Godot.Key.Escape);
        await Wait(0.2);
        Check(!_hud.HasPanel && !_keys.Paused, "closing settings mid-rebind un-pauses the hotkeys");
        OpenSettings((t, w) => _hud.OpenPanel(t, w));
        await Wait(0.3);
        kb = HideKeyButton();
        kb.GrabFocus();
        kb.ButtonPressed = true;
        Input.ParseInputEvent(new InputEventKey { Keycode = Godot.Key.Home, Pressed = true });
        await Wait(0.1);
        Check(_s.HideKey == "F8" && _keys.Paused, "an unwatchable key (Home) is refused");
        Input.ParseInputEvent(new InputEventKey { Keycode = Godot.Key.F9, CtrlPressed = true, Pressed = true });
        await Wait(0.1);
        Check(_s.HideKey == "Ctrl+F9" && !_keys.Paused, $"rebound to Ctrl+F9 (got {_s.HideKey})");
        _s.HideKey = "F8";
        ApplySettings();
        _hud.ClosePanel();

        var asm = Find<Assembler>();
        _hud.Inspect(asm);
        await Wait(0.5);
        Shot("07_recipe");
        _world.SetRecipe(asm, "gear");
        _hud.ClosePanel();
        _hud.Inspect(Find<Inserter>());
        await Wait(0.5);
        Shot("08_filter");
        _hud.ClosePanel();

        OpenSettings((t, w) => _hud.OpenPanel(t, w));
        await Wait(0.5);
        Shot("09_settings");
        FindAll<HSlider>(_hud).First().Value = 0.6;
        await Wait(0.2);
        Check(_hud.HasPanel && _s.MiniOpacity == 0.6f, "changing a slider keeps the settings window open");
        _hud.ClosePanel();

        var pole = Find<Pole>();
        var net = _world.NetOf(pole);
        Check(net != null && net.Gens.Count == 1 && net.Users.Count >= 3 && net.Poles.Count == 2, $"pole network: {net?.Poles.Count} poles, {net?.Gens.Count} gens, {net?.Users.Count} machines");
        Check(net != null && net.Satisfaction > 0 && _world.PowerBoost(Find<Inserter>()) > 1, "powered inserter gets the speed bonus");
        _hud.Inspect(pole);
        await Wait(0.6);
        Shot("09b_power");
        _hud.ClosePanel();
        _view.Selected = "pole";
        await Wait(0.3);
        Shot("09c_pole_ghost");
        _view.Selected = null;

        _world.MilestoneIndex = 2; // fast belts unlocked, express still locked
        _s.LastTier.Clear();
        Key(Godot.Key.Key1);
        await Wait(0.1);
        System.Collections.Generic.List<Button> TierButtons() => FindAll<Button>(_hud).Where(b => b.IsVisibleInTree() && b.TooltipText.Contains("Tab: next tier")).ToList();
        Check(_view.Selected == "belt" && TierButtons().Count == Defs.BeltTiers.Length, $"1 picks the belt and shows all its tiers above the slot (got {_view.Selected}, {TierButtons().Count})");
        Shot("09d_tier_strip");
        TierButtons()[1].EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(0.1);
        Check(_view.Selected == "fast_belt", $"clicking the second tier picks the fast belt (got {_view.Selected})");
        TierButtons()[2].EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(0.1);
        Check(_view.Selected == "fast_belt", $"a locked tier can't be picked (got {_view.Selected})");
        Key(Godot.Key.Key1);
        await Wait(0.1);
        Check(_view.Selected == null && TierButtons().Count == 0, "pressing 1 again puts the belt and its tiers away");
        Key(Godot.Key.Key1);
        await Wait(0.1);
        Check(_view.Selected == "fast_belt", $"1 starts with the belt tier used last (got {_view.Selected})");
        Key(Godot.Key.Tab);
        await Wait(0.1);
        Check(_view.Selected == "belt", $"Tab skips the locked express belt and wraps to the belt (got {_view.Selected})");
        Key(Godot.Key.Escape);
        await Wait(0.1);
        Key(Godot.Key.Key0);
        await Wait(0.1);
        Key(Godot.Key.Tab);
        await Wait(0.1);
        Check(_view.Selected == "big_pole", $"0 then Tab steps from the power pole to the big pole (got {_view.Selected})");
        Shot("09e_big_pole_ghost");
        Key(Godot.Key.Escape);
        await Wait(0.1);

        // The mouse wheel over an open window scrolls it and never zooms the map; a fast spin zooms one step.
        void Wheel(MouseButton b)
        {
            var at = GetViewport().GetVisibleRect().Size / 2;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = true, Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = false, Position = at, GlobalPosition = at });
        }
        float zoom = _view.ZoomTarget;
        OpenSettings((t, w) => _hud.OpenPanel(t, w)); // long enough to scroll
        await Wait(0.3);
        var scroll = FindAll<ScrollContainer>(_hud).First();
        for (int i = 0; i < 40; i++) { Wheel(MouseButton.WheelDown); await Wait(0.03); } // past the bottom
        for (int i = 0; i < 60; i++) { Wheel(MouseButton.WheelUp); await Wait(0.03); }   // and back past the top
        await Wait(0.3);
        Check(_view.ZoomTarget == zoom, $"scrolling past the end of a window doesn't zoom the map ({zoom} -> {_view.ZoomTarget}, scrolled {scroll.ScrollVertical})");
        _hud.ClosePanel();
        await Wait(0.2);
        Wheel(MouseButton.WheelUp);
        Wheel(MouseButton.WheelUp);
        await Wait(0.3);
        Check(_view.ZoomTarget == zoom + 0.5f, $"two quick wheel clicks zoom one half step ({zoom} -> {_view.ZoomTarget})");
        _view.SetCamera(_view.CamTarget, zoom);

        // Save Now: a green message once the file is really on disk.
        ToggleMenu();
        await Wait(0.3);
        FindAll<Button>(_uiRoot).First(b => b.Text == "Save Now").EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(0.3);
        Check(FindAll<Label>(_hud).Any(l => l.Text.StartsWith("Saved \"") && l.Modulate.A > 0.5f), "Save Now says it saved");
        Shot("09d_saved");

        // Step 6: game speed, stats, undo, copy and paste, blueprints, cosmetics.
        Key(Godot.Key.Space);
        await Wait(0.2);
        long pausedAt = _world.Tick;
        float beltsAt = _view.BeltTime;
        await Wait(0.4);
        Check(_world.Paused && _world.Tick == pausedAt, $"Space pauses the game (tick {pausedAt} -> {_world.Tick})");
        Check(_view.BeltTime == beltsAt, "belts stop moving while paused");
        Key(Godot.Key.Period);
        await Wait(0.2);
        Check(_world.Tick == pausedAt + 1, $". steps exactly one tick ({pausedAt} -> {_world.Tick})");
        Shot("19_paused");
        FindAll<Button>(_hud).First(b => b.Text == "4x").EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(0.5);
        Check(!_world.Paused && _world.Speed == 4 && Engine.PhysicsTicksPerSecond == 80, $"4x runs 80 ticks a second ({Engine.PhysicsTicksPerSecond})");
        FindAll<Button>(_hud).First(b => b.Text == "1x").EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(0.2);
        Key(Godot.Key.P);
        await Wait(0.5);
        Check(_hud.HasPanel && FindAll<Label>(_hud).Any(l => l.Text == "Coins earned"), "P opens the stats window");
        Shot("20_stats");
        Key(Godot.Key.Escape);
        await Wait(0.2);
        Key(Godot.Key.I);
        await Wait(0.4);
        var plateName = Defs.Items["iron_plate"].Name;
        Check(_hud.HasPanel && FindAll<Button>(_hud).Any(b => b.Text == plateName), "I opens the item book");
        Shot("20b_items");
        FindAll<Button>(_hud).First(b => b.Text == plateName).EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(0.4);
        Check(FindAll<Label>(_hud).Any(l => l.Text.StartsWith("Sells for")) && FindAll<Label>(_hud).Any(l => l.Text == "Used in") && FindAll<Label>(_hud).Any(l => l.Text.StartsWith("Furnace")),
            "clicking an item shows its price, how to get it and what uses it");
        Shot("20c_item_detail");
        Key(Godot.Key.Escape);
        await Wait(0.2);
        var scareDir = Path.Combine(outDir, "scare"); // three plain frames, so the check needs no real pictures
        Directory.CreateDirectory(scareDir);
        foreach (var n in new[] { 1, 2, 10 })
        {
            var img = Image.CreateEmpty(64, 36, false, Image.Format.Rgba8);
            img.Fill(Colors.Red with { A = 0.5f });
            img.SavePng(Path.Combine(scareDir, $"frame-{n}.png"));
        }
        Check(Jumpscare.FrameFiles(scareDir).Select(Path.GetFileName).SequenceEqual(new[] { "frame-1.png", "frame-2.png", "frame-10.png" }), "jumpscare frames play in number order");
        Jumpscare.Show(_sfx, null);
        Check(!FindAll<Jumpscare>(GetTree().Root).Any(), "no folder, no jumpscare");
        Jumpscare.Show(_sfx, scareDir);
        await Wait(0.05);
        Check(FindAll<Jumpscare>(GetTree().Root).Any(), "a jumpscare opens its own window");
        await Wait(0.3);
        Check(!FindAll<Jumpscare>(GetTree().Root).Any(), "the jumpscare window closes after its last frame");
        var spot = new Vector2I(-12, -12);
        foreach (var (item, n) in Defs.Buildings["chest"].Cost) _world.AddStorage(item, n * 2); // enough for the chest and its redo
        _world.BeginAction(); _world.Place("chest", spot, 0); _world.EndAction();
        CtrlKey(Godot.Key.Z);
        await Wait(0.1);
        Check(_world.At(spot) == null, "Ctrl+Z takes the chest back");
        CtrlKey(Godot.Key.Z, shift: true);
        await Wait(0.1);
        Check(_world.At(spot) is Chest, "Ctrl+Shift+Z puts it back");
        CtrlKey(Godot.Key.V);
        await Wait(0.1);
        Check(!_view.Underground, "Ctrl+V never toggles the underground view");
        _view.StartPaste(_world.Copy(new Rect2I(2, -1, 3, 2)));
        var mouse = _view.GetCanvasTransform() * (new Vector2(-10.5f, 4.5f) * 16);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = mouse, GlobalPosition = mouse });
        await Wait(0.3);
        Check(_view.Pasting && _view.Clip.Count > 0, $"a copy follows the mouse ({_view.Clip?.Count} parts)");
        Shot("21_paste_ghost");
        Key(Godot.Key.B);
        await Wait(0.4);
        Check(_hud.HasPanel && FindAll<LineEdit>(_hud).Any(), "B opens blueprints with a name field for the current copy");
        Shot("22_blueprints");
        var camBefore = _view.CamTarget;
        FindAll<LineEdit>(_hud).First().GrabFocus();
        Input.ParseInputEvent(new InputEventKey { Keycode = Godot.Key.Up, Pressed = true });
        await Wait(0.3);
        Input.ParseInputEvent(new InputEventKey { Keycode = Godot.Key.Up, Pressed = false });
        Check(_view.CamTarget == camBefore, "arrow keys in a text field don't move the camera");
        Key(Godot.Key.Escape);
        Key(Godot.Key.Escape);
        await Wait(0.2);
        Check(!_view.Pasting, "Esc puts the copy away");
        _world.Coins += 5000;
        Check(_world.BuyCosmetic("hat_party") && _world.BuyCosmetic("belt_mint") && _world.BuyCosmetic("fx_rainbow"), "bought a hat, a belt colour and a particle style");
        _hud.OpenHub(Hud.ShopTab);
        await Wait(0.5);
        foreach (var sc in FindAll<ScrollContainer>(_hud)) sc.ScrollVertical = 100000;
        await Wait(0.2);
        Shot("23_cosmetics_shop");
        _hud.ClosePanel();
        await Wait(0.3);
        Shot("24_hub_hat");

        // Step 5: underground belt, splitter, pipes, underground pipe, pump and refinery, built in an empty corner.
        _world.MilestoneIndex = 3;
        Entity Put(string type, int x, int y, int rot = 0)
        {
            var e = _world.Place(type, new Vector2I(x, y), rot, force: true);
            Check(e != null, $"placed {type} at {x},{y}");
            return e;
        }
        ((Chest)Put("chest", -8, 8)).Inv["iron_plate"] = 100;
        Put("inserter", -7, 8); Put("belt", -6, 8); Put("tunnel", -5, 8);
        Put("chest", -4, 8); Put("furnace", -3, 8); // in the way on the surface
        var exit = Put("tunnel", -2, 8);
        Put("belt", -1, 8); Put("splitter", 0, 8);
        ((Chest)Put("chest", -1, 5)).Inv["copper_plate"] = 50; Put("inserter", -1, 6, 1); Put("belt", -1, 7, 1); // a T-join from above
        Put("belt", 1, 8); Put("belt", 2, 8); var cFront = (Chest)Put("chest", 3, 8);
        Put("belt", 0, 9, 1); var cDown = (Chest)Put("chest", 0, 10);
        Put("pump", -6, 12); Put("pipe", -5, 12); Put("pipe", -4, 12);
        Put("pipe_tunnel", -3, 12); Put("belt", -1, 11, 1); Put("belt", -1, 12, 1); Put("belt", -1, 13, 1);
        var pexit = Put("pipe_tunnel", 1, 12); Put("pipe", 2, 12);
        var rf = (Refinery)Put("refinery", 3, 12);
        _world.SetRecipe(rf, "black_goo");
        Put("inserter", 3, 13, 3); ((Chest)Put("chest", 3, 14)).Inv["coal"] = 50;
        Put("pipe", 4, 12); Put("pipe", 5, 12); var outPipe = (Pipe)Put("pipe", 5, 11);
        Put("big_pole", 6, 9); Put("big_pole", 6 + 14, 9); // wired across the scene
        Check(exit.Exit && _world.Partner(exit) != null && pexit.Exit && _world.Partner(pexit) != null, "underground pieces paired up");
        _view.SetCamera(new Vector2(-1.5f, 10.5f) * 16, 3);
        await Wait(5);
        Check(cFront.Count > 0 && cDown.Count > 0, $"plates came through the tunnel and the splitter (front {cFront.Count}, down {cDown.Count})");
        Check(_world.FluidOf(outPipe) is { Fluid: "black_goo", Amount: > 0 }, $"refinery made black goo from pumped water ({_world.FluidOf(outPipe)?.Fluid} {_world.FluidOf(outPipe)?.Amount})");
        Shot("15_step5_surface");
        _s.Stats["milestone"] = false; // the milestone box would cover the refinery
        _hud.ApplyStats();
        var over = _view.GetCanvasTransform() * (new Vector2(3.5f, 12.5f) * 16); // the refinery
        Input.ParseInputEvent(new InputEventMouseMotion { Position = over, GlobalPosition = over });
        await Wait(0.3);
        Check(FindAll<Label>(_hud).Any(l => l.IsVisibleInTree() && l.Text == "Refinery")
              && FindAll<Label>(_hud).Any(l => l.IsVisibleInTree() && l.Text.Contains("Makes 20 Black Goo")), "hovering a building shows its info card");
        Shot("15b_hover_card");
        _s.Stats.Remove("milestone");
        _hud.ApplyStats();
        Key(Godot.Key.V);
        await Wait(0.3);
        Check(_view.Underground, "V switches to the underground view");
        Shot("16_step5_underground");
        Key(Godot.Key.V);
        await Wait(0.1);
        Check(!_view.Underground, "V again switches back");
        _hud.Inspect(outPipe);
        await Wait(0.5);
        Shot("17_pipe_panel");
        _hud.ClosePanel();
        _hud.Inspect(rf);
        await Wait(0.5);
        Shot("18_refinery_recipes");
        _hud.ClosePanel();
        // Milestones 4 to 6: every new item in storage, a solar array on the scene's network, the full recipe list.
        _world.MilestoneIndex = Defs.Milestones.Length;
        foreach (var id in new[] { "sand", "glass", "lens", "battery", "motor", "lamp", "smart_chip", "frame", "toy_robot", "clock", "solar_panel", "drone", "computer", "sky_beacon" }) _world.AddStorage(id, 25);
        var sun = Put("solar", 7, 9);
        await Wait(0.5);
        Check(_world.NetOf(sun) != null, "the solar array joins the big pole's network");
        Check(FindAll<Label>(_hud).Any(l => l.IsVisibleInTree() && l.Text.StartsWith("All milestones done")), "after milestone 6 the tracker says all are done");
        await Wait(1.5); // past the milestone flash
        Shot("25_new_items_and_solar");
        var rod = (LightningRod)Put("lightning_rod", 5, 7);
        await Wait(0.2);
        Check(_world.NetOf(rod) != null, "the lightning rod joins the big pole's network");
        var (camWas, zoomWas) = (_view.CamTarget, _view.ZoomTarget);
        _view.SetCamera((rod.Pos + new Vector2(0.5f, -1)) * 16, 3);
        rod.Charge = Defs.LightningTicks; // a strike now, for the picture
        _world.Emit("lightning", rod.Pos, rod.Type);
        await Wait(0.1);
        Shot("25b_lightning");
        _view.SetCamera(camWas, zoomWas);
        _hud.Inspect(Find<Assembler>());
        await Wait(0.5);
        Shot("26_all_recipes");
        _hud.ClosePanel();
        _view.SetCamera(Vector2.Zero, 0.5f);
        await Wait(1.5); // ground chunks bake a few per frame
        Shot("27_ore_overview");
        _view.SetCamera(Vector2.Zero, 2);

        // Mini-size layout without the real always-on-top window: same size, HUD in mini mode.
        GetWindow().Size = new Vector2I(400, 300);
        _view.Mini = true;
        _hud.SetMini(true);
        await Wait(0.6);
        Check(GetViewport().GetVisibleRect().Size == new Vector2(400, 300), $"window is mini sized ({GetViewport().GetVisibleRect().Size})");
        Shot("10_mini_game");
        var slots = FindAll<Button>(_hud).Where(b => b.HasMeta("id")).ToList();
        Check(slots.Count == _view.Hotbar().Count && slots.Max(b => b.GetGlobalRect().End.X) <= 400 && slots.Min(b => b.GetGlobalRect().Position.X) >= 0, $"all {slots.Count} hotbar slots fit in the mini window");
        _world.MilestoneIndex = Defs.Milestones.Length; // every belt tier unlocked
        _view.Selected = null;
        Key(Godot.Key.Key1);
        await Wait(0.3);
        var strip = FindAll<Button>(_hud).Where(b => b.IsVisibleInTree() && b.TooltipText.Contains("Tab: next tier")).ToList();
        Check(strip.Count == Defs.BeltTiers.Length && strip.Max(b => b.GetGlobalRect().End.X) <= 400 && strip.Min(b => b.GetGlobalRect().Position.X) >= 0
            && strip.Max(b => b.GetGlobalRect().End.Y) <= slots.Min(b => b.GetGlobalRect().Position.Y), $"the belt tier strip ({strip.Count}) fits in the mini window above the hotbar");
        Shot("10b_mini_tier_strip");
        _view.Selected = null;
        await Wait(0.1);
        _hud.OpenHub(Hud.SellTab);
        await Wait(0.5);
        Shot("11_mini_hub");
        _hud.OpenHub(Hud.ShopTab);
        await Wait(0.5);
        Shot("12_mini_shop");
        _hud.OpenBuildMenu();
        await Wait(0.5);
        Shot("13_mini_build");
        OpenSettings((t, w) => _hud.OpenPanel(t, w));
        await Wait(0.5);
        Shot("14_mini_settings");

        GD.Print(_uiFails == 0 ? "UITEST PASS" : $"UITEST FAIL ({_uiFails})");
        GetTree().Quit(_uiFails == 0 ? 0 : 1);
    }
}
