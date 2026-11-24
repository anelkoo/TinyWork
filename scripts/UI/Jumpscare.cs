using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using TinyWorks.View;

namespace TinyWorks.UI;

// A jumpscare in its own borderless, transparent, click-through window over the whole screen.
// It plays the frames (30 fps, in number order) and the sound from the FramesAndSound folder: the project folder
// while developing, next to the executable in a build. The folder is never committed or exported.
public sealed partial class Jumpscare : Control
{
    const float Fps = 30;
    static readonly string[] ImageTypes = { ".png", ".jpg", ".jpeg", ".webp" };
    static string _loaded; // the folder the cache below belongs to
    static List<Texture2D> _frames = new();
    static AudioStream _sound;

    float _t;
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

    static string Folder => OS.HasFeature("editor") ? ProjectSettings.GlobalizePath("res://FramesAndSound")
        : Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()), "FramesAndSound");

    public static void Show(Sfx sfx, string folder = null) // folder: only the UI test passes its own
    {
        folder ??= Folder;
        var root = sfx.GetTree().Root;
        if (root.GetChildren().OfType<Window>().Any(w => w.GetChildCount() > 0 && w.GetChild(0) is Jumpscare)) return; // one at a time
        Load(folder);
        if (_frames.Count == 0) return;
        int screen = DisplayServer.WindowGetCurrentScreen();
        var win = new Window
        {
            Visible = false, // force_native can only change while hidden
            Title = "Jumpscare", Borderless = true, Transparent = true, TransparentBg = true, AlwaysOnTop = true, Unfocusable = true,
            Unresizable = true, MousePassthrough = true, ForceNative = true, ProcessMode = ProcessModeEnum.Always,
            Position = DisplayServer.ScreenGetPosition(screen), Size = DisplayServer.ScreenGetSize(screen),
        };
        var s = new Jumpscare { MouseFilter = MouseFilterEnum.Ignore };
        s.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        s._pic = new TextureRect { Texture = _frames[0], ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
        s._pic.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        s.AddChild(s._pic);
        win.AddChild(s);
        root.AddChild(win);
        win.Show();
        if (_sound != null && !sfx.Muted && sfx.Volume > 0)
        {
            // On the Sfx node, not the window, so the sound plays to its end after the last frame.
            var p = new AudioStreamPlayer { Stream = _sound, VolumeDb = Mathf.LinearToDb(sfx.Volume) };
            sfx.AddChild(p);
            p.Finished += p.QueueFree;
            p.Play();
        }
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        int f = (int)(_t * Fps);
        if (f >= _frames.Count) { GetParent().QueueFree(); return; }
        _pic.Texture = _frames[f];
    }
}
