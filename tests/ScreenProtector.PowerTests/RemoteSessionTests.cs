using ScreenProtector;

internal static class RemoteSessionTests
{
    public static readonly (string Name, Action Run)[] Tests =
    {
        ("Active RDP is detected when the process is outside the console", RemoteClassification),
        ("Disconnected and unknown sessions remain unavailable", UnavailableClassification),
        ("Local classification still requires the active console", LocalClassification),
        ("RDP startup reports a transition once", RemoteStartup),
        ("Remote and unavailable transitions remain distinct", SessionTransitions),
        ("Remote query errors clear remote state and recover", QueryFailure),
        ("Only entry into local session resets the idle clock", LocalIdleClock),
    };

    private static void RemoteClassification()
    {
        Equal(SessionKind.Remote, LocalSessionGuard.ClassifySession(2, 0, 2, 3));
        Equal(SessionKind.Remote, LocalSessionGuard.ClassifySession(2, 0, 2, uint.MaxValue));
        Equal(SessionKind.Remote, LocalSessionGuard.ClassifySession(2, 0, 2, 2));
    }

    private static void UnavailableClassification()
    {
        foreach (int state in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, -1 })
        {
            Equal(SessionKind.Unavailable, LocalSessionGuard.ClassifySession(2, state, 2, 3));
            Equal(SessionKind.Unavailable, LocalSessionGuard.ClassifySession(0, state, 2, 2));
        }
        Equal(SessionKind.Unavailable, LocalSessionGuard.ClassifySession(1, 0, 2, 2));
        Equal(SessionKind.Unavailable, LocalSessionGuard.ClassifySession(3, 0, 2, 2));
    }

    private static void LocalClassification()
    {
        Equal(SessionKind.Local, LocalSessionGuard.ClassifySession(0, 0, 2, 2));
        Equal(SessionKind.Unavailable, LocalSessionGuard.ClassifySession(0, 0, 2, 3));
        Equal(SessionKind.Unavailable, LocalSessionGuard.ClassifySession(0, 0, 2, uint.MaxValue));
        Equal(SessionKind.Unavailable, LocalSessionGuard.ClassifySession(0, 0, -1, uint.MaxValue));
    }

    private static void RemoteStartup()
    {
        var guard = new LocalSessionGuard(() => SessionKind.Remote);
        Equal(false, guard.IsRemote);
        Equal(true, guard.Refresh());
        Equal(true, guard.IsRemote);
        Equal(false, guard.IsLocal);
        Equal(false, guard.Refresh());
        Equal(0, guard.GetIdleSeconds(600));
    }

    private static void SessionTransitions()
    {
        SessionKind kind = SessionKind.Unavailable;
        var guard = new LocalSessionGuard(() => kind, () => 1000);
        Equal(false, guard.Refresh());
        foreach (SessionKind next in new[]
            { SessionKind.Remote, SessionKind.Unavailable, SessionKind.Local, SessionKind.Remote,
                SessionKind.Local, SessionKind.Unavailable })
        {
            kind = next;
            Equal(true, guard.Refresh());
            Equal(kind, guard.Kind);
            Equal(kind == SessionKind.Local, guard.IsLocal);
            Equal(kind == SessionKind.Remote, guard.IsRemote);
            Equal(false, guard.Refresh());
        }
    }

    private static void QueryFailure()
    {
        bool fail = false;
        var guard = new LocalSessionGuard(
            () => fail ? throw new IOException("Session unavailable") : SessionKind.Remote);
        Equal(true, guard.Refresh());
        fail = true;
        Equal(true, guard.Refresh());
        Equal(SessionKind.Unavailable, guard.Kind);
        Equal(false, guard.IsRemote);
        Equal(false, guard.IsLocal);
        Equal(false, guard.Refresh());
        fail = false;
        Equal(true, guard.Refresh());
        Equal(true, guard.IsRemote);
    }

    private static void LocalIdleClock()
    {
        SessionKind kind = SessionKind.Remote;
        long ticks = 1000;
        int clockReads = 0;
        var guard = new LocalSessionGuard(() => kind, () => { clockReads++; return ticks; });
        guard.Refresh();
        kind = SessionKind.Unavailable;
        guard.Refresh();
        Equal(0, clockReads);
        kind = SessionKind.Local;
        guard.Refresh();
        Equal(1, clockReads);
        ticks += 60000;
        Equal(false, guard.Refresh());
        Equal(1, clockReads);
        Equal(60, guard.GetIdleSeconds(600));
        kind = SessionKind.Remote;
        guard.Refresh();
        ticks += 600000;
        Equal(0, guard.GetIdleSeconds(600));
        kind = SessionKind.Local;
        guard.Refresh();
        Equal(0, guard.GetIdleSeconds(600));
        ticks += 30000;
        Equal(30, guard.GetIdleSeconds(600));
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}; got {actual}.");
    }
}
