using System;
using System.Collections.Generic;
using Godot;
using TinyWorks.Platform;
using TinyWorks.View;

namespace TinyWorks.UI;

// Fills a panel with every setting. Each change is applied and saved right away via `apply`.
public static class SettingsUi
{

    static Control Slider(string name, double min, double max, float value, Action<float> set, Action apply)
    {
        var label = Percent(value);
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = 0.05, Value = value, CustomMinimumSize = new Vector2(140, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        slider.ValueChanged += v => { set((float)v); label.Text = $"{v:P0}"; apply(); };
        return Line(name, slider, label);
    }

    public static void Fill(VBoxContainer body, Settings s, Action apply, GlobalHotkeys keys, Sfx sfx)
    {
        Section(body, "Mini window");
        var corner = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        foreach (var c in new[] { "Top left", "Top right", "Bottom left", "Bottom right" }) corner.AddItem(c);
        corner.Selected = s.Corner;
        corner.ItemSelected += i => { s.Corner = (int)i; apply(); };
        body.AddChild(Line("Corner", corner));

        body.AddChild(Slider("Opacity on hover", 0.2, 1, s.MiniOpacity, v => s.MiniOpacity = v, apply));
        body.AddChild(Slider("Opacity otherwise", 0.2, 1, s.MiniIdleOpacity, v => s.MiniIdleOpacity = v, apply));
        body.AddChild(Slider("Size on hover", 1, 2, s.MiniHoverScale, v => s.MiniHoverScale = v, apply));

        Section(body, "Frame rate");
        body.AddChild(Line("When focused", Fps(s.FpsFocused, v => { s.FpsFocused = v; apply(); })));
        body.AddChild(Line("Mini or in background", Fps(s.FpsBackground, v => { s.FpsBackground = v; apply(); })));
        var vsync = new CheckBox { Text = "VSync: no tearing, but never faster than your screen", ButtonPressed = s.VSync, FocusMode = Control.FocusModeEnum.None };
        vsync.Toggled += on => { s.VSync = on; apply(); };
        body.AddChild(vsync);

        Section(body, "Keys (work from any app on Windows and X11)");
        body.AddChild(Line("Hide / show window", KeyButton(() => s.HideKey, v => { s.HideKey = v; apply(); }, keys)));
        body.AddChild(Line("Normal / mini window", KeyButton(() => s.ModeKey, v => { s.ModeKey = v; apply(); }, keys)));

        Section(body, "Effects");
        var head = new GridContainer { Columns = 3 };
        head.AddThemeConstantOverride("h_separation", 16);
        head.AddChild(Ui.Grow(Ui.MakeLabel("", 14)));
        head.AddChild(Ui.MakeLabel("Normal", 13, Ui.Muted));
        head.AddChild(Ui.MakeLabel("Mini", 13, Ui.Muted));
        foreach (var (key, name) in new[] { ("place", "Building and removing"), ("production", "Machines, particles, numbers"), ("screen", "Screen shake, zoom easing, slow motion"), ("ui", "Bouncy buttons and counters") })
        {
            head.AddChild(Ui.Grow(Ui.MakeLabel(name, 14)));
            foreach (var mode in new[] { "normal", "mini" })
            {
                var id = $"{key}.{mode}";
                var cb = new CheckBox { ButtonPressed = s.Juice.GetValueOrDefault(id, true), FocusMode = Control.FocusModeEnum.None, TooltipText = $"{name} in the {mode} window" };
                cb.Toggled += on => { s.Juice[id] = on; apply(); };
                head.AddChild(cb);
            }
        }
        body.AddChild(head);

        Section(body, "Show on screen");
        foreach (var (key, name) in new[] { ("storage", "Storage counts"), ("coins", "Coins"), ("coinrate", "Coins per minute"), ("milestone", "Milestone progress"), ("bottleneck", "Bottleneck bubbles over machines"), ("tooltips", "Info card for what the mouse is over"), ("hints", "Key hints") })
        {
            var cb = new CheckBox { Text = name, ButtonPressed = s.StatOn(key), FocusMode = Control.FocusModeEnum.None };
            cb.Toggled += on => { s.Stats[key] = on; apply(); };
            body.AddChild(cb);
        }

        Section(body, "Sound");
        var volLabel = Percent(s.SfxVolume);
        var vol = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = s.SfxVolume, CustomMinimumSize = new Vector2(140, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        vol.ValueChanged += v => { s.SfxVolume = (float)v; volLabel.Text = $"{v:P0}"; apply(); };
        vol.DragEnded += _ => sfx.Play("click");
        body.AddChild(Line("Effects volume", vol, volLabel));
        var musicLabel = Percent(s.MusicVolume);
        var music = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = s.MusicVolume, CustomMinimumSize = new Vector2(140, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        music.ValueChanged += v => { s.MusicVolume = (float)v; musicLabel.Text = $"{v:P0}"; apply(); };
        body.AddChild(Line("Music volume", music, musicLabel));

        Section(body, "Start");
        var skip = new CheckBox { Text = "Skip the title screen and open the last save", ButtonPressed = s.SkipTitle, FocusMode = Control.FocusModeEnum.None };
        skip.Toggled += on => { s.SkipTitle = on; apply(); };
        body.AddChild(skip);
    }

    static void Section(VBoxContainer body, string title)
    {
        var l = Ui.MakeLabel(title, 15, Ui.Accent);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(l);
    }

    // Fixed width, right-aligned: "60 %" and "100 %" take the same space, so the slider never shifts.
    static Label Percent(float value)
    {
        var l = Ui.MakeLabel($"{value:P0}", 14, Ui.Muted);
        l.CustomMinimumSize = new Vector2(48, 0);
        l.HorizontalAlignment = HorizontalAlignment.Right;
        return l;
    }

    static HBoxContainer Line(string label, params Control[] controls)
    {
        var row = Ui.Row(Ui.Grow(Ui.MakeLabel(label, 14)));
        foreach (var c in controls) row.AddChild(c);
        return row;
    }

    // Any number from 5 to 1000; 0 means no limit. Applies as soon as you press Enter or click away.
    static SpinBox Fps(int current, Action<int> set)
    {
        var box = new SpinBox { MinValue = 0, MaxValue = 1000, Step = 1, Value = current, Suffix = "fps", CustomMinimumSize = new Vector2(130, 0), TooltipText = "Type any number. 0 = no limit." };
        box.ValueChanged += v => { int f = (int)v; set(f is > 0 and < 5 ? 5 : f); };
        return box;
    }

    // Click, then press the new key (with Ctrl/Shift/Alt if wanted). Esc cancels. Hotkeys pause meanwhile.
    static Button KeyButton(Func<string> get, Action<string> set, GlobalHotkeys keys)
    {
        var b = new Button { Text = get(), CustomMinimumSize = new Vector2(150, 36), ToggleMode = true };
        void Stop() { b.ButtonPressed = false; b.Text = get(); keys.Paused = false; }
        b.Toggled += on => { if (on) { b.Text = "Press a key..."; keys.Paused = true; } else Stop(); };
        b.FocusExited += Stop;
        b.TreeExiting += () => keys.Paused = false; // the panel closing mid-rebind must never leave F7/F8 dead
        b.GuiInput += e =>
        {
            if (!b.ButtonPressed || e is not InputEventKey { Pressed: true, Echo: false } k) return;
            b.AcceptEvent();
            if (k.Keycode == Key.Escape) { Stop(); return; }
            if (k.Keycode is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta) return; // wait for the real key
            if (!GlobalHotkeys.CanWatch(k.Keycode)) { b.Text = "Letter, digit or F-key"; return; }
            var combo = (k.CtrlPressed ? "Ctrl+" : "") + (k.ShiftPressed ? "Shift+" : "") + (k.AltPressed ? "Alt+" : "") + OS.GetKeycodeString(k.Keycode);
            set(combo);
            Stop();
        };
        return b;
    }
}
