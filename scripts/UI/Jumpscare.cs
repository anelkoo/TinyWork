using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using TinyWorks.View;

namespace TinyWorks.UI;

// A jumpscare in its own borderless, transparent, click-through window over the whole screen.
// It plays the player's own frames and sound from a folder they pick (frames at 30 fps, in number order),
// or, without one, a pale face drawn in code with a screech.
public sealed partial class Jumpscare : Control
{
    const float Fps = 30, FaceTime = 1.4f;
    static readonly string[] ImageTypes = { ".png", ".jpg", ".jpeg", ".webp" };
    static string _loaded; // the folder the cache below belongs to
    static List<Texture2D> _frames = new();
    static AudioStream _sound;

    float _t;
    readonly RandomNumberGenerator _rng = new();
    TextureRect _pic;

    // Frames sorted by the number in their name, so frame-2 comes before frame-10.
    public static List<string> FrameFiles(string folder) =>
        string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? new()
        : Directory.GetFiles(folder).Where(f => ImageTypes.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => int.TryParse(Regex.Match(Path.GetFileName(f), @"\d+").Value, out var n) ? n : 0).ThenBy(f => f).ToList();

    public static string SoundFile(string folder) =>
        string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? null
        : Directory.GetFiles(folder).Order().FirstOrDefault(f => Sfx.MusicTypes.Contains(Path.GetExtension(f).ToLowerInvariant()));

    static void Load(string folder)
    {
        if (folder == _loaded) return;
        _loaded = folder;
        _frames = FrameFiles(folder).Select(Image.LoadFromFile).Where(i => i != null).Select(i => (Texture2D)ImageTexture.CreateFromImage(i)).ToList();
        var snd = SoundFile(folder);
        _sound = snd == null ? null : Path.GetExtension(snd).ToLowerInvariant() switch
        {
            ".ogg" => AudioStreamOggVorbis.LoadFromFile(snd),
            ".mp3" => AudioStreamMP3.LoadFromFile(snd),
            _ => AudioStreamWav.LoadFromFile(snd),
        };
    }

    public static void Show(Sfx sfx, string folder)
    {
        var root = sfx.GetTree().Root;
        if (FindIn(root)) return; // one at a time
        Load(folder);
        int screen = DisplayServer.WindowGetCurrentScreen();
        var win = new Window
        {
            Visible = false, Title = "Jumpscare", Borderless = true, Transparent = true, TransparentBg = true, AlwaysOnTop = true, Unfocusable = true,
            Unresizable = true, MousePassthrough = true, ForceNative = true, ProcessMode = ProcessModeEnum.Always,
            Position = DisplayServer.ScreenGetPosition(screen), Size = DisplayServer.ScreenGetSize(screen),
        };
        var s = new Jumpscare { MouseFilter = MouseFilterEnum.Ignore };
        s.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        win.AddChild(s);
        if (_frames.Count > 0)
        {
            s._pic = new TextureRect { Texture = _frames[0], ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
            s._pic.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            s.AddChild(s._pic);
        }
        root.AddChild(win);
        win.Show();
        if (_sound == null) sfx.Play("scare", 0, 0);
        else if (!sfx.Muted && sfx.Volume > 0)
        {
            // On the Sfx node, not the window, so the sound plays to its end after the last frame.
            var p = new AudioStreamPlayer { Stream = _sound, VolumeDb = Mathf.LinearToDb(sfx.Volume) };
            sfx.AddChild(p);
            p.Finished += p.QueueFree;
            p.Play();
        }
    }

    static bool FindIn(Node root) => root.GetChildren().OfType<Window>().Any(w => w.GetChildCount() > 0 && w.GetChild(0) is Jumpscare);

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_pic != null)
        {
            int f = (int)(_t * Fps);
            if (f >= _frames.Count) { GetParent().QueueFree(); return; }
            _pic.Texture = _frames[f];
            return;
        }
        if (_t > FaceTime) { GetParent().QueueFree(); return; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_pic != null) return;
        float a = Mathf.Clamp((FaceTime - _t) / 0.35f, 0, 1); // fades out over the last 0.35 s
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
