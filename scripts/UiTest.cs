using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using TinyWorks.Sim;

namespace TinyWorks;

// Dev check: "-- --data <dir> --uitest <outdir>" loads the last save in <dir>, opens every window,
// sends key events through the engine and saves screenshots of the game's own render.
// Needs no mouse and no focus, so it can run while someone uses the PC.
public partial class Main
{
    int _uiFails;

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

        LoadGame(_s.LastSlot);
        await Wait(0.35);
        Shot("01_celebration");
        await Wait(2.5);
        Shot("02_game");
        Check(_world.MilestoneIndex == 1, "milestone 1 reached from the prepared save");
        Check(_view.Hotbar().Count == Defs.Buildings.Count - 3, $"every building is on the hotbar ({_view.Hotbar().Count} slots)"); // belt tiers share slot 1, no Hub
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

        _hud.OpenHub(true);
        await Wait(0.5);
        Shot("05_hub_shop");
        Check(_world.BuyUpgrade("machine_speed"), "bought an upgrade");
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

        _world.MilestoneIndex = 2; // fast belts unlocked: 1 steps through the tiers
        Key(Godot.Key.Key1);
        await Wait(0.1);
        Key(Godot.Key.Key1);
        await Wait(0.1);
        Check(_view.Selected == "fast_belt", $"pressing 1 twice picks the fast belt (got {_view.Selected})");
        Key(Godot.Key.Key1);
        await Wait(0.1);
        Check(_view.Selected == null, "a third press puts the belt away");

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
        Put("belt", 1, 8); Put("belt", 2, 8); var cFront = (Chest)Put("chest", 3, 8);
        Put("belt", 0, 9, 1); var cDown = (Chest)Put("chest", 0, 10);
        Put("pump", -6, 12); Put("pipe", -5, 12); Put("pipe", -4, 12);
        Put("pipe_tunnel", -3, 12); Put("belt", -1, 11, 1); Put("belt", -1, 12, 1); Put("belt", -1, 13, 1);
        var pexit = Put("pipe_tunnel", 1, 12); Put("pipe", 2, 12);
        var rf = (Refinery)Put("refinery", 3, 12);
        _world.SetRecipe(rf, "black_goo");
        Put("inserter", 3, 13, 3); ((Chest)Put("chest", 3, 14)).Inv["coal"] = 50;
        Put("pipe", 4, 12); Put("pipe", 5, 12); var outPipe = (Pipe)Put("pipe", 5, 11);
        Check(exit.Exit && _world.Partner(exit) != null && pexit.Exit && _world.Partner(pexit) != null, "underground pieces paired up");
        _view.SetCamera(new Vector2(-1.5f, 10.5f) * 16, 3);
        await Wait(5);
        Check(cFront.Count > 0 && cDown.Count > 0, $"plates came through the tunnel and the splitter (front {cFront.Count}, down {cDown.Count})");
        Check(_world.FluidOf(outPipe) is { Fluid: "black_goo", Amount: > 0 }, $"refinery made black goo from pumped water ({_world.FluidOf(outPipe)?.Fluid} {_world.FluidOf(outPipe)?.Amount})");
        Shot("15_step5_surface");
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
        _view.SetCamera(Vector2.Zero, 2);

        // Mini-size layout without the real always-on-top window: same size, HUD in mini mode.
        GetWindow().Size = new Vector2I(400, 300);
        _view.Mini = true;
        _hud.SetMini(true);
        await Wait(0.6);
        Check(GetViewport().GetVisibleRect().Size == new Vector2(400, 300), $"window is mini sized ({GetViewport().GetVisibleRect().Size})");
        Shot("10_mini_game");
        var slots = FindAll<Button>(_hud).Where(b => b.HasMeta("id")).ToList();
        Check(slots.Count == 15 && slots.Max(b => b.GetGlobalRect().End.X) <= 400 && slots.Min(b => b.GetGlobalRect().Position.X) >= 0, $"all {slots.Count} hotbar slots fit in the mini window");
        _hud.OpenHub(false);
        await Wait(0.5);
        Shot("11_mini_hub");
        _hud.OpenHub(true);
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
