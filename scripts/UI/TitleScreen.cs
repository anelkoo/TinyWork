using System;
using Godot;
using TinyWorks.Sim;
using TinyWorks.View;

namespace TinyWorks.UI;

// Title, new game and load pages. Settings get their own page once there is a settings UI (step 3).
public sealed partial class TitleScreen : Control
{
    public Sfx Sfx;
    public string LastSlot;
    public event Action<string> LoadSlot;
    public event Action<string, string, bool> NewGame; // name, mode, flat
    public event Action Quit, OpenSettings;

    VBoxContainer _page;
    float _t;
    readonly RandomNumberGenerator _rng = new();
    readonly (Vector2 P, float Speed, string Item)[] _drift = new (Vector2, float, string)[24];

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var ids = new System.Collections.Generic.List<string>(Defs.Items.Keys);
        for (int i = 0; i < _drift.Length; i++) _drift[i] = (new Vector2(_rng.Randf(), _rng.Randf()), _rng.RandfRange(12, 40), ids[i % ids.Count]);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        var title = new BouncyTitle();
        box.AddChild(title);
        _page = new VBoxContainer();
        _page.AddThemeConstantOverride("separation", 8);
        box.AddChild(_page);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        center.AddChild(box);
        AddChild(center);
        // Smaller title and tighter spacing when the window is the mini one.
        void Fit()
        {
            bool small = Size.Y < 420;
            title.FontSize = small ? 34 : 64;
            title.CustomMinimumSize = small ? new Vector2(260, 48) : new Vector2(420, 100);
            _page.CustomMinimumSize = new Vector2(small ? 260 : 320, 0);
            _page.AddThemeConstantOverride("separation", small ? 4 : 8);
            box.AddThemeConstantOverride("separation", small ? 2 : 10);
        }
        Resized += Fit;
        Fit();
        MainPage();
    }

    void Swap(Action fill)
    {
        foreach (var c in _page.GetChildren()) c.QueueFree();
        fill();
        Ui.PopIn(_page);
    }

    void MainPage() => Swap(() =>
    {
        if (Persist.SlotExists(LastSlot))
        {
            var cont = Ui.MakeButton("Continue", () => LoadSlot?.Invoke(LastSlot), Sfx);
            _page.AddChild(cont);
            cont.CallDeferred(Control.MethodName.GrabFocus);
        }
        _page.AddChild(Ui.MakeButton("New Game", NewPage, Sfx));
        _page.AddChild(Ui.MakeButton("Load Game", LoadPage, Sfx));
        _page.AddChild(Ui.MakeButton("Settings", () => OpenSettings?.Invoke(), Sfx));
        _page.AddChild(Ui.MakeButton("Quit", () => Quit?.Invoke(), Sfx));
    });

    // A scrollable page with a Back button; Main fills it (used for Settings).
    public VBoxContainer OpenPage(string title, float width)
    {
        VBoxContainer body = null;
        Swap(() =>
        {
            _page.AddChild(Ui.MakeLabel(title, 20));
            bool small = Size.Y < 420;
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(Mathf.Min(width, Size.X - 24), small ? Size.Y - 150 : Mathf.Min(420, Size.Y - 220)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            body.AddThemeConstantOverride("separation", 6);
            scroll.AddChild(body);
            _page.AddChild(scroll);
            _page.AddChild(Ui.MakeButton("Back", MainPage, Sfx));
        });
        return body;
    }

    void NewPage() => Swap(() =>
    {
        _page.AddChild(Ui.MakeLabel("Factory name", 14, Ui.Muted));
        var name = new LineEdit { Text = "My Factory", CustomMinimumSize = new Vector2(0, 36), SelectAllOnFocus = true };
        _page.AddChild(name);
        _page.AddChild(Ui.MakeLabel("Mode", 14, Ui.Muted));
        var mode = new OptionButton { CustomMinimumSize = new Vector2(0, 36) };
        mode.AddItem("Normal: unlock and pay for buildings");
        mode.AddItem("Sandbox: everything free");
        _page.AddChild(mode);
        var flat = new CheckBox { Text = "Flat world with no ore", Visible = false };
        mode.ItemSelected += i => flat.Visible = i == 1;
        _page.AddChild(flat);
        _page.AddChild(Ui.MakeButton("Create", () =>
        {
            var n = name.Text.Trim();
            NewGame?.Invoke(n == "" ? "My Factory" : n, mode.Selected == 1 ? "sandbox" : "normal", mode.Selected == 1 && flat.ButtonPressed);
        }, Sfx));
        _page.AddChild(Ui.MakeButton("Back", MainPage, Sfx));
        name.CallDeferred(Control.MethodName.GrabFocus);
    });

    void LoadPage() => Swap(() =>
    {
        var slots = Persist.ListSlots();
        if (slots.Count == 0) _page.AddChild(Ui.MakeLabel("No saves yet. Start a new game.", 16, Ui.Muted));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(Mathf.Min(420, Size.X - 24), Mathf.Min(Size.Y < 420 ? 150 : 300, slots.Count * 46)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        _page.AddChild(scroll);
        foreach (var (slot, data) in slots)
        {
            var row = new HBoxContainer();
            var load = Ui.MakeButton($"{data.Name}   ·   {(data.Mode == "sandbox" ? "Sandbox" : "Normal")}   ·   {data.SavedAt}", () => LoadSlot?.Invoke(slot), Sfx);
            load.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            load.Alignment = HorizontalAlignment.Left;
            load.ClipText = true;
            row.AddChild(load);
            row.AddChild(Ui.MakeButton("Delete", () => ConfirmDelete(slot, data.Name), Sfx));
            list.AddChild(row);
        }
        _page.AddChild(Ui.MakeButton("Back", MainPage, Sfx));
    });

    void ConfirmDelete(string slot, string name)
    {
        var d = new ConfirmationDialog { DialogText = $"Delete \"{name}\"? This can't be undone.", OkButtonText = "Delete", Title = "Delete save" };
        d.Confirmed += () => { Persist.DeleteSlot(slot); LoadPage(); };
        d.Canceled += d.QueueFree;
        AddChild(d);
        d.PopupCentered();
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        for (int i = 0; i < _drift.Length; i++)
        {
            var d = _drift[i];
            d.P.Y -= d.Speed * (float)delta / Mathf.Max(1, Size.Y);
            if (d.P.Y < -0.05f) d.P = new Vector2(_rng.Randf(), 1.05f);
            _drift[i] = d;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("1f2330"));
        foreach (var d in _drift) // items floating up like a lazy conveyor
        {
            var p = d.P * Size + new Vector2(Mathf.Sin(_t + d.Speed) * 10, 0);
            DrawTextureRect(Sprites.Items[d.Item], new Rect2(p.Round(), Vector2.One * 24), false, new Color(1, 1, 1, 0.35f));
        }
    }

    sealed partial class BouncyTitle : Control
    {
        float _t;
        public int FontSize = 64;
        public override void _Process(double delta) { _t += (float)delta; QueueRedraw(); }
        public override void _Draw()
        {
            const string text = "Tiny Works";
            var font = ThemeDB.FallbackFont;
            int size = FontSize;
            float w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            float x = (Size.X - w) / 2;
            for (int i = 0; i < text.Length; i++)
            {
                var ch = text[i].ToString();
                float y = size * 1.1f + (Ui.Enabled ? Mathf.Sin(_t * 3 + i * 0.6f) * size / 10 : 0);
                DrawString(font, new Vector2(x + 4, y + 5), ch, HorizontalAlignment.Left, -1, size, Ui.Ink);
                DrawString(font, new Vector2(x, y), ch, HorizontalAlignment.Left, -1, size, i < 4 ? Ui.Accent : Ui.Text);
                x += font.GetStringSize(ch, HorizontalAlignment.Left, -1, size).X;
            }
        }
    }
}
