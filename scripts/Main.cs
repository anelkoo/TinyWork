using System;
using Godot;
using TinyWorks.Platform;
using TinyWorks.Sim;
using TinyWorks.UI;
using TinyWorks.View;

namespace TinyWorks;

// Root: builds the viewport stack, owns window/hotkeys/audio, and switches between title and game.
public partial class Main : Node
{
    const double AutosaveSeconds = 120;

    Settings _s;
    AppWindow _win;
    GlobalHotkeys _keys;
    Sfx _sfx;
    SubViewport _worldVp;
    Control _uiRoot, _menu;
    World _world;
    WorldView _view;
    Hud _hud;
    TitleScreen _title;
    string _slot;
    double _sinceSave;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var args = OS.GetCmdlineUserArgs();
        int dataArg = Array.IndexOf(args, "--data");
        if (dataArg >= 0 && dataArg + 1 < args.Length) Persist.Root = args[dataArg + 1];
        if (Array.IndexOf(args, "--selftest") >= 0) { GetTree().Quit(SelfTest.Run() == 0 ? 0 : 1); return; }
        GetTree().AutoAcceptQuit = false;
        _s = Persist.LoadSettings();
        Sprites.Build();

        // Everything renders into one full-res viewport, so mini-mode opacity fades a single texture.
        var outer = new SubViewportContainer { Stretch = true };
        outer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(outer);
        // SubViewports filter linearly by default, whatever the project setting says; that blurred every pixel.
        var vp = new SubViewport { TransparentBg = true, CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest };
        outer.AddChild(vp);
        // The world draws at full resolution (WorldView doubles the zoom), so moving things step one screen pixel at a time.
        var worldBox = new SubViewportContainer { Stretch = true };
        worldBox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        vp.AddChild(worldBox);
        _worldVp = new SubViewport { CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest };
        worldBox.AddChild(_worldVp);
        _uiRoot = new Control { Theme = Ui.MakeTheme(), MouseFilter = Control.MouseFilterEnum.Ignore };
        _uiRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        vp.AddChild(_uiRoot);

        AddChild(_sfx = new Sfx { Volume = _s.SfxVolume });
        _win = new AppWindow { Faded = outer, MiniCorner = (ScreenCorner)_s.Corner, MiniSize = new Vector2I(_s.MiniW, _s.MiniH), FpsFocused = _s.FpsFocused, FpsBackground = _s.FpsBackground };
        AddChild(_win);
        _win.MiniOpacity = _s.MiniOpacity;
        _win.MiniIdleOpacity = _s.MiniIdleOpacity;
        _win.HoverScale = _s.MiniHoverScale;
        if (_s.NormalX >= 0) _win.RestoreNormal(new Rect2I(_s.NormalX, _s.NormalY, _s.NormalW, _s.NormalH));
        _win.Changed += OnWindowChanged;
        if (_s.WindowMode == nameof(WindowMode.Mini)) Callable.From(() => _win.SetMode(WindowMode.Mini)).CallDeferred();

        AddChild(_keys = new GlobalHotkeys());
        _keys.Bind("hide", ParseCombo(_s.HideKey, Key.F8));
        _keys.Bind("mode", ParseCombo(_s.ModeKey, Key.F7));
        _keys.Pressed += id => { if (id == "hide") _win.ToggleHidden(); else _win.ToggleMode(); };
        ApplySettings();

