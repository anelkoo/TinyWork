using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TinyWorks.Sim;

namespace TinyWorks.View;

// Draws the world and turns mouse input into building. Lives inside the half-resolution pixel viewport.
public sealed partial class WorldView : Node2D
{
    const int T = Sprites.T;
    static readonly float[] Zooms = { 0.5f, 1, 2, 3, 4 };
    const float PixelScale = 2; // screen pixels per art pixel at zoom 1

    public World W;
    public Settings S;
    public Sfx Sfx;
    public bool Mini;
    public Action<string> Toast;
    public string Selected;
    public int Rot;
    public Camera2D Cam;
    public bool Underground; // V: dims the surface and shows what runs below it

    readonly Juice _juice = new();
    readonly Dictionary<Vector2I, Sprite2D> _chunks = new();
    readonly Dictionary<Entity, float> _born = new();
    readonly List<(string Type, Vector2 At, int Rot, float T)> _gone = new();
    readonly Dictionary<string, int> _pendingDeliver = new();
    float _time, _dt, _hubPulse, _targetZoom = 1, _deliverTimer;
    Vector2 _camTarget;
    Vector2I _hover, _lastPaint;
    Entity _lastBelt;
    bool _painting, _erasing, _panning;
    Hub _hub;

    public override void _Ready()
    {
        Cam = new Camera2D();
        AddChild(Cam);
    }

    public void SetCamera(Vector2 pos, float zoom)
    {
        _camTarget = Cam.Position = pos;
        _targetZoom = Mathf.Clamp(zoom, Zooms[0], Zooms[^1]);
        Cam.Zoom = Vector2.One * _targetZoom * PixelScale;
    }

    public Vector2 CamTarget => _camTarget;
    public float ZoomTarget => _targetZoom;

    bool J(string family) => S.JuiceOn(family, Mini);
    static Vector2 Center(Vector2I tile) => tile * T + Vector2.One * T / 2;

    // shortcut: the hotbar is simply every unlocked building in a fixed order; make it customizable if 9 slots run out.
    public List<string> Hotbar() => Defs.HotbarOrder.ToList();

    public Action<Entity> Inspect; // clicked a building with nothing selected
    public Action<GameEvent> OnEvent; // every world event, as the view drains them

    public void Select(string building)
    {
        if (!W.IsUnlocked(building))
        {
            Toast?.Invoke($"{Defs.Buildings[building].Name} unlocks with milestone {Defs.UnlockTier(building)}");
            Sfx.Play("error");
            return;
        }
        if (Defs.IsBelt(building) && Defs.IsBelt(Selected))
        {
            // Pressing the belt key again steps through the unlocked tiers, then puts the belt away.
            var tiers = new[] { "belt", "fast_belt", "express_belt" }.Where(W.IsUnlocked).ToList();
            int i = tiers.IndexOf(Selected);
            Selected = i >= 0 && i + 1 < tiers.Count ? tiers[i + 1] : null;
        }
        else Selected = Selected == building ? null : building;
        Sfx.Play("click");
    }

