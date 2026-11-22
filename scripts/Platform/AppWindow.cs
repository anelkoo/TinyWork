using Godot;

namespace TinyWorks.Platform;

public enum WindowMode { Normal, Mini }
public enum ScreenCorner { TopLeft, TopRight, BottomLeft, BottomRight }

// Owns the OS window: normal vs. mini mode, hide/show, mini opacity, mini hover and mini edge-resize.
// The simulation keeps ticking while hidden; only rendering stops.
public sealed partial class AppWindow : Node
{
    [Signal] public delegate void ChangedEventHandler();

    public WindowMode Mode { get; private set; } = WindowMode.Normal;
    public bool Hidden { get; private set; }
    public ScreenCorner MiniCorner = ScreenCorner.BottomRight;
    public int MiniMargin = 16;
    public Vector2I MiniSize = new(400, 300); // idle size; the window itself is MiniSize * HoverScale
    public int FpsFocused = 60, FpsBackground = 60;

    // Opacity fades one composited texture, so overlapping sprites don't stack their alpha.
    public Control Faded;
    float _miniOpacity = 0.85f;
    public float MiniOpacity { get => _miniOpacity; set { _miniOpacity = Mathf.Clamp(value, 0.2f, 1f); ApplyOpacity(); } }
    public float MiniIdleOpacity = 0.5f, HoverScale = 1.25f; // mini window: faded when the mouse is away, bigger when it's over it

    // Hover grows the mini window without resizing the OS window (that is slow and choppy): the window always has
    // its hovered size, and while idle the picture is drawn scaled down into the anchored corner. The empty rest
    // is transparent and lets clicks through to whatever is behind it.
    float _hover, _hoverTarget;    // 0 = idle, 1 = hovered; moves linearly, eased when applied
    const float HoverTime = 0.12f; // seconds for a whole grow or shrink
    float Ease => 1 - Mathf.Pow(1 - _hover, 3);
    float ContentScale => Mathf.Lerp(1 / HoverScale, 1, Ease);
    Vector2I BigSize => (Vector2I)((Vector2)MiniSize * HoverScale).Round();
    bool AnchorRight => MiniCorner is ScreenCorner.TopRight or ScreenCorner.BottomRight;
    bool AnchorBottom => MiniCorner is ScreenCorner.BottomLeft or ScreenCorner.BottomRight;

    Rect2I? _normalRect;

    // The normal window's position and size, also while in mini mode (for saving to settings).
    public Rect2I NormalRect => Mode == WindowMode.Normal ? new Rect2I(W.Position, W.Size) : _normalRect ?? new Rect2I(W.Position, W.Size);

