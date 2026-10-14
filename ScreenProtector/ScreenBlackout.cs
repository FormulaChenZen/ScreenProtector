using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace ScreenProtector;

internal sealed class ScreenBlackout : IDisposable
{
    internal const uint Continuous = 0x80000000;
    internal const uint KeepAwake = Continuous | 0x00000001 | 0x00000002;
    private readonly IBlackoutPlatform _platform;
    private readonly List<IBlackoutCover> _covers = new();
    private Point _initialCursor;
    private uint _previousExecutionState;
    private bool _awakeRequested;
    private bool _cursorHidden;
    private bool _disposed;

    public ScreenBlackout() : this(new BlackoutPlatform()) { }
    internal ScreenBlackout(IBlackoutPlatform platform) => _platform = platform;
    public bool IsActive { get; private set; }
    public event EventHandler? Closed;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsActive) return;
        IsActive = true;
        try
        {
            _initialCursor = _platform.CursorPosition;
            _previousExecutionState = _platform.SetExecutionState(KeepAwake);
            _awakeRequested = true;
            _platform.HideCursor();
            _cursorHidden = true;
            foreach (Rectangle bounds in _platform.ScreenBounds)
            {
                var cover = _platform.CreateCover(bounds);
                _covers.Add(cover);
                cover.Wake += OnWake;
                cover.MouseMoved += OnMouseMoved;
                cover.Closed += OnWake;
                cover.Show();
                if (!IsActive) break;
            }
        }
        catch (Exception startupError)
        {
            try { Close(); }
            catch (Exception cleanupError) { throw new AggregateException(startupError, cleanupError); }
            throw;
        }
    }

    public void Close()
    {
        if (!IsActive) return;
        IsActive = false;
        var errors = new List<Exception>();
        foreach (var cover in _covers)
        {
            cover.Wake -= OnWake;
            cover.MouseMoved -= OnMouseMoved;
            cover.Closed -= OnWake;
            try { cover.Dispose(); }
            catch (Exception error) { errors.Add(error); }
        }
        _covers.Clear();
        if (_cursorHidden)
        {
            _cursorHidden = false;
            try { _platform.ShowCursor(); }
            catch (Exception error) { errors.Add(error); }
        }
        if (_awakeRequested)
        {
            _awakeRequested = false;
            try { _platform.SetExecutionState(_previousExecutionState | Continuous); }
            catch (Exception error) { errors.Add(error); }
        }
        Closed?.Invoke(this, EventArgs.Empty);
        if (errors.Count > 0) throw new AggregateException("Unable to completely close the blackout.", errors);
    }

    private void OnWake(object? sender, EventArgs args) => Close();
    private void OnMouseMoved(object? sender, EventArgs args)
    {
        // Showing a window can emit MouseMove without any physical movement.
        if (_platform.CursorPosition != _initialCursor) Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}

internal interface IBlackoutCover : IDisposable
{
    event EventHandler? Wake;
    event EventHandler? MouseMoved;
    event EventHandler? Closed;
    void Show();
}

internal interface IBlackoutPlatform
{
    Point CursorPosition { get; }
    IEnumerable<Rectangle> ScreenBounds { get; }
    IBlackoutCover CreateCover(Rectangle bounds);
    uint SetExecutionState(uint state);
    void HideCursor();
    void ShowCursor();
}

internal sealed class BlackoutPlatform : IBlackoutPlatform
{
    public Point CursorPosition => Forms.Cursor.Position;
    public IEnumerable<Rectangle> ScreenBounds => Forms.Screen.AllScreens.Select(screen => screen.Bounds);
    public IBlackoutCover CreateCover(Rectangle bounds) => new BlackoutCover(bounds);
    public void HideCursor() => Forms.Cursor.Hide();
    public void ShowCursor() => Forms.Cursor.Show();
    public uint SetExecutionState(uint state)
    {
        uint previous = SetThreadExecutionState(state);
        if (previous == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to keep the display awake.");
        return previous;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint state);

    private sealed class BlackoutCover : Forms.Form, IBlackoutCover
    {
        public BlackoutCover(Rectangle bounds)
        {
            Text = "Screen Protector - Black Screen";
            AutoScaleMode = Forms.AutoScaleMode.None;
            FormBorderStyle = Forms.FormBorderStyle.None;
            BackColor = Color.Black;
            Opacity = 1;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = Forms.FormStartPosition.Manual;
            Bounds = bounds;
            KeyPreview = true;
            KeyDown += (_, _) => Wake?.Invoke(this, EventArgs.Empty);
            MouseDown += (_, _) => Wake?.Invoke(this, EventArgs.Empty);
            MouseMove += (_, _) => MouseMoved?.Invoke(this, EventArgs.Empty);
            FormClosed += (_, _) => CoverClosed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? Wake;
        public event EventHandler? MouseMoved;
        private event EventHandler? CoverClosed;
        event EventHandler? IBlackoutCover.Closed
        {
            add => CoverClosed += value;
            remove => CoverClosed -= value;
        }
    }
}
