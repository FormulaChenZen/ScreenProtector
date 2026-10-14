using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScreenProtector;

internal enum SessionKind { Unavailable, Local, Remote }

internal sealed class LocalSessionGuard
{
    private readonly Func<SessionKind> querySession;
    private readonly Func<long> getTicks;
    private long localResumeTick;

    public LocalSessionGuard(Func<bool>? queryLocalSession = null, Func<long>? getTicks = null)
        : this(queryLocalSession == null ? QuerySession :
            () => queryLocalSession() ? SessionKind.Local : SessionKind.Unavailable, getTicks)
    {
    }

    public LocalSessionGuard(Func<SessionKind> querySession, Func<long>? getTicks = null)
    {
        this.querySession = querySession;
        this.getTicks = getTicks ?? (() => Environment.TickCount64);
    }

    public SessionKind Kind { get; private set; }
    public bool IsLocal => Kind == SessionKind.Local;
    public bool IsRemote => Kind == SessionKind.Remote;

    public bool Refresh()
    {
        SessionKind kind;
        try { kind = querySession(); }
        catch (Exception error)
        {
            Debug.WriteLine($"Local session query failed: {error}");
            kind = SessionKind.Unavailable;
        }

        if (kind == Kind) return false;
        if (kind == SessionKind.Local) localResumeTick = getTicks();
        Kind = kind;
        return true;
    }

    public int GetIdleSeconds(int actualIdleSeconds)
    {
        if (!IsLocal) return 0;
        // Remote-session input timestamps can be stale when the console resumes.
        long localSeconds = Math.Max(0, getTicks() - localResumeTick) / 1000;
        return (int)Math.Min(Math.Max(0, actualIdleSeconds), localSeconds);
    }

    public void RestartIdleCountdown()
    {
        if (IsLocal) localResumeTick = getTicks();
    }

    internal static bool IsLocalSession(ushort protocol, int state, int ownSessionId, uint activeConsoleSessionId) =>
        protocol == 0 && state == 0 && activeConsoleSessionId != uint.MaxValue &&
        ownSessionId >= 0 && (uint)ownSessionId == activeConsoleSessionId;

    internal static SessionKind ClassifySession(ushort protocol, int state, int ownSessionId,
        uint activeConsoleSessionId)
    {
        // WTSClientProtocolType 2 is RDP; WTSActive (0) confirms it is connected.
        if (protocol == 2 && state == 0) return SessionKind.Remote;
        return IsLocalSession(protocol, state, ownSessionId, activeConsoleSessionId)
            ? SessionKind.Local : SessionKind.Unavailable;
    }

    private static SessionKind QuerySession()
    {
        using var process = Process.GetCurrentProcess();
        uint activeConsoleSessionId = WTSGetActiveConsoleSessionId();
        IntPtr protocolBuffer = IntPtr.Zero;
        IntPtr stateBuffer = IntPtr.Zero;
        try
        {
            if (!WTSQuerySessionInformationW(IntPtr.Zero, -1, 16, out protocolBuffer, out int protocolBytes) ||
                protocolBuffer == IntPtr.Zero || protocolBytes < sizeof(ushort)) return SessionKind.Unavailable;
            if (!WTSQuerySessionInformationW(IntPtr.Zero, -1, 8, out stateBuffer, out int stateBytes) ||
                stateBuffer == IntPtr.Zero || stateBytes < sizeof(int)) return SessionKind.Unavailable;

            return ClassifySession((ushort)Marshal.ReadInt16(protocolBuffer), Marshal.ReadInt32(stateBuffer),
                process.SessionId, activeConsoleSessionId);
        }
        finally
        {
            if (protocolBuffer != IntPtr.Zero) WTSFreeMemory(protocolBuffer);
            if (stateBuffer != IntPtr.Zero) WTSFreeMemory(stateBuffer);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int informationClass,
        out IntPtr buffer, out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr buffer);
}