    public void RestoreNormal(Rect2I r)
    {
        W.Size = r.Size;
        W.Position = r.Position;
    }
    bool _focused = true, _switching, _mouseInside;
    Window W => GetWindow();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // Remember the mini size only when the user resizes it, not while this class resizes the window.
        // The window is always the hovered size, so store what it means at idle scale.
        W.SizeChanged += () => { if (Mode == WindowMode.Mini && !Hidden && !_switching) MiniSize = (Vector2I)((Vector2)W.Size / HoverScale).Round(); };
        // Transparency is switched on in the project settings (Windows can't add it to a window later);
        // the normal window just paints an opaque background.
        W.TransparentBg = false;
        // The window's own enter/leave events, not the global cursor position: under XWayland (most Linux
        // desktops now) an X11 app stops seeing the cursor once it leaves its window, so the last position
        // inside stays frozen and the mini window would never shrink again.
        W.MouseEntered += () => _mouseInside = true;
        W.MouseExited += () => _mouseInside = false;
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
            W.TransparentBg = true;
            W.AlwaysOnTop = true;
            _hover = _hoverTarget = 0;
            W.Size = BigSize;
            W.Position = AnchoredPosition(screen, BigSize);
        }
        else
        {
            W.AlwaysOnTop = false;
            W.TransparentBg = false;
            W.Borderless = false;
            SetPassthrough(false);
            if (_normalRect is { } r) { W.Size = r.Size; W.Position = r.Position; }
        }
        Mode = m;
        _switching = false;
        ApplyOpacity();
        ApplyHover();
        EmitSignal(SignalName.Changed);
    }

    // Mini window hover: grow from the anchored corner and fade in while the mouse is over it.
    // Works while another app has focus too: enter/leave events arrive for unfocused windows.
    public override void _Process(double delta)
    {
        if (Mode != WindowMode.Mini || Hidden) return;
        var mouse = W.GetMousePosition(); // window pixels; only trusted while the mouse is inside
        // A held button keeps it big only while the mouse is still inside: after a window-manager resize drag
        // the release often never reaches the game, and a "still pressed" button would keep it big forever.
        bool over = _mouseInside && ((_hoverTarget > 0 ? new Rect2(Vector2.Zero, W.Size) : VisibleRect(1 / HoverScale)).HasPoint(mouse)
                                     || _hoverTarget > 0 && Input.IsMouseButtonPressed(MouseButton.Left));
        float target = over ? 1 : 0;
        if (target != _hoverTarget)
        {
            _hoverTarget = target;
            if (target > 0) SetPassthrough(false); // the whole window takes the mouse while it is big
        }
        if (_hover == _hoverTarget) return;
        _hover = Mathf.MoveToward(_hover, _hoverTarget, (float)delta / HoverTime);
        ApplyOpacity();
        ApplyHover();
    }

    // The part of the window the picture covers at a given scale, in window pixels.
    Rect2 VisibleRect(float scale)
    {
        var size = (Vector2)W.Size * scale;
        return new Rect2(new Vector2(AnchorRight ? W.Size.X - size.X : 0, AnchorBottom ? W.Size.Y - size.Y : 0), size);
    }

    void ApplyHover()
    {
        if (Faded == null) return;
        bool mini = Mode == WindowMode.Mini;
        Faded.PivotOffset = new Vector2(AnchorRight ? Faded.Size.X : 0, AnchorBottom ? Faded.Size.Y : 0);
        Faded.Scale = Vector2.One * (mini ? ContentScale : 1);
        if (mini && _hover == 0) SetPassthrough(true); // shrunk: only the small picture takes the mouse
    }

    void SetPassthrough(bool idle)
    {
        if (!idle || HoverScale <= 1.001f) { DisplayServer.WindowSetMousePassthrough(System.Array.Empty<Vector2>()); return; }
        var r = VisibleRect(1 / HoverScale);
        DisplayServer.WindowSetMousePassthrough(new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) });
    }

    // Settings changed the corner, size or hover size: resize and move the mini window right away.
    public void Reanchor()
    {
        if (Mode != WindowMode.Mini || Hidden) return;
        _switching = true;
        W.Size = BigSize;
        W.Position = AnchoredPosition(W.CurrentScreen, BigSize);
        _switching = false;
        ApplyHover();
    }

    Vector2I AnchoredPosition(int screen, Vector2I size)
    {
        var u = DisplayServer.ScreenGetUsableRect(screen);
        int x = AnchorRight ? u.End.X - size.X - MiniMargin : u.Position.X + MiniMargin;
        int y = AnchorBottom ? u.End.Y - size.Y - MiniMargin : u.Position.Y + MiniMargin;
        return new Vector2I(x, y);
    }

    // Borderless mini window: grab one of the edges facing away from the anchored corner to resize.
    // A wide grip and a resize cursor, so you don't have to hit a hairline.
    DisplayServer.WindowResizeEdge? EdgeAt(Vector2 pos)
    {
        const int grip = 16;
        var p = (Vector2I)pos;
        var s = W.Size;
        bool anchorRight = AnchorRight, anchorBottom = AnchorBottom;
        bool h = anchorRight ? p.X < grip : p.X >= s.X - grip, v = anchorBottom ? p.Y < grip : p.Y >= s.Y - grip;
        if (!h && !v) return null;
        return (h, v, anchorRight, anchorBottom) switch
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
    }

    bool _resizeCursor;

    public override void _Input(InputEvent e)
    {
        if (Mode != WindowMode.Mini)
        {
            if (_resizeCursor) { _resizeCursor = false; Input.SetDefaultCursorShape(); }
            return;
        }
        if (e is InputEventMouseMotion mm)
        {
            var hover = _hover > 0 ? EdgeAt(mm.Position) : null; // only once the window has grown to full size
            _resizeCursor = hover != null;
            Input.SetDefaultCursorShape(hover switch
            {
                DisplayServer.WindowResizeEdge.Left or DisplayServer.WindowResizeEdge.Right => Input.CursorShape.Hsize,
                DisplayServer.WindowResizeEdge.Top or DisplayServer.WindowResizeEdge.Bottom => Input.CursorShape.Vsize,
                DisplayServer.WindowResizeEdge.TopLeft or DisplayServer.WindowResizeEdge.BottomRight => Input.CursorShape.Fdiagsize,
                DisplayServer.WindowResizeEdge.TopRight or DisplayServer.WindowResizeEdge.BottomLeft => Input.CursorShape.Bdiagsize,
                _ => Input.CursorShape.Arrow,
            });
            return;
        }
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb || EdgeAt(mb.Position) is not { } edge) return;
        GetViewport().SetInputAsHandled();
        W.StartResize(edge);
    }

    void ApplyOpacity()
    {
        if (Faded == null) return;
        var c = Faded.Modulate;
        c.A = Mode == WindowMode.Mini ? Mathf.Lerp(Mathf.Min(MiniIdleOpacity, _miniOpacity), _miniOpacity, Ease) : 1f;
        Faded.Modulate = c;
    }

    public void ApplyFps() => Engine.MaxFps = Hidden ? Engine.PhysicsTicksPerSecond : _focused && Mode == WindowMode.Normal ? FpsFocused : FpsBackground;
}
