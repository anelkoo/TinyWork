using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace TinyWorks.View;

// Retro sound effects synthesized at startup (sfxr style), so there are no audio files to license.
public sealed partial class Sfx : Node
{
    const int Rate = 22050;
    readonly Dictionary<string, AudioStreamWav> _sounds = new();
    readonly Dictionary<string, double> _lastPlayed = new();
    readonly AudioStreamPlayer[] _pool = new AudioStreamPlayer[10];
    readonly RandomNumberGenerator _rng = new();
    int _next;

    public float Volume = 0.8f; // 0..1, already includes the mini-mode reduction
    bool _muted;
    float _musicVolume;
    AudioStreamPlayer _music;

    public bool Muted { get => _muted; set { _muted = value; UpdateMusic(); } }
    public float MusicVolume { get => _musicVolume; set { _musicVolume = value; UpdateMusic(); } }
    // The player's own music: every OGG, MP3 and WAV in this folder, in name order, round and round.
    // Null, or a folder with nothing playable, means the built-in loop.
    public string MusicFolder
    {
        get => _musicFolder;
        set { if (value == _musicFolder) return; _musicFolder = value; _music?.QueueFree(); _music = null; UpdateMusic(); }
    }
    string _musicFolder;
    List<string> _tracks = new();
    int _track;
    public static readonly string[] MusicTypes = { ".ogg", ".mp3", ".wav" };

    public static List<string> Tracks(string folder) =>
        string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? new()
        : Directory.GetFiles(folder).Where(f => MusicTypes.Contains(Path.GetExtension(f).ToLowerInvariant())).Order().ToList();

    // The loop is built on first use, so a game started with music at 0 never spends time on it.
    void UpdateMusic()
    {
        if (!IsInsideTree()) return;
        bool on = !_muted && _musicVolume > 0;
        if (on && _music == null)
        {
            AddChild(_music = new AudioStreamPlayer());
            _tracks = Tracks(_musicFolder);
            _track = -1;
            if (_tracks.Count == 0) _music.Stream = MusicLoop();
            else { _music.Finished += NextTrack; NextTrack(); }
        }
        if (_music == null) return;
        _music.VolumeDb = Mathf.LinearToDb(Mathf.Max(_musicVolume, 0.0001f));
        if (on && !_music.Playing) _music.Play();
        _music.StreamPaused = !on;
    }

