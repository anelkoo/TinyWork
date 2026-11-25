using System.Collections.Generic;
using Godot;

namespace TinyWorks.View;

// World-space effects: particles, floating numbers, screen shake. Plain lists, drawn by WorldView.
public sealed class Juice
{
    struct Particle { public Vector2 P, V; public float Life, Max, Size, Grav; public Color C; }
    struct Floater { public Vector2 P; public string Text; public Color C; public float Life; public Texture2D Icon; }

    readonly List<Particle> _parts = new();
    readonly List<Floater> _floats = new();
    readonly RandomNumberGenerator _rng = new();
    float _trauma;
    public bool Thin; // reduced animations: half as many particles

    public Vector2 ShakeOffset { get; private set; }
    public System.Func<Color, Color> Recolor; // a particle style from the shop, or null for the natural colours

    public void Burst(Vector2 at, Color c, int n, float speed = 40, float size = 2, float grav = 120, float life = 0.45f)
    {
        if (_parts.Count > 600) return; // keep it cheap when lots happens at once
        if (Thin) n = (n + 1) / 2;
        for (int i = 0; i < n; i++)
        {
            var dir = Vector2.FromAngle(_rng.RandfRange(0, Mathf.Tau));
            _parts.Add(new Particle { P = at, V = dir * _rng.RandfRange(0.3f, 1f) * speed + new Vector2(0, -speed * 0.4f), Life = life * _rng.RandfRange(0.7f, 1.2f), Size = size, Grav = grav, C = Recolor?.Invoke(c) ?? c });
            _parts[^1] = _parts[^1] with { Max = _parts[^1].Life };
        }
    }

    public void Puff(Vector2 at, Color c) // soft rising smoke/dust
    {
        if (_parts.Count > 600) return;
        _parts.Add(new Particle { P = at + new Vector2(_rng.RandfRange(-2, 2), 0), V = new Vector2(_rng.RandfRange(-4, 4), -14), Life = 0.9f, Max = 0.9f, Size = 3, Grav = -6, C = c });
    }

    public void Float(Vector2 at, string text, Color c, Texture2D icon = null) =>
        _floats.Add(new Floater { P = at + new Vector2(_rng.RandfRange(-3, 3), 0), Text = text, C = c, Life = 0.9f, Icon = icon });

    public void Shake(float amount) => _trauma = Mathf.Min(1, _trauma + amount);

    public void Update(float dt)
    {
        for (int i = _parts.Count - 1; i >= 0; i--)
        {
            var p = _parts[i];
            p.Life -= dt;
            if (p.Life <= 0) { _parts.RemoveAt(i); continue; }
            p.V.Y += p.Grav * dt;
            p.V *= 1 - 2.5f * dt;
            p.P += p.V * dt;
            _parts[i] = p;
        }
        for (int i = _floats.Count - 1; i >= 0; i--)
        {
            var f = _floats[i];
            f.Life -= dt;
            f.P.Y -= 18 * dt;
            if (f.Life <= 0) _floats.RemoveAt(i); else _floats[i] = f;
        }
        _trauma = Mathf.Max(0, _trauma - dt * 2.2f);
        float s = _trauma * _trauma * 6;
        ShakeOffset = s < 0.05f ? Vector2.Zero : new Vector2(_rng.RandfRange(-s, s), _rng.RandfRange(-s, s));
    }

    public void Draw(CanvasItem ci)
    {
        foreach (var p in _parts)
        {
            float k = p.Life / p.Max;
            float sz = Mathf.Max(1, Mathf.Round(p.Size * (p.Grav < 0 ? 2 - k : k + 0.3f)));
            ci.DrawRect(new Rect2((p.P - Vector2.One * sz / 2).Round(), Vector2.One * sz), p.C with { A = p.C.A * Mathf.Min(1, k * 2) });
        }
        foreach (var f in _floats)
        {
            float pop = f.Life > 0.75f ? 1 + (f.Life - 0.75f) * 3 : 1; // pops in big, settles
            var pos = f.P.Round();
            if (f.Icon != null) ci.DrawTextureRect(f.Icon, new Rect2(pos - new Vector2(9, 1) * pop, Vector2.One * 8 * pop), false, Colors.White with { A = Mathf.Min(1, f.Life * 3) });
            Sprites.DrawText(ci, pos, f.Text, f.C with { A = Mathf.Min(1, f.Life * 3) });
        }
    }
}
