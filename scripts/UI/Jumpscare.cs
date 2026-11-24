using Godot;
using TinyWorks.View;

namespace TinyWorks.UI;

// A pale face with hollow eyes that lunges at the screen for a moment, with a screech. Drawn in code.
public sealed partial class Jumpscare : Control
{
    float _t;
    readonly RandomNumberGenerator _rng = new();

    public static void Show(Control parent, Sfx sfx)
    {
        var s = new Jumpscare { MouseFilter = MouseFilterEnum.Ignore, ProcessMode = ProcessModeEnum.Always };
        s.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        parent.AddChild(s);
        sfx.Play("scare", 0, 0);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t > 1.4f) QueueFree();
        QueueRedraw();
    }

    public override void _Draw()
    {
        float a = Mathf.Clamp((1.4f - _t) / 0.35f, 0, 1); // fades out over the last 0.35 s
        float grow = Mathf.Min(1, _t / 0.12f);
        float r = Mathf.Min(Size.X, Size.Y) * Mathf.Lerp(0.15f, 0.5f, grow * grow);
        var c = Size / 2 + new Vector2(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1)) * 14 * a;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.9f * a));

        DrawSetTransform(c, 0, new Vector2(0.8f, 1)); // a tall oval head
        DrawCircle(Vector2.Zero, r, new Color("c9c7b8") with { A = a });
        DrawSetTransform(Vector2.Zero);
        foreach (float side in new[] { -1f, 1f })
        {
            var eye = c + new Vector2(side * r * 0.32f, -r * 0.22f);
            DrawCircle(eye, r * 0.2f, new Color(0, 0, 0, a));
            DrawCircle(eye + new Vector2(0, r * 0.03f), r * 0.045f, new Color("ff2a2a") with { A = a });
        }
        var mouth = new[] { c + new Vector2(-r * 0.5f, r * 0.25f), c + new Vector2(r * 0.5f, r * 0.25f), c + new Vector2(r * 0.3f, r * 0.72f), c + new Vector2(-r * 0.3f, r * 0.72f) };
        DrawColoredPolygon(mouth, new Color(0, 0, 0, a));
        for (int i = 0; i < 6; i++) // jagged teeth along the top of the mouth
        {
            float x0 = -0.5f + i / 6f, x1 = x0 + 1 / 6f;
            DrawColoredPolygon(new[] { c + new Vector2(r * x0, r * 0.25f), c + new Vector2(r * x1, r * 0.25f), c + new Vector2(r * (x0 + x1) / 2, r * 0.45f) }, new Color("eeeadb") with { A = a });
        }
    }
}