    // Keys the HUD forwards. Returns true when used.
    public bool HandleKey(Key k)
    {
        var bar = Hotbar();
        int slot = k == Key.Key0 ? 9 : k >= Key.Key1 && k <= Key.Key9 ? (int)(k - Key.Key1) : -1;
        if (slot >= 0 && slot < bar.Count) { Select(bar[slot]); return true; }
        switch (k)
        {
            case Key.R when Selected != null: Rot = (Rot + 1) % 4; Sfx.Play("rotate"); return true;
            case Key.R: W.Rotate(_hover); return true;
            case Key.X: W.Remove(_hover); return true;
            case Key.V:
                Underground = !Underground;
                Toast?.Invoke(Underground ? "Underground view: belts and pipes below the surface" : "Surface view");
                Sfx.Play("click");
                return true;
            case Key.Escape when Selected != null: Selected = null; return true;
        }
        return false;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion mm:
                if (_panning) _camTarget -= mm.Relative / Cam.Zoom.X;
                _hover = EventTile(mm);
                if (_painting && _hover != _lastPaint) PaintTo(_hover);
                if (_erasing) W.Remove(_hover);
                break;
            case InputEventMouseButton mb:
                _hover = EventTile(mb);
                switch (mb.ButtonIndex)
                {
                    case MouseButton.Left when mb.Pressed:
                        if (Selected != null) { _painting = true; _lastBelt = null; _lastPaint = _hover; PlaceOne(_hover, true); }
                        else if (W.At(_hover) is { } clicked) { Inspect?.Invoke(clicked); Sfx.Play("open"); }
                        else W.HandMine(_hover);
                        break;
                    case MouseButton.Left: _painting = false; break;
                    case MouseButton.Right when mb.Pressed:
                        if (!W.Remove(_hover) && Selected != null) Selected = null;
                        _erasing = true;
                        break;
                    case MouseButton.Right: _erasing = false; break;
                    case MouseButton.Middle: _panning = mb.Pressed; break;
                    case MouseButton.WheelUp when mb.Pressed: ZoomStep(+1); break;
                    case MouseButton.WheelDown when mb.Pressed: ZoomStep(-1); break;
                    default: return;
                }
                break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    // Where the event happened, not where the mouse is now: a fast drag has already moved on.
    Vector2I EventTile(InputEventMouse e) => (Vector2I)((GetCanvasTransform().AffineInverse() * e.Position) / T).Floor();

    void ZoomStep(int dir)
    {
        int i = Array.IndexOf(Zooms, _targetZoom);
        int j = Mathf.Clamp(i + dir, 0, Zooms.Length - 1);
        if (i == j) return;
        // keep the point under the mouse where it is
        var m = GetGlobalMousePosition();
        _camTarget += (m - _camTarget) * (1 - _targetZoom / Zooms[j]);
        _targetZoom = Zooms[j];
    }

    // Dragging: walk tile by tile so fast mouse moves leave no gaps; belts turn to follow the drag.
    void PaintTo(Vector2I target)
    {
        while (_lastPaint != target)
        {
            var d = target - _lastPaint;
            var step = Mathf.Abs(d.X) >= Mathf.Abs(d.Y) ? new Vector2I(Math.Sign(d.X), 0) : new Vector2I(0, Math.Sign(d.Y));
            if (Defs.IsBelt(Selected))
            {
                Rot = Array.IndexOf(Entity.Dirs, step);
                if (_lastBelt != null && W.Grid.ContainsValue(_lastBelt)) W.SetRot(_lastBelt.Pos, Rot);
            }
            _lastPaint += step;
            PlaceOne(_lastPaint, false);
        }
    }

    void PlaceOne(Vector2I tile, bool first)
    {
        if (Defs.IsBelt(Selected) && W.At(tile) is Belt existing) { W.SetRot(tile, Rot); _lastBelt = existing; return; }
        var e = W.Place(Selected, tile, Rot);
        if (e is Belt) _lastBelt = e;
        if (e == null && first && W.At(tile) == null)
        {
            W.CanPlace(Selected, tile, out var why);
            Toast?.Invoke(why);
            Sfx.Play("error");
            if (J("screen")) _juice.Shake(0.2f);
        }
    }

    public override void _Process(double delta)
    {
        _dt = (float)delta;
        _time += _dt;
        var move = new Vector2(
            (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) ? 1 : 0) - (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) ? 1 : 0),
            (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down) ? 1 : 0) - (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) ? 1 : 0));
        if (GetWindow().HasFocus()) _camTarget += move.Normalized() * 520 * _dt / Cam.Zoom.X;

        float z = J("screen") ? Mathf.Lerp(Cam.Zoom.X / PixelScale, _targetZoom, 1 - Mathf.Exp(-14 * _dt)) : _targetZoom;
        Cam.Zoom = Vector2.One * z * PixelScale;
        Cam.Position = J("screen") ? Cam.Position.Lerp(_camTarget, 1 - Mathf.Exp(-20 * _dt)) : _camTarget;
        Cam.Offset = _juice.ShakeOffset;

        StreamChunks();
        DrainEvents();
        _juice.Update(_dt);
        _hubPulse = Mathf.Max(0, _hubPulse - _dt * 4);
        for (int i = _gone.Count - 1; i >= 0; i--) if (_time - _gone[i].T > 0.25f) _gone.RemoveAt(i);
        QueueRedraw();
    }

    Rect2 ViewRect()
    {
        var size = GetViewportRect().Size / Cam.Zoom;
        return new Rect2(Cam.Position - size / 2, size);
    }

    void StreamChunks()
    {
        const int cs = Sprites.Chunk * T;
        var view = ViewRect().Grow(T * 4);
        var a = (Vector2I)(view.Position / cs).Floor();
        var b = (Vector2I)(view.End / cs).Floor();
        int made = 0;
        for (int cy = a.Y; cy <= b.Y; cy++)
            for (int cx = a.X; cx <= b.X; cx++)
            {
                var c = new Vector2I(cx, cy);
                if (_chunks.ContainsKey(c) || made++ >= 2) continue; // spread baking over frames
                var s = new Sprite2D { Texture = Sprites.PaintChunk(W, c), Centered = false, Position = c * cs, ZIndex = -10 };
                AddChild(s);
                _chunks[c] = s;
            }
        if (_chunks.Count <= 60) return;
        var keep = new Rect2I(a - Vector2I.One, b - a + Vector2I.One * 3);
        foreach (var c in new List<Vector2I>(_chunks.Keys))
            if (!keep.HasPoint(c)) { _chunks[c].QueueFree(); _chunks.Remove(c); }
    }

    void DrainEvents()
    {
        while (W.Events.TryDequeue(out var e))
        {
            OnEvent?.Invoke(e);
            var at = Center(e.Tile);
            switch (e.Kind)
            {
                case "placed":
                    if (W.At(e.Tile) is { } placed) _born[placed] = _time;
                    if (J("place")) _juice.Burst(at + new Vector2(0, 6), new Color("d9cbb0"), 8, 30, 2, 60);
                    if (J("screen")) _juice.Shake(0.12f);
                    Sfx.Play("place");
                    if (W.At(e.Tile) is Pole pole && W.NetOf(pole) is { } net && J("production"))
                        foreach (var (a, b) in net.Wires.Where(w => w.A == pole || w.B == pole))
                        {
                            for (int k = 1; k < 6; k++) _juice.Burst(PoleTop(a).Lerp(PoleTop(b), k / 6f), Sprites.Spark, 2, 30, 1, 40, 0.35f);
                            Sfx.Play("zap");
                        }
                    break;
                case "removed":
                    if (J("place")) { _gone.Add((e.Id, at, 0, _time)); _juice.Burst(at, Sprites.Accent, 10, 55); }
                    Sfx.Play("remove");
                    break;
                case "rotated":
                    if (W.At(e.Tile) is { } turned) _born[turned] = _time;
                    Sfx.Play("rotate");
                    break;
                case "mined":
                    if (J("production")) { _juice.Burst(at, Defs.Items[e.Id].Color, 7, 45); _juice.Float(at - new Vector2(0, 6), "+1", Colors.White, Sprites.Items[e.Id]); }
                    if (J("screen")) _juice.Shake(0.06f);
                    Sfx.Play("mine");
                    break;
                case "delivered":
                    _pendingDeliver[e.Id] = _pendingDeliver.GetValueOrDefault(e.Id) + 1;
                    _hubPulse = 1;
                    Sfx.Play("deliver", 0.04f, 0.12f);
                    break;
                case "crafted":
                    if (J("production")) _juice.Burst(at + new Vector2(-3, 2), Sprites.Glow, 3, 25, 1, 40, 0.3f);
                    Sfx.Play("craft", 0.05f, 0.15f);
                    break;
                case "sold":
                    _hub ??= W.Entities.Find(x => x is Hub) as Hub;
                    if (_hub != null && J("production"))
                    {
                        var top = _hub.Pos * T + new Vector2(18, -2);
                        _juice.Float(top, "+" + e.N, new Color("f7c948"), Sprites.Coin);
                        _juice.Burst(top, new Color("f7c948"), 5, 35, 2, 90);
                    }
                    Sfx.Play("coin", 0.05f, 0.2f);
                    break;
                case "spilled":
                    Toast?.Invoke($"Two fluids met: the smaller one ({Defs.Items[e.Id].Name}) spilled");
                    if (J("place")) _juice.Burst(at, Defs.Items[e.Id].Color, 12, 50, 2, 80);
                    Sfx.Play("error");
                    break;
                case "upgraded" or "bought":
                    Sfx.Play("coin");
                    break;
            }
        }
        // Batch Hub deliveries into "+N" numbers so 8 items/s stays readable.
        _deliverTimer += _dt;
        if (_deliverTimer < 0.4f || _pendingDeliver.Count == 0) return;
        _deliverTimer = 0;
        _hub ??= W.Entities.Find(x => x is Hub) as Hub;
        if (_hub != null && J("production"))
            foreach (var kv in _pendingDeliver) _juice.Float(_hub.Pos * T + new Vector2(28, -4), "+" + kv.Value, Colors.White, Sprites.Items[kv.Key]);
        _pendingDeliver.Clear();
    }

    // Squash and stretch for a moment after a building lands or turns.
    Vector2 Squash(Entity e)
    {
        if (!_born.TryGetValue(e, out var t0)) return Vector2.One;
        float k = _time - t0;
        if (k > 0.6f || !J("place")) { _born.Remove(e); return Vector2.One; }
        float a = 0.35f * Mathf.Exp(-k * 9) * Mathf.Cos(k * 28);
        return new Vector2(1 + a, 1 - a);
    }

    void Sprite(Texture2D tex, Vector2 center, float angle, Vector2 scale, Color? mod = null)
    {
        DrawSetTransform(center.Round(), angle, scale);
        DrawTexture(tex, -tex.GetSize() / 2, mod ?? Colors.White);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    static float Angle(int rot) => rot * Mathf.Pi / 2;

    // Moving things snap to whole screen pixels, not art pixels, so they glide instead of stepping 0-1-0-1.
    Vector2 Px(Vector2 p) => (p * Cam.Zoom.X).Round() / Cam.Zoom.X;

    public override void _Draw()
    {
        var view = ViewRect().Grow(T * 3);
        float f = (float)Engine.GetPhysicsInterpolationFraction();
        int frame = (int)(_time * 16) % 4;
        bool prod = J("production");

        foreach (var e in W.Entities)
            if (e is Belt b && view.HasPoint(Center(b.Pos))) DrawBelt(b, frame);

        foreach (var e in W.Entities)
        {
            if (e is not Belt b || !view.HasPoint(Center(b.Pos))) continue;
            var input = b.InputFrom(W);
            foreach (var it in b.Items)
            {
                var p = BeltPoint(b, input, Mathf.Lerp(it.Prev, it.Pos, f));
                DrawTexture(Sprites.Items[it.Id], Px(p - Vector2.One * 4));
            }
        }

        foreach (var e in W.Entities)
        {
            if (e is Belt || !view.Grow(T * 2).HasPoint(Center(e.Pos))) continue;
            var c = Center(e.Pos);
            var sq = Squash(e);
            // Powered machines fizz with tiny sparks, so you can see the bonus is on.
            if (prod && e.Working && W.PowerBoost(e) > 1.05f && GD.Randf() < _dt * 1.5f)
                _juice.Burst(c + new Vector2(GD.Randf() * 10 - 5, -6), Sprites.Spark, 1, 12, 1, -10, 0.4f);
            switch (e)
            {
                case Miner m:
                    if (prod && m.Working) sq *= new Vector2(1, 1 + 0.05f * Mathf.Sin(_time * 22));
                    Sprite(Sprites.Miner, c, Angle(m.Rot), sq);
                    var hub = c + new Vector2(-0.5f, -1.5f).Rotated(Angle(m.Rot));
                    float spin = m.Working ? _time * 12 : 0.4f;
                    DrawLine(hub - Vector2.FromAngle(spin) * 2.5f, hub + Vector2.FromAngle(spin) * 2.5f, Sprites.Glow, 1);
                    DrawLine(hub - Vector2.FromAngle(spin + 1.57f) * 2.5f, hub + Vector2.FromAngle(spin + 1.57f) * 2.5f, Sprites.Glow, 1);
                    break;
                case Furnace fu:
                    if (prod && fu.Working) sq *= new Vector2(1 + 0.03f * Mathf.Sin(_time * 9), 1 - 0.03f * Mathf.Sin(_time * 9));
                    Sprite(Sprites.Furnace, c, 0, sq);
                    if (fu.Working)
                    {
                        float flick = 0.75f + 0.25f * Mathf.Sin(_time * 23 + e.Pos.X);
                        DrawRect(new Rect2(c + new Vector2(-3, 0), new Vector2(6, 4)), Sprites.Glow with { A = flick });
                        DrawRect(new Rect2(c + new Vector2(-2, 1), new Vector2(4, 2)), Colors.White with { A = flick * 0.6f });
                        if (prod && GD.Randf() < _dt * 2.5f) _juice.Puff(c + new Vector2(0, -8), new Color(0.85f, 0.85f, 0.9f, 0.55f));
                    }
                    break;
                case Inserter ins:
                    Sprite(Sprites.InserterBase, c, 0, sq);
                    DrawArm(ins, c, f);
                    if (ins.Filter != null) DrawTextureRect(Sprites.Items[ins.Filter], new Rect2(c + new Vector2(2, 2), Vector2.One * 6), false);
                    break;
                case Assembler a when a is not Refinery:
                    if (prod && a.Working) sq *= new Vector2(1 + 0.04f * Mathf.Sin(_time * 14), 1 - 0.04f * Mathf.Sin(_time * 14));
                    Sprite(Sprites.Assembler, c, 0, sq);
                    if (a.Recipe is { } r)
                    {
                        float bob = a.Working && prod ? Mathf.Round(Mathf.Sin(_time * 10)) : 0;
                        DrawTexture(Sprites.Items[r.Out], (c + new Vector2(-4, -5 + bob)).Round());
                    }
                    break;
                case Crossing x:
                    DrawCrossing(x, c, f);
                    break;
                case Pole:
                    Sprite(Sprites.Pole, c, 0, sq);
                    break;
                case Splitter sp:
                    Sprite(Sprites.BeltFrames["belt"][frame], c, Angle(sp.Rot), Vector2.One);
                    var outDir = (Vector2)(sp.OutTile(sp.Next) - sp.Pos);
                    foreach (var it in sp.Lane)
                    {
                        float k = Mathf.Lerp(it.Prev, it.Pos, f);
                        var ip = k < 0.5f ? c - (Vector2)sp.Dir * (0.5f - k) * T : c + outDir * (k - 0.5f) * T;
                        DrawTexture(Sprites.Items[it.Id], Px(ip - Vector2.One * 4));
                    }
                    Sprite(Sprites.Splitter, c, Angle(sp.Rot), sq);
                    break;
                case Tunnel tu:
                    if (!tu.Exit) DrawTunnelItems(tu, f, false);
                    Sprite(Sprites.Tunnel, c, Angle(tu.Rot), sq * new Vector2(tu.Exit ? -1 : 1, 1));
                    break;
                case Pipe pi when pi.Type == "pipe_tunnel":
                    Sprite(Sprites.PipeTunnel, c, Angle(pi.Rot), sq * new Vector2(pi.Exit ? -1 : 1, 1));
                    break;
                case Pipe pi:
                    DrawPipe(pi, c);
                    break;
                case Pump pu:
                    Sprite(Sprites.Pump, c, Angle(pu.Rot), sq);
                    float stroke = pu.Working && prod ? Mathf.Round(Mathf.Sin(_time * 12) * 1.5f) : 0;
                    DrawRect(new Rect2((c + new Vector2(-2, -9 + stroke)).Round(), new Vector2(4, 3)), Sprites.Accent);
                    if (prod && pu.Working && GD.Randf() < _dt * 2) _juice.Burst(c + new Vector2(-1, 3), Defs.Items["water"].Color, 2, 20, 1, 30, 0.4f);
                    break;
                case Refinery rf:
                    if (prod && rf.Working) sq *= new Vector2(1 + 0.03f * Mathf.Sin(_time * 11), 1 - 0.03f * Mathf.Sin(_time * 11));
                    Sprite(Sprites.Refinery, c, Angle(rf.Rot), sq);
                    if (rf.Recipe is { } rr)
                    {
                        float bob = rf.Working && prod ? Mathf.Round(Mathf.Sin(_time * 10)) : 0;
                        DrawTexture(Sprites.Items[rr.Out], (c + new Vector2(-4, -3 + bob)).Round());
                        if (prod && rf.Working && GD.Randf() < _dt * 2) _juice.Puff(c + new Vector2(0, -9), Defs.Items[rr.Out].Color with { A = 0.6f });
                    }
                    break;
                case Generator g:
                    if (prod && g.Working) sq *= new Vector2(1 + 0.03f * Mathf.Sin(_time * 18), 1 - 0.03f * Mathf.Sin(_time * 18));
                    Sprite(Sprites.Generator, c, 0, sq);
                    var wheel = c + new Vector2(5, 1);
                    float turn = g.Working ? _time * 9 : 0.3f;
                    for (int k = 0; k < 3; k++) DrawLine(wheel, wheel + Vector2.FromAngle(turn + k * 2.094f) * 2.5f, Sprites.Glow, 1);
                    if (g.Working)
                    {
                        DrawRect(new Rect2(c + new Vector2(-4, 3), new Vector2(4, 2)), Sprites.Glow with { A = 0.7f + 0.3f * Mathf.Sin(_time * 21) });
                        if (prod && GD.Randf() < _dt * 3) _juice.Puff(c + new Vector2(-4, -9), new Color(0.35f, 0.33f, 0.36f, 0.6f));
                    }
                    // fuel gauge: one pixel per coal
                    for (int k = 0; k < g.Fuel; k++) DrawRect(new Rect2(c + new Vector2(-6 + k, 7), Vector2.One), Sprites.Ink);
                    break;
                case Chest ch:
                    Sprite(Sprites.Chest, c, 0, sq * (ch.Count > 0 && prod ? new Vector2(1, 1 + 0.02f * Mathf.Sin(_time * 3)) : Vector2.One));
                    break;
                case Hub:
                    var hc = e.Pos * T + Vector2.One * T * 1.5f;
                    float p = J("production") ? _hubPulse : 0;
                    Sprite(Sprites.Hub, hc + new Vector2(0, p * 2), 0, sq * new Vector2(1 + p * 0.08f, 1 - p * 0.06f));
                    DrawEyes(hc + new Vector2(0, p * 2));
                    break;
            }
        }

        if (Underground) DrawUnderground(view, f);

        foreach (var g in _gone) // removed buildings shrink and spin away
        {
            float k = (_time - g.T) / 0.25f;
            if (Sprites.Icons.TryGetValue(g.Type, out var tex)) Sprite(tex, g.At, k * 2, Vector2.One * (1 - k));
        }

        foreach (var net in W.Nets)
            foreach (var (a, b) in net.Wires)
                if (view.HasPoint(Center(a.Pos)) || view.HasPoint(Center(b.Pos))) DrawWire(PoleTop(a), PoleTop(b), net.Satisfaction > 0);

        if (S.StatOn("bottleneck"))
            foreach (var e in W.Entities)
                if (e.Status is { } st && view.HasPoint(Center(e.Pos))) DrawStatus(Center(e.Pos), st);

        _juice.Draw(this);
        DrawCursor();
    }

    // Items in an underground belt. On the surface only the bits next to the mouths show; below, the whole run.
    void DrawTunnelItems(Tunnel tu, float f, bool all)
    {
        var exit = W.Partner(tu);
        float len = exit == null ? 0 : tu.Length(exit);
        var start = Center(tu.Pos) - (Vector2)tu.Dir * T / 2;
        foreach (var it in tu.Lane)
        {
            float k = Mathf.Lerp(it.Prev, it.Pos, f);
            if (!all && k > 0.55f && k < len + 0.45f) continue;
            DrawTexture(Sprites.Items[it.Id], Px(start + (Vector2)tu.Dir * k * T - Vector2.One * 4));
        }
    }

    // Whether a pipe draws an arm toward tile t: another pipe that opens back, or a machine's fluid port.
    bool PipeJoins(Pipe p, Vector2I t) => p.Opens(t) && W.At(t) switch
    {
        Pipe q => q.Opens(p.Pos),
        Pump pu => pu.Front == p.Pos,
        Refinery r => r.Front == p.Pos || r.Back == p.Pos,
        Generator => true,
        _ => false,
    };

    void DrawPipe(Pipe p, Vector2 c)
    {
        foreach (var d in Entity.Dirs)
        {
            if (!PipeJoins(p, p.Pos + d)) continue;
            var arm = new Rect2(c + (Vector2)d * 4 - new Vector2(Mathf.Abs(d.Y) * 3 + Mathf.Abs(d.X) * 4, Mathf.Abs(d.X) * 3 + Mathf.Abs(d.Y) * 4), new Vector2(d.X != 0 ? 8 : 6, d.Y != 0 ? 8 : 6));
            DrawRect(arm, Sprites.Ink);
            DrawRect(arm.Grow(-1), Sprites.PipeBody);
        }
        var hub = new Rect2(c - Vector2.One * 4, Vector2.One * 8);
        DrawRect(hub, Sprites.Ink);
        DrawRect(hub.Grow(-1), Sprites.PipeBody.Lightened(0.1f));
        // a little window that fills with the fluid's colour
        var net = W.FluidOf(p);
        DrawRect(new Rect2(c - Vector2.One * 2, Vector2.One * 4), new Color("1c1a24"));
        if (net?.Fluid is { } fl && net.Amount > 0)
        {
            float fill = Mathf.Clamp(net.Amount / net.Cap, 0.15f, 1);
            DrawRect(new Rect2(c + new Vector2(-2, 2 - 4 * fill), new Vector2(4, 4 * fill)), Defs.Items[fl].Color);
        }
    }

    // Underground view: the surface goes dim and every underground run is drawn on top of it.
    void DrawUnderground(Rect2 view, float f)
    {
        DrawRect(view, new Color(0.08f, 0.06f, 0.12f, 0.62f));
        foreach (var e in W.Entities)
        {
            if (e.Exit || e.Type is not ("tunnel" or "pipe_tunnel") || W.Partner(e) is not { } exit) continue;
            Vector2 a = Center(e.Pos), b = Center(exit.Pos);
            if (!view.HasPoint(a) && !view.HasPoint(b)) continue;
            if (e is Tunnel tu)
            {
                DrawLine(a, b, Sprites.Ink, 10);
                DrawLine(a, b, new Color("3b3a46"), 6);
                DrawTunnelItems(tu, f, true);
            }
            else
            {
                var net = W.FluidOf((Pipe)e);
                DrawLine(a, b, Sprites.Ink, 8);
                DrawLine(a, b, net?.Fluid is { } fl ? Defs.Items[fl].Color : Sprites.PipeBody, 4);
            }
        }
        // the pieces themselves stay bright so you can see where each run starts and ends
        foreach (var e in W.Entities)
        {
            if (e.Type is not ("tunnel" or "pipe_tunnel") || !view.HasPoint(Center(e.Pos))) continue;
            var col = W.Partner(e) == null ? new Color("e8665a") : Sprites.Glow;
            DrawRect(new Rect2(e.Pos * T, Vector2.One * T).Grow(-0.5f), col, false, 1);
        }
    }

    static Vector2 PoleTop(Entity p) => Center(p.Pos) + new Vector2(0, -6);

    // A sagging wire, swaying a little. Live wires are copper coloured, dead ones dark.
    void DrawWire(Vector2 a, Vector2 b, bool live)
    {
        float sway = J("production") ? Mathf.Sin(_time * 1.3f + a.X * 0.05f) * 0.8f : 0;
        float sag = 3 + ((b - a).Length() / T) * 0.4f + sway;
        var col = live ? new Color("c9733e") : Sprites.Wire;
        var prev = a;
        for (int i = 1; i <= 8; i++)
        {
            float t = i / 8f;
            var p = a.Lerp(b, t) + new Vector2(0, sag * 4 * t * (1 - t));
            DrawLine(prev, p, col, 1);
            prev = p;
        }
    }

    // Poles the new pole would wire to: its 3 nearest within range (same rule as the simulation).
    IEnumerable<Pole> WireTargets(Vector2I at) => W.Entities.OfType<Pole>()
        .Where(p => p.Pos != at && ((Vector2)(p.Pos - at)).Length() <= Defs.WireRange)
        .OrderBy(p => (p.Pos - at).LengthSquared()).Take(3);

    void DrawPoleArea(Vector2I at, Color col)
    {
        var r = new Rect2((at - Vector2I.One * Defs.PoleReach) * T, Vector2.One * T * (Defs.PoleReach * 2 + 1));
        DrawRect(r, col with { A = 0.12f });
        DrawRect(r, col with { A = 0.6f }, false, 1);
    }

    // A little speech bubble: "..." waiting for input, "!" output blocked, "?" no recipe picked.
    void DrawStatus(Vector2 c, string status)
    {
        var (glyph, col) = status switch { "blocked" => ("!", new Color("e8665a")), "norecipe" => ("?", new Color("5aa9e8")), _ => ("...", new Color("f2c14e")) };
        float bob = J("production") ? Mathf.Round(Mathf.Sin(_time * 4 + c.X) * 1.5f) : 0;
        int w = glyph.Length * 4 + 3;
        var box = new Rect2((c + new Vector2(-w / 2f, -17 + bob)).Round(), new Vector2(w, 9));
        DrawRect(box.Grow(1), Sprites.Ink);
        DrawRect(box, new Color("fff7e8"));
        DrawRect(new Rect2(box.Position + new Vector2(w / 2f - 1, 9), new Vector2(2, 2)), Sprites.Ink);
        Sprites.DrawText(this, box.Position + new Vector2(2, 2), glyph, col);
    }

    // A small cheer in the middle of the view (tutorial step done).
    public void Cheer()
    {
        if (!J("production")) return;
        foreach (var col in new[] { "f2a65a", "f7c948", "62b86a", "5aa9e8" })
            _juice.Burst(Cam.Position, new Color(col), 6, 80, 2, 120, 1.1f);
    }

    // Milestone reached: confetti over the Hub, a big shake. The HUD adds the flash and slow motion.
    public void Celebrate()
    {
        _hub ??= W.Entities.Find(x => x is Hub) as Hub;
        var at = _hub != null ? _hub.Pos * T + new Vector2(24, 0) : Cam.Position;
        if (J("production"))
            foreach (var col in new[] { "f2a65a", "f7c948", "62b86a", "5aa9e8", "e8665a", "fff7e8" })
                _juice.Burst(at, new Color(col), 14, 110, 2, 140, 1.4f);
        if (J("screen")) _juice.Shake(0.6f);
        _hubPulse = 1;
    }

    void DrawBelt(Belt b, int frame)
    {
        var input = b.InputFrom(W);
        var c = Center(b.Pos);
        frame = (int)(_time * 160 * b.Speed) % 4; // faster tiers animate faster
        if (input == b.Back) Sprite(Sprites.BeltFrames[b.Type][frame], c, Angle(b.Rot), Vector2.One);
        else
        {
            bool fromRight = input == b.Pos + Entity.Dirs[(b.Rot + 1) % 4];
            Sprite(Sprites.BeltCurves[b.Type], c, Angle(b.Rot), new Vector2(1, fromRight ? 1 : -1));
        }
        // Belts joining from the side get a short piece of track, so the join reads as connected.
        foreach (var side in b.SideFeeds(W))
            Sprite(Sprites.Stubs[b.Type], c, Angle(Array.IndexOf(Entity.Dirs, b.Pos - side)), Vector2.One);
    }

    void DrawCrossing(Crossing x, Vector2 c, float f)
    {
        Sprite(Sprites.Crossing, c, 0, Squash(x));
        for (int d = 0; d < 4; d++)
            foreach (var it in x.Lanes[d])
            {
                var dir = (Vector2)Entity.Dirs[d];
                var p = c - dir * T / 2 + dir * T * Mathf.Lerp(it.Prev, it.Pos, f);
                DrawTexture(Sprites.Items[it.Id], Px(p - Vector2.One * 4));
            }
    }

    // Pos 0..0.5 runs from the input edge to the centre, 0.5..1 from the centre to the front edge.
    static Vector2 BeltPoint(Belt b, Vector2I input, float pos)
    {
        var c = Center(b.Pos);
        if (pos < 0.5f) return c - (Vector2)(b.Pos - input) * (0.5f - pos) * T;
        return c + (Vector2)b.Dir * (pos - 0.5f) * T;
    }

    void DrawArm(Inserter ins, Vector2 c, float f)
    {
        float arm = Mathf.Lerp(ins.PrevArm, ins.Arm, f);
        var d = (Vector2)ins.Dir;
        var up = new Vector2(d.Y, -d.X);
        float th = Mathf.Pi * (1 - arm);
        var end = c + (d * Mathf.Cos(th) + up * Mathf.Sin(th) * 0.5f) * 10;
        DrawLine(c, end, Sprites.Ink, 3);
        DrawLine(c, end, Sprites.Metal, 1);
        DrawRect(new Rect2((end - Vector2.One * 2).Round(), Vector2.One * 4), Sprites.Ink);
        if (ins.Held != null) DrawTexture(Sprites.Items[ins.Held], Px(end - Vector2.One * 4));
    }

    void DrawEyes(Vector2 hc)
    {
        var look = GetGlobalMousePosition();
        bool blink = _time % 4.3f < 0.12f;
        foreach (var off in new[] { new Vector2(-9.5f, -3), new Vector2(10.5f, -3) })
        {
            var eye = hc + off;
            if (blink) { DrawRect(new Rect2(eye - new Vector2(2.5f, 3), new Vector2(5, 6)), Sprites.Accent); DrawRect(new Rect2(eye + new Vector2(-2.5f, 1), new Vector2(5, 1)), Sprites.Ink); continue; }
            var pupil = eye + (look - eye).LimitLength(1) * 1.5f;
            DrawRect(new Rect2((pupil - new Vector2(1, 1.5f)).Round(), new Vector2(2, 3)), Sprites.Ink);
        }
    }

    void DrawCursor()
    {
        var c = Center(_hover);
        if (Selected != null)
        {
            bool ok = W.CanPlace(Selected, _hover, out _);
            float wob = J("place") ? 1 + 0.06f * Mathf.Sin(_time * 7) : 1;
            var tint = ok ? new Color(1, 1, 1, 0.6f) : new Color(1, 0.4f, 0.4f, 0.6f);
            float ang = Defs.IsBelt(Selected) || Selected is "miner" or "inserter" or "splitter" or "tunnel" or "pipe_tunnel" or "pump" or "refinery" ? Angle(Rot) : 0;
            // An underground piece that would pair shows as an exit, with a line back to its entrance.
            var entrance = W.EntranceFor(Selected, _hover, Rot);
            if (entrance != null) DrawLine(Center(entrance.Pos), c, Sprites.Glow with { A = 0.7f }, 2);
            Sprite(Sprites.Icons[Selected], c, ang, new Vector2(entrance != null ? -wob : wob, wob), tint);
            if (Selected == "pole") // where it powers and which poles it would wire to
            {
                DrawPoleArea(_hover, Sprites.Glow);
                foreach (var p in WireTargets(_hover)) DrawWire(c + new Vector2(0, -6), PoleTop(p), true);
            }
            if (Selected == "inserter") // show which way it hands items
            {
                var d = (Vector2)Entity.Dirs[Rot];
                DrawLine(c - d * 5, c + d * 5, Sprites.Glow, 1);
                DrawLine(c + d * 5, c + d * 2 + new Vector2(d.Y, -d.X) * 3, Sprites.Glow, 1);
                DrawLine(c + d * 5, c + d * 2 - new Vector2(d.Y, -d.X) * 3, Sprites.Glow, 1);
            }
            return;
        }
        var e = W.At(_hover);
        if (e is Pole hovered) DrawPoleArea(hovered.Pos, Sprites.Glow);
        var rect = e is Hub ? new Rect2(e.Pos * T, Vector2.One * T * 3) : new Rect2(_hover * T, Vector2.One * T);
        var col = e == null && W.OreAt(_hover) != null ? Sprites.Glow : Colors.White;
        DrawRect(rect.Grow(-0.5f), col with { A = 0.45f + 0.15f * Mathf.Sin(_time * 5) }, false, 1);
    }
}