        int uiTest = Array.IndexOf(args, "--uitest");
        if (uiTest >= 0 && uiTest + 1 < args.Length) { RunUiTest(args[uiTest + 1]); return; }
        if (_s.SkipTitle && Persist.SlotExists(_s.LastSlot)) LoadGame(_s.LastSlot);
        else ShowTitle();
        OnWindowChanged();
    }

    static Combo ParseCombo(string text, Key fallback)
    {
        var parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries);
        var key = OS.FindKeycodeFromString(parts[^1]);
        if (key == Key.None) return new Combo(fallback);
        return new Combo(key, Array.IndexOf(parts, "Ctrl") >= 0, Array.IndexOf(parts, "Shift") >= 0, Array.IndexOf(parts, "Alt") >= 0);
    }

    // Called whenever a setting changes: push it into the window, hotkeys, sound and HUD, then save.
    void ApplySettings()
    {
        _win.MiniCorner = (ScreenCorner)_s.Corner;
        _win.MiniOpacity = _s.MiniOpacity;
        _win.MiniIdleOpacity = _s.MiniIdleOpacity;
        _win.HoverScale = _s.MiniHoverScale;
        _win.FpsFocused = _s.FpsFocused;
        _win.FpsBackground = _s.FpsBackground;
        _win.ApplyFps();
        _win.Reanchor();
        _keys.Bind("hide", ParseCombo(_s.HideKey, Key.F8));
        _keys.Bind("mode", ParseCombo(_s.ModeKey, Key.F7));
        OnWindowChanged();
        try { Persist.SaveSettings(_s); } catch (Exception e) { GD.PushError(e.ToString()); }
    }

    void OpenSettings(Func<string, float, VBoxContainer> openPanel) =>
        SettingsUi.Fill(openPanel("Settings", 560), _s, ApplySettings, _keys, _sfx);

    void OnWindowChanged()
    {
        bool mini = _win.Mode == WindowMode.Mini;
        if (_win.Hidden) Engine.TimeScale = 1; // never leave slow motion running while nobody watches
        _hud?.ApplyStats();
        Ui.Enabled = _s.JuiceOn("ui", mini);
        _hud?.SetMini(mini);
        if (_view != null) _view.Mini = mini;
        _sfx.Muted = _win.Hidden;
        // While hidden only the simulation runs; views and UI sleep.
        var pm = _win.Hidden ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
        _worldVp.ProcessMode = pm;
        _uiRoot.ProcessMode = pm;
        _sfx.Volume = _s.SfxVolume * (mini ? 0.5f : 1f);
    }

    void ShowTitle()
    {
        _title = new TitleScreen { Sfx = _sfx, LastSlot = _s.LastSlot };
        _title.LoadSlot += LoadGame;
        _title.NewGame += (name, mode, flat) =>
            StartGame(World.New(name, mode, flat, (int)(GD.Randi() % 1_000_000)), Persist.NewSlotId(), Vector2.Zero, 1);
        _title.Quit += QuitGame;
        _title.OpenSettings += () => OpenSettings(_title.OpenPage);
        _uiRoot.AddChild(_title);
    }

    void LoadGame(string slot)
    {
        try
        {
            var data = Persist.LoadSlot(slot);
            StartGame(World.FromSave(data), slot, new Vector2(data.CamX, data.CamY), data.Zoom);
        }
        catch (Exception e)
        {
            var d = new AcceptDialog { Title = "Couldn't load", DialogText = $"This save couldn't be opened.\n{e.Message}" };
            _uiRoot.AddChild(d);
            d.PopupCentered();
        }
    }

    void StartGame(World w, string slot, Vector2 cam, float zoom)
    {
        Engine.TimeScale = 1;
        _title?.QueueFree();
        _title = null;
        _world = w;
        _slot = _s.LastSlot = slot;
        _view = new WorldView { W = w, S = _s, Sfx = _sfx };
        _worldVp.AddChild(_view);
        _view.SetCamera(cam, zoom);
        _hud = new Hud { View = _view, Sfx = _sfx, S = _s };
        _uiRoot.AddChild(_hud);
        _view.Toast = _hud.Toast;
        _hud.OpenMenu += ToggleMenu;
        _hud.ToggleMini += _win.ToggleMode;
        _sinceSave = 0;
        OnWindowChanged();
        SaveNow();
    }

    void EndGame()
    {
        SaveNow();
        CloseMenu();
        Engine.TimeScale = 1;
        _view.QueueFree();
        _hud.QueueFree();
        _view = null;
        _hud = null;
        _world = null;
        ShowTitle();
    }

    void ToggleMenu()
    {
        if (_menu != null) { CloseMenu(); return; }
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(Ui.MakeLabel(_world.Name, 20));
        box.AddChild(Ui.MakeButton("Resume", CloseMenu, _sfx));
        box.AddChild(Ui.MakeButton("Save Now", () => { if (SaveNow()) _hud.Toast("Saved"); CloseMenu(); }, _sfx));
        box.AddChild(Ui.MakeButton("Settings", () => { CloseMenu(); OpenSettings((t, wd) => _hud.OpenPanel(t, wd)); }, _sfx));
        box.AddChild(Ui.MakeButton("Main Menu", EndGame, _sfx));
        box.AddChild(Ui.MakeButton("Quit", QuitGame, _sfx));
        var panel = Ui.Center(_uiRoot, box);
        _menu = panel.GetParent<Control>();
        Ui.PopIn(panel);
        _sfx.Play("open");
    }

    void CloseMenu()
    {
        _menu?.QueueFree();
        _menu = null;
    }

    // Game keys are read before the GUI, so step aside while a window is open (typing a number
    // into a field must not pick a building). Esc always closes the top window first.
    public override void _Input(InputEvent e)
    {
        if (_world == null || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (_hud.HasPanel)
        {
            if (k.Keycode != Key.Escape) return;
            _hud.ClosePanel();
        }
        else if (k.Keycode == Key.Escape)
        {
            if (_menu != null || !_view.HandleKey(Key.Escape)) ToggleMenu();
        }
        else if (_menu != null) return;
        else if (k.Keycode == Key.Q) _hud.OpenBuildMenu();
        else if (!_view.HandleKey(k.Keycode)) return;
        GetViewport().SetInputAsHandled();
    }

    public override void _PhysicsProcess(double delta)
    {
        _world?.Step();
        if (_win?.Hidden == true) _world?.Events.Clear(); // nobody sees effects while hidden; don't replay them all on show
    }

    public override void _Process(double delta)
    {
        if (_world == null || (_sinceSave += delta) < AutosaveSeconds) return;
        SaveNow();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) QuitGame();
    }

    bool SaveNow()
    {
        _sinceSave = 0;
        bool ok = true;
        if (_world != null)
        {
            try
            {
                var data = _world.ToSave();
                data.CamX = _view.CamTarget.X;
                data.CamY = _view.CamTarget.Y;
                data.Zoom = _view.ZoomTarget;
                Persist.SaveSlot(_slot, data);
            }
            catch (Exception e) { ok = false; _hud?.Toast("Save failed: " + e.Message); GD.PushError(e.ToString()); }
        }
        _s.WindowMode = _win.Mode.ToString();
        _s.MiniOpacity = _win.MiniOpacity;
        (_s.MiniW, _s.MiniH) = (_win.MiniSize.X, _win.MiniSize.Y);
        var n = _win.NormalRect;
        (_s.NormalX, _s.NormalY, _s.NormalW, _s.NormalH) = (n.Position.X, n.Position.Y, n.Size.X, n.Size.Y);
        try { Persist.SaveSettings(_s); } catch (Exception e) { GD.PushError(e.ToString()); }
        return ok;
    }

    void QuitGame()
    {
        SaveNow();
        GetTree().Quit();
    }
}
