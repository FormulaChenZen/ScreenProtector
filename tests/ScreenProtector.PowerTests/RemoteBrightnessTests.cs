using ScreenProtector;

internal static class RemoteBrightnessTests
{
    public static readonly (string Name, Action Run)[] Tests =
    {
        ("Remote brightness saves once, restores, and captures the next session", EnterRestoreReenter),
        ("Remote startup uses the same brightness save and restore", RemoteStartup),
        ("Read failure does not lower brightness and can recover", ReadFailure),
        ("Missing or invalid hardware brightness is never overwritten", InvalidReading),
        ("Partial lowering failure retains the original brightness for retry", LowerFailure),
        ("Partial lowering failure can restore without a successful retry", RestoreAfterLowerFailure),
        ("Restore failure retains the original brightness for retry", RestoreFailure),
        ("Original zero brightness is a valid saved value", OriginalZero),
        ("Remote reentry after partial restore lowers again without recapturing", ReenterAfterRestoreFailure),
    };

    private static void EnterRestoreReenter()
    {
        int brightness = 50;
        int reads = 0;
        var writes = new List<int>();
        var session = new RemoteBrightnessSession(() => { reads++; return brightness; }, value =>
        {
            writes.Add(value);
            brightness = value;
        });
        session.Restore();
        Equal(0, writes.Count);
        session.Enter();
        session.Enter();
        Equal(1, reads);
        Equal(1, writes.Count);
        Equal(0, brightness);
        State(session, saved: true, dimmed: true);
        session.Restore();
        session.Restore();
        Equal(50, brightness);
        Equal(2, writes.Count);
        State(session, saved: false, dimmed: false);
        Equal<int?>(null, session.SavedBrightness);
        brightness = 80;
        session.Enter();
        session.Restore();
        Equal(2, reads);
        Equal(80, brightness);
    }

    private static void RemoteStartup()
    {
        int brightness = 65;
        var session = new RemoteBrightnessSession(() => brightness, value => brightness = value);
        State(session, saved: false, dimmed: false);
        session.Enter();
        Equal(0, brightness);
        session.Restore();
        Equal(65, brightness);
    }

    private static void ReadFailure()
    {
        bool fail = true;
        var writes = new List<int>();
        var session = new RemoteBrightnessSession(
            () => fail ? throw new IOException("Read failed") : 70, writes.Add);
        Throws<IOException>(session.Enter);
        State(session, saved: false, dimmed: false);
        Equal(0, writes.Count);
        fail = false;
        session.Enter();
        session.Restore();
        Equal(70, writes[^1]);
    }

    private static void InvalidReading()
    {
        foreach (int? brightness in new int?[] { null, -1, 101 })
        {
            var writes = new List<int>();
            var session = new RemoteBrightnessSession(() => brightness, writes.Add);
            Throws<InvalidOperationException>(session.Enter);
            State(session, saved: false, dimmed: false);
            session.Restore();
            Equal(0, writes.Count);
        }
    }

    private static void LowerFailure()
    {
        int brightness = 50;
        int reads = 0;
        bool fail = true;
        var session = new RemoteBrightnessSession(() => { reads++; return brightness; }, value =>
        {
            brightness = value;
            if (fail) throw new IOException("Lower failed after changing hardware");
        });
        Throws<IOException>(session.Enter);
        Equal(0, brightness);
        State(session, saved: true, dimmed: false);
        Equal<int?>(50, session.SavedBrightness);
        fail = false;
        session.Enter();
        Equal(1, reads);
        State(session, saved: true, dimmed: true);
        session.Restore();
        Equal(50, brightness);
    }

    private static void RestoreAfterLowerFailure()
    {
        int brightness = 45;
        var session = new RemoteBrightnessSession(() => brightness, value =>
        {
            brightness = value;
            if (value == 0) throw new IOException("Partial lower failure");
        });
        Throws<IOException>(session.Enter);
        session.Restore();
        Equal(45, brightness);
        State(session, saved: false, dimmed: false);
    }

    private static void RestoreFailure()
    {
        int brightness = 55;
        bool fail = true;
        var session = new RemoteBrightnessSession(() => brightness, value =>
        {
            if (value != 0 && fail) throw new IOException("Restore failed");
            brightness = value;
        });
        session.Enter();
        Throws<IOException>(session.Restore);
        Equal(0, brightness);
        State(session, saved: true, dimmed: false);
        fail = false;
        session.Restore();
        Equal(55, brightness);
        State(session, saved: false, dimmed: false);
    }

    private static void OriginalZero()
    {
        var writes = new List<int>();
        var session = new RemoteBrightnessSession(() => 0, writes.Add);
        session.Enter();
        State(session, saved: true, dimmed: true);
        session.Restore();
        Equal(2, writes.Count);
        Equal(0, writes[^1]);
        State(session, saved: false, dimmed: false);
    }

    private static void ReenterAfterRestoreFailure()
    {
        int brightness = 75;
        int reads = 0;
        bool fail = true;
        var session = new RemoteBrightnessSession(() => { reads++; return brightness; }, value =>
        {
            brightness = value;
            if (value != 0 && fail) throw new IOException("Partial restore failure");
        });
        session.Enter();
        Throws<IOException>(session.Restore);
        Equal(75, brightness);
        State(session, saved: true, dimmed: false);
        session.Enter();
        Equal(0, brightness);
        Equal(1, reads);
        State(session, saved: true, dimmed: true);
        fail = false;
        session.Restore();
        Equal(75, brightness);
    }

    private static void State(RemoteBrightnessSession session, bool saved, bool dimmed)
    {
        Equal(saved, session.HasSavedBrightness);
        Equal(dimmed, session.IsDimmed);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}; got {actual}.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