    // Tracks are loaded one at a time when they start, so a big folder costs nothing up front.
    void NextTrack()
    {
        for (int tries = 0; tries < _tracks.Count; tries++)
        {
            _track = (_track + 1) % _tracks.Count;
            var path = _tracks[_track];
            AudioStream s = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".ogg" => AudioStreamOggVorbis.LoadFromFile(path),
                ".mp3" => AudioStreamMP3.LoadFromFile(path),
                _ => AudioStreamWav.LoadFromFile(path),
            };
            if (s != null) { _music.Stream = s; _music.Play(); return; }
        }
        _tracks.Clear(); // nothing readable in the folder: back to the built-in loop
        _music.Finished -= NextTrack;
        _music.Stream = MusicLoop();
        _music.Play();
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        for (int i = 0; i < _pool.Length; i++) AddChild(_pool[i] = new AudioStreamPlayer());
        _sounds["place"] = Synth(220, 80, 0.13f, noise: 0.3f);
        _sounds["remove"] = Synth(320, 760, 0.09f);
        _sounds["mine"] = Synth(620, 380, 0.06f, noise: 0.6f);
        _sounds["deliver"] = Synth(900, 1350, 0.06f, vol: 0.18f);
        _sounds["craft"] = Synth(520, 560, 0.04f, vol: 0.12f);
        _sounds["rotate"] = Synth(420, 520, 0.04f, vol: 0.25f);
        _sounds["error"] = Synth(170, 120, 0.16f, vol: 0.3f);
        _sounds["click"] = Synth(760, 980, 0.03f, vol: 0.25f);
        _sounds["open"] = Synth(400, 800, 0.08f, vol: 0.22f);
        _sounds["coin"] = Notes(new[] { 1320f, 1760f }, 0.05f, 0.2f);
        _sounds["zap"] = Synth(1800, 300, 0.12f, noise: 0.55f, vol: 0.22f);
        _sounds["thunder"] = Synth(140, 40, 0.7f, noise: 0.85f, vol: 0.3f);
        _sounds["milestone"] = Notes(new[] { 523f, 659f, 784f, 1047f, 784f, 1047f }, 0.09f, 0.28f); // C-E-G-C jingle
        UpdateMusic();
    }

    // A calm 32 s loop made in code: soft sine pads over C, Am, F and G, a low root, and a few music-box notes
    // from the pentatonic scale. Everything wraps around the end, so the loop has no seam.
    static AudioStreamWav MusicLoop()
    {
        const float Bar = 4f;
        int n = (int)(Rate * Bar * 8);
        var buf = new float[n];
        float[][] chords = { new[] { 261.6f, 329.6f, 392f }, new[] { 220f, 261.6f, 329.6f }, new[] { 174.6f, 220f, 261.6f }, new[] { 196f, 246.9f, 293.7f } };
        float[] scale = { 523.3f, 587.3f, 659.3f, 784f, 880f, 1046.5f };
        var rng = new Random(7);
        int barLen = (int)(Rate * Bar), fade = (int)(Rate * 0.8f);
        for (int b = 0; b < 8; b++)
        {
            var chord = chords[b % 4];
            int start = b * barLen;
            for (int i = -fade; i < barLen + fade; i++) // pads overlap the next bar a little, wrapping at the end
            {
                float env = i < 0 ? (i + fade) / (float)fade * 0.5f : i > barLen ? (barLen + fade - i) / (float)fade * 0.5f : Mathf.Min(1, 0.5f + i / (float)fade);
                float t = (start + i) / (float)Rate, v = 0;
                foreach (var f in chord) v += Mathf.Sin(Mathf.Tau * f * t) * 0.05f;
                v += Mathf.Sin(Mathf.Tau * chord[0] / 2 * t) * 0.06f;
                buf[((start + i) % n + n) % n] += v * env;
            }
            for (int beat = 0; beat < 8; beat++) // up to eight half-beat notes per bar, about half of them played
            {
                if (rng.NextDouble() < 0.45) continue;
                float f = scale[rng.Next(scale.Length)];
                int at = start + beat * barLen / 8, len = (int)(Rate * 1.6f);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Rate, env = Mathf.Min(1, i / (Rate * 0.01f)) * Mathf.Exp(-t * 3f);
                    buf[(at + i) % n] += (Mathf.Sin(Mathf.Tau * f * t) + 0.25f * Mathf.Sin(Mathf.Tau * f * 2 * t)) * 0.07f * env;
                }
            }
        }
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            short v = (short)(Mathf.Clamp(buf[i], -1, 1) * short.MaxValue);
            data[i * 2] = (byte)v;
            data[i * 2 + 1] = (byte)(v >> 8);
        }
        return new AudioStreamWav { Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = n };
    }

    // Short square-wave melody, one note after another.
    static AudioStreamWav Notes(float[] freqs, float each, float vol)
    {
        var parts = new List<byte>();
        foreach (var f in freqs) parts.AddRange(Synth(f, f * 1.01f, each, vol: vol).Data);
        return new AudioStreamWav { Data = parts.ToArray(), Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false };
    }

    // minGap stops rapid repeats (a Hub taking 8 items/s) from turning into a buzz.
    public void Play(string name, float pitchVar = 0.08f, float minGap = 0.04f)
    {
        if (Muted || Volume <= 0 || !_sounds.TryGetValue(name, out var s)) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (_lastPlayed.TryGetValue(name, out var last) && now - last < minGap) return;
        _lastPlayed[name] = now;
        var p = _pool[_next++ % _pool.Length];
        p.Stream = s;
        p.PitchScale = 1 + _rng.RandfRange(-pitchVar, pitchVar);
        p.VolumeDb = Mathf.LinearToDb(Volume);
        p.Play();
    }

    static AudioStreamWav Synth(float f0, float f1, float dur, float noise = 0, float vol = 0.3f)
    {
        int n = (int)(Rate * dur);
        var data = new byte[n * 2];
        var rng = new Random(1);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            phase += Mathf.Lerp(f0, f1, t) / Rate;
            float sq = phase % 1 < 0.5 ? 1 : -1;
            float s = Mathf.Lerp(sq, (float)rng.NextDouble() * 2 - 1, noise);
            float env = Mathf.Min(1, i / (Rate * 0.004f)) * Mathf.Pow(1 - t, 2.2f); // quick attack, soft tail
            short v = (short)(s * env * vol * short.MaxValue);
            data[i * 2] = (byte)v;
            data[i * 2 + 1] = (byte)(v >> 8);
        }
        return new AudioStreamWav { Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false };
    }
}
