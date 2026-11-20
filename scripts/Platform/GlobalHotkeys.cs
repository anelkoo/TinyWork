using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;

namespace TinyWorks.Platform;

public readonly record struct Combo(Key Key, bool Ctrl = false, bool Shift = false, bool Alt = false);

// Hotkeys that work while the window is unfocused: a background thread polls the OS key state
// (GetAsyncKeyState on Windows, XQueryKeymap on X11), the same approach as InputLib.
// When polling is available it is the only source, so a focused window never sees a key twice.
// Elsewhere (Wayland) it falls back to normal Godot input, which only works while focused.
public sealed partial class GlobalHotkeys : Node
{
    [Signal] public delegate void PressedEventHandler(string id);

    public bool Global => _backend != null;
    public bool Paused { get; set; } // true while a rebind picker is listening

    // The keys both pollers can map (Windows virtual keys and X11 keysyms).
    public static bool CanWatch(Key k) => k is >= Key.A and <= Key.Z or >= Key.Key0 and <= Key.Key9 or >= Key.F1 and <= Key.F24;

    volatile KeyValuePair<string, Combo>[] _binds = Array.Empty<KeyValuePair<string, Combo>>();
    readonly ConcurrentQueue<string> _fired = new();
    IKeyState _backend;
    Thread _thread;
    volatile bool _run;

    public void Bind(string id, Combo combo)
    {
        var d = new Dictionary<string, Combo>();
        foreach (var kv in _binds) d[kv.Key] = kv.Value;
        d[id] = combo;
        _binds = new List<KeyValuePair<string, Combo>>(d).ToArray(); // swap a snapshot, the poller only reads
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            if (OS.GetName() == "Windows") _backend = new WinKeys();
            else if (DisplayServer.GetName() == "X11") _backend = X11Keys.Open();
        }
        catch (Exception e) { GD.PushWarning($"Global hotkeys unavailable: {e.Message}"); _backend = null; }

        if (_backend == null) { GD.Print("hotkeys: focus-only fallback"); return; }
        _run = true;
        _thread = new Thread(Poll) { IsBackground = true, Name = "GlobalHotkeys" };
        _thread.Start();
        GD.Print("hotkeys: global polling");
    }

    public override void _ExitTree()
    {
        _run = false;
        _thread?.Join(200);
        (_backend as IDisposable)?.Dispose();
    }

    void Poll()
    {
        var wasDown = new HashSet<string>();
        while (_run)
        {
            _backend.Refresh();
            foreach (var (id, c) in _binds)
            {
                bool down = _backend.IsDown(c.Key)
                    && (!c.Ctrl || _backend.IsDown(Key.Ctrl))
                    && (!c.Shift || _backend.IsDown(Key.Shift))
                    && (!c.Alt || _backend.IsDown(Key.Alt));
                if (down && wasDown.Add(id) && !Paused) _fired.Enqueue(id); // fire on the press edge only
                else if (!down) wasDown.Remove(id);
            }
            Thread.Sleep(30);
        }
    }

    public override void _Process(double delta)
    {
        while (_fired.TryDequeue(out var id)) EmitSignal(SignalName.Pressed, id);
    }

    public override void _Input(InputEvent e)
    {
        if (Global || Paused || e is not InputEventKey { Pressed: true, Echo: false } k) return;
        foreach (var (id, c) in _binds)
            if (k.Keycode == c.Key && k.CtrlPressed == c.Ctrl && k.ShiftPressed == c.Shift && k.AltPressed == c.Alt)
            {
                GetViewport().SetInputAsHandled();
                EmitSignal(SignalName.Pressed, id);
            }
    }

    interface IKeyState { void Refresh(); bool IsDown(Key k); }

    sealed class WinKeys : IKeyState
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

        public void Refresh() { }
        public bool IsDown(Key k) { int vk = Vk(k); return vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0; }

        static int Vk(Key k)
        {
            if (k is >= Key.A and <= Key.Z or >= Key.Key0 and <= Key.Key9) return (int)k; // same ASCII codes
            if (k is >= Key.F1 and <= Key.F24) return 0x70 + (int)(k - Key.F1);
            return k switch { Key.Ctrl => 0x11, Key.Shift => 0x10, Key.Alt => 0x12, _ => 0 };
        }
    }

    sealed class X11Keys : IKeyState, IDisposable
    {
        const string Lib = "libX11.so.6";
        [DllImport(Lib)] static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(Lib)] static extern int XCloseDisplay(IntPtr d);
        [DllImport(Lib)] static extern int XQueryKeymap(IntPtr d, byte[] keys);
        [DllImport(Lib)] static extern byte XKeysymToKeycode(IntPtr d, UIntPtr keysym);

        readonly IntPtr _d;
        readonly byte[] _keys = new byte[32];
        readonly Dictionary<Key, byte[]> _codes = new();

        X11Keys(IntPtr d) => _d = d;
        public static X11Keys Open() { var d = XOpenDisplay(IntPtr.Zero); return d == IntPtr.Zero ? null : new X11Keys(d); }
        public void Dispose() => XCloseDisplay(_d);
        public void Refresh() => XQueryKeymap(_d, _keys);

        public bool IsDown(Key k)
        {
            if (!_codes.TryGetValue(k, out var codes))
            {
                var list = new List<byte>();
                foreach (var sym in Keysyms(k)) { var c = XKeysymToKeycode(_d, (UIntPtr)sym); if (c != 0) list.Add(c); }
                _codes[k] = codes = list.ToArray();
            }
            foreach (var c in codes) if ((_keys[c >> 3] & (1 << (c & 7))) != 0) return true;
            return false;
        }

        static uint[] Keysyms(Key k)
        {
            if (k is >= Key.A and <= Key.Z) return new[] { (uint)k + 32 }; // keysyms are lowercase ASCII
            if (k is >= Key.Key0 and <= Key.Key9) return new[] { (uint)k };
            if (k is >= Key.F1 and <= Key.F35) return new[] { 0xFFBEu + (uint)(k - Key.F1) };
            return k switch
            {
                Key.Ctrl => new[] { 0xFFE3u, 0xFFE4u },
                Key.Shift => new[] { 0xFFE1u, 0xFFE2u },
                Key.Alt => new[] { 0xFFE9u, 0xFFEAu },
                _ => Array.Empty<uint>(),
            };
        }
    }
}
