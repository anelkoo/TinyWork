using System;
using System.Collections.Generic;
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
    public bool Muted;

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
        _sounds["milestone"] = Notes(new[] { 523f, 659f, 784f, 1047f, 784f, 1047f }, 0.09f, 0.28f); // C-E-G-C jingle
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
