using Godot;

namespace TinyWorks.Platform;

public enum WindowMode { Normal, Mini }
public enum ScreenCorner { TopLeft, TopRight, BottomLeft, BottomRight }

// Owns the OS window: normal vs. mini mode, hide/show, mini opacity and mini edge-resize.
// The simulation keeps ticking while hidden; only rendering stops.
public sealed partial class AppWindow : Node
{
    [Signal] public delegate void ChangedEventHandler();

    public WindowMode Mode { get; private set; } = WindowMode.Normal;
    public bool Hidden { get; private set; }
    public ScreenCorner MiniCorner = ScreenCorner.BottomRight;
    public int MiniMargin = 16;
    public Vector2I MiniSize = new(400, 300);
    public int FpsFocused = 60, FpsBackground = 60;

    // Opacity fades one composited texture, so overlapping sprites don't stack their alpha.
    public Control Faded;
    float _miniOpacity = 0.85f;
    public float MiniOpacity { get => _miniOpacity; set { _miniOpacity = Mathf.Clamp(value, 0.2f, 1f); ApplyOpacity(); } }

    Rect2I? _normalRect;

    // The normal window's position and size, also while in mini mode (for saving to settings).
    public Rect2I NormalRect => Mode == WindowMode.Normal ? new Rect2I(W.Position, W.Size) : _normalRect ?? new Rect2I(W.Position, W.Size);

    public void RestoreNormal(Rect2I r)
    {
        W.Size = r.Size;
        W.Position = r.Position;
    }
    bool _focused = true, _switching;
    Window W => GetWindow();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // Remember the mini size only when the user resizes it, not while SetMode itself resizes the window.
        W.SizeChanged += () => { if (Mode == WindowMode.Mini && !Hidden && !_switching) MiniSize = W.Size; };
        ApplyFps();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn) { _focused = true; ApplyFps(); }
        else if (what == NotificationApplicationFocusOut) { _focused = false; ApplyFps(); }
    }

    public void ToggleHidden()
    {
        Hidden = !Hidden;
        RenderingServer.RenderLoopEnabled = !Hidden;
        // Godot refuses to hide its main window, so hide it natively (also drops the taskbar entry).
        // shortcut: Linux minimizes instead of hiding, upgrade to XUnmapWindow if the taskbar entry bothers.
        if (OS.GetName() == "Windows")
            ShowWindow((nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle), Hidden ? 0 : 5);
        else
            W.Mode = Hidden ? Window.ModeEnum.Minimized : Window.ModeEnum.Windowed;
        if (!Hidden)
        {
            if (Mode == WindowMode.Mini) W.AlwaysOnTop = true; // some WMs drop it across a hide
            W.GrabFocus();
        }
        ApplyFps();
        EmitSignal(SignalName.Changed);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(nint hWnd, int nCmdShow);

    public void ToggleMode() => SetMode(Mode == WindowMode.Normal ? WindowMode.Mini : WindowMode.Normal);

    public void SetMode(WindowMode m)
    {
        if (Hidden) ToggleHidden();
        if (m == Mode) return;
        _switching = true;
        if (m == WindowMode.Mini)
        {
            _normalRect = new Rect2I(W.Position, W.Size);
            int screen = W.CurrentScreen; // anchor to the monitor the window is on
            W.Mode = Window.ModeEnum.Windowed;
            W.Borderless = true;
            W.Transparent = true;
            W.TransparentBg = true;
            W.AlwaysOnTop = true;
            W.Size = MiniSize;
            W.Position = AnchoredPosition(screen, MiniSize);
        }
        else
        {
            W.AlwaysOnTop = false;
            W.TransparentBg = false;
            W.Transparent = false;
            W.Borderless = false;
            if (_normalRect is { } r) { W.Size = r.Size; W.Position = r.Position; }
        }
        Mode = m;
        _switching = false;
        ApplyOpacity();
        EmitSignal(SignalName.Changed);
    }

    // Settings changed the corner or size: move the mini window there right away.
    public void Reanchor()
    {
        if (Mode != WindowMode.Mini || Hidden) return;
        W.Position = AnchoredPosition(W.CurrentScreen, W.Size);
    }

    Vector2I AnchoredPosition(int screen, Vector2I size)
    {
        var u = DisplayServer.ScreenGetUsableRect(screen);
        int x = MiniCorner is ScreenCorner.TopLeft or ScreenCorner.BottomLeft ? u.Position.X + MiniMargin : u.End.X - size.X - MiniMargin;
        int y = MiniCorner is ScreenCorner.TopLeft or ScreenCorner.TopRight ? u.Position.Y + MiniMargin : u.End.Y - size.Y - MiniMargin;
        return new Vector2I(x, y);
    }

    // Borderless mini window: grab one of the edges facing away from the anchored corner to resize.
    public override void _Input(InputEvent e)
    {
        if (Mode != WindowMode.Mini || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
        const int grip = 6;
        var p = (Vector2I)mb.Position;
        var s = W.Size;
        bool l = p.X < grip, r = p.X >= s.X - grip, t = p.Y < grip, b = p.Y >= s.Y - grip;
        bool anchorRight = MiniCorner is ScreenCorner.TopRight or ScreenCorner.BottomRight;
        bool anchorBottom = MiniCorner is ScreenCorner.BottomLeft or ScreenCorner.BottomRight;
        bool h = anchorRight ? l : r, v = anchorBottom ? t : b;
        if (!h && !v) return;
        var edge = (h, v, anchorRight, anchorBottom) switch
        {
            (true, true, true, true) => DisplayServer.WindowResizeEdge.TopLeft,
            (true, true, true, false) => DisplayServer.WindowResizeEdge.BottomLeft,
            (true, true, false, true) => DisplayServer.WindowResizeEdge.TopRight,
            (true, true, false, false) => DisplayServer.WindowResizeEdge.BottomRight,
            (true, false, true, _) => DisplayServer.WindowResizeEdge.Left,
            (true, false, false, _) => DisplayServer.WindowResizeEdge.Right,
            (false, true, _, true) => DisplayServer.WindowResizeEdge.Top,
            _ => DisplayServer.WindowResizeEdge.Bottom,
        };
        GetViewport().SetInputAsHandled();
        W.StartResize(edge);
    }

    void ApplyOpacity()
    {
        if (Faded == null) return;
        var c = Faded.Modulate;
        c.A = Mode == WindowMode.Mini ? _miniOpacity : 1f;
        Faded.Modulate = c;
    }

    public void ApplyFps() => Engine.MaxFps = Hidden ? Engine.PhysicsTicksPerSecond : _focused && Mode == WindowMode.Normal ? FpsFocused : FpsBackground;
}
