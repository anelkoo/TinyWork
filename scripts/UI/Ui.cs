using System;
using Godot;
using TinyWorks.View;

namespace TinyWorks.UI;

// Shared look: warm slate panels, cream text (about 12:1 on the panel), one orange accent for "selected".
public static class Ui
{
    public static readonly Color Panel = new("262a35f0"), Btn = new("343a48"), BtnHover = new("414859"), Border = new("4a5163");
    public static readonly Color Text = new("f4ede1"), Muted = new("b8b2a7"), Accent = new("f2a65a"), Ink = new("2a2433");

    public static StyleBoxFlat Box(Color bg, Color border, int bw = 2, int radius = 8, int pad = 8)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border, CornerDetail = 4 };
        s.SetBorderWidthAll(bw);
        s.SetCornerRadiusAll(radius);
        s.SetContentMarginAll(pad);
        return s;
    }

    public static Theme MakeTheme()
    {
        var t = new Theme { DefaultFontSize = 16 };
        t.SetStylebox("panel", "PanelContainer", Box(Panel, Border));
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            t.SetStylebox("normal", type, Box(Btn, Border));
            t.SetStylebox("hover", type, Box(BtnHover, Accent with { A = 0.7f }));
            t.SetStylebox("hover_pressed", type, Box(Accent.Lightened(0.15f), Accent));
            t.SetStylebox("pressed", type, Box(Accent, Accent));
            t.SetStylebox("disabled", type, Box(Btn with { A = 0.5f }, Border));
            t.SetStylebox("focus", type, Box(Colors.Transparent, Accent));
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, Text);
            t.SetColor("font_pressed_color", type, Ink);
            t.SetColor("font_focus_color", type, Text);
        }
        t.SetStylebox("normal", "LineEdit", Box(new Color("1d2029"), Border));
        t.SetStylebox("focus", "LineEdit", Box(Colors.Transparent, Accent));
        t.SetColor("font_color", "LineEdit", Text);
        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_color", "CheckBox", Text);
        t.SetColor("font_hover_color", "CheckBox", Accent);
        t.SetStylebox("panel", "TooltipPanel", Box(Panel, Accent, 1, 6, 6));
        t.SetColor("font_color", "TooltipLabel", Text);
        t.SetStylebox("panel", "PopupMenu", Box(Panel, Border));
        t.SetStylebox("panel", "AcceptDialog", Box(Panel, Border));
        t.SetStylebox("background", "ProgressBar", Box(new Color("1d2029"), Border, 1, 4, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, Accent, 0, 4, 0));
        t.SetColor("font_color", "CheckButton", Text);
        t.SetColor("font_hover_color", "CheckButton", Accent);
        t.SetColor("font_hover_pressed_color", "CheckButton", Accent);
        t.SetColor("font_hover_pressed_color", "CheckBox", Accent);
        t.SetColor("font_pressed_color", "CheckButton", Text);
        t.SetStylebox("panel", "PopupPanel", Box(Panel, Border));
        return t;
    }

    public static TextureRect Icon(Texture2D tex, float size = 16) => new()
    {
        Texture = tex, CustomMinimumSize = Vector2.One * size, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static HBoxContainer Row(params Control[] items)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        foreach (var c in items) row.AddChild(c);
        if (items.Length > 0 && items[0] is TextureRect) Glow(row); // item rows light up under the mouse
        return row;
    }

    public static Label Grow(Label l) { l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; return l; }

    public static string Cost((string Item, int Count)[] cost) =>
        cost.Length == 0 ? "free" : string.Join(", ", Array.ConvertAll(cost, c => $"{c.Count} {Sim.Defs.Items[c.Item].Name}"));

    public static Button MakeButton(string text, Action onPress, Sfx sfx = null)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 36) };
        b.Pressed += () => { sfx?.Play("click"); onPress(); };
        Squishy(b);
        return b;
    }

    // UI juice: buttons grow a little under the mouse, dip when pressed and spring back.
    public static void Squishy(Control c)
    {
        const float hover = 1.06f;
        c.Resized += () => c.PivotOffset = c.Size / 2;
        bool Live() => Enabled && c is not BaseButton { Disabled: true };
        void To(float s, double t) => c.CreateTween().TweenProperty(c, "scale", Vector2.One * s, t).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        c.MouseEntered += () => { if (Live()) To(hover, 0.12); };
        c.MouseExited += () => { if (Enabled) To(1, 0.15); };
        c.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb || !Live()) return;
            var tw = c.CreateTween();
            if (mb.Pressed) tw.TweenProperty(c, "scale", Vector2.One * 0.9f, 0.06);
            else tw.TweenProperty(c, "scale", Vector2.One * (c.GetGlobalRect().HasPoint(c.GetGlobalMousePosition()) ? hover : 1), 0.35).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        };
    }

    // Hover for things that aren't buttons (item rows, labels): they brighten while the mouse is over them.
    public static void Glow(Control c)
    {
        if (c.MouseFilter == Control.MouseFilterEnum.Ignore) c.MouseFilter = Control.MouseFilterEnum.Pass;
        void To(Color m) => c.CreateTween().TweenProperty(c, "modulate", m, 0.1);
        c.MouseEntered += () => To(new Color(1.35f, 1.3f, 1.2f));
        c.MouseExited += () => To(Colors.White);
    }

    public static bool Enabled = true; // UI-juice toggle for the current window mode

    // Panels pop in and overshoot a little when they open. Scale, not position, so containers keep their layout.
    public static void PopIn(Control c)
    {
        if (!Enabled) return;
        c.Modulate = Colors.Transparent;
        c.Resized += () => c.PivotOffset = c.Size / 2;
        var tw = c.CreateTween().SetParallel();
        tw.TweenProperty(c, "modulate", Colors.White, 0.15);
        tw.TweenProperty(c, "scale", Vector2.One, 0.35).From(Vector2.One * 0.85f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public static Label MakeLabel(string text, int size = 16, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        return l;
    }

    public static PanelContainer Center(Control parent, Control content)
    {
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        center.MouseFilter = Control.MouseFilterEnum.Ignore;
        parent.AddChild(center);
        var panel = new PanelContainer();
        panel.AddChild(content);
        center.AddChild(panel);
        return panel;
    }
}
