using ScreenProtector;
using System.Drawing;

var tests = new (string Name, Action Run)[]
{
    ("Every screen is covered; repeated Show is ignored", CoversAllScreens),
    ("Wake cleans all covers and restores previous awake flags", WakeRestores),
    ("Synthetic mouse movement is ignored; real movement wakes", MouseMovement),
    ("Closing one cover closes the whole blackout", CoverClosed),
    ("Close allows a fresh blackout session", Reopen),
    ("Partial startup failures clean all resources", StartupFailure),
    ("Awake request failure does not create covers", AwakeFailure),
    ("Dispose is idempotent and prevents reopening", DisposeTwice),
    ("Cleanup continues after a cover disposal failure", CleanupFailure),
    ("Idle screen off does not trigger before the threshold", IdleNotEarly),
    ("Idle screen off triggers once per continuous idle period", IdleOnce),
    ("New input rearms screen off even above threshold", IdleNewInput),
    ("Disabled and nonpositive thresholds never trigger", IdleDisabled),
    ("Reenabling idle screen off rearms the trigger", IdleReenabled),
    ("Remote startup leaves blackout unavailable", RemoteStartup),
    ("Remote transition cleans blackout and console return restores saved eligibility", SessionTransition),
    ("Only the active local console session is eligible", SessionClassification),
    ("Session query failure disables eligibility and can recover", SessionQueryFailure),
    ("Repeated local refresh preserves the idle countdown", SessionRepeatedRefresh),
    ("Console return clips stale idle and waits for fresh local idle", SessionIdleResume),
}.Concat(RemoteBrightnessTests.Tests).Concat(RemoteSessionTests.Tests).Concat(ProtectionModeTests.Tests).ToArray();
int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void CoversAllScreens()
{
    var api = new FakeBlackoutPlatform();
    using var blackout = new ScreenBlackout(api);
    blackout.Show();
    blackout.Show();
    True(blackout.IsActive);
    Equal(2, api.Covers.Count);
    Equal(api.Bounds[0], api.Covers[0].Bounds);
    Equal(api.Bounds[1], api.Covers[1].Bounds);
    True(api.Covers.All(c => c.ShowCount == 1));
    Equal(ScreenBlackout.KeepAwake, api.States.Single());
    Equal(1, api.HideCount);
}
static void WakeRestores()
{
    var api = new FakeBlackoutPlatform();
    using var blackout = new ScreenBlackout(api);
    int closed = 0;
    blackout.Closed += (_, _) => { closed++; AssertClean(blackout, api); };
    blackout.Show();
    api.Covers[1].RaiseWake();
    Equal(1, closed);
    blackout.Close();
    Equal(1, closed);
}
static void MouseMovement()
{
    var api = new FakeBlackoutPlatform { SyntheticMove = true };
    using var blackout = new ScreenBlackout(api);
    blackout.Show();
    True(blackout.IsActive);
    api.Covers[0].RaiseMove();
    True(blackout.IsActive);
    api.CursorPosition = new Point(101, 100);
    api.Covers[1].RaiseMove();
    AssertClean(blackout, api);
}
static void CoverClosed()
{
    var api = new FakeBlackoutPlatform();
    using var blackout = new ScreenBlackout(api);
    blackout.Show();
    api.Covers[0].RaiseClosed();
    AssertClean(blackout, api);
}
static void Reopen()
{
    var api = new FakeBlackoutPlatform();
    using var blackout = new ScreenBlackout(api);
    int closed = 0;
    blackout.Closed += (_, _) => closed++;
    blackout.Show();
    blackout.Close();
    blackout.Show();
    Equal(4, api.Covers.Count);
    True(blackout.IsActive);
    api.Covers[2].RaiseWake();
    Equal(2, closed);
    Equal(2, api.HideCount);
    Equal(2, api.ShowCount);
}
static void StartupFailure()
{
    foreach (bool create in new[] { false, true })
    {
        var api = new FakeBlackoutPlatform { FailCreate = create, FailShow = !create };
        using var blackout = new ScreenBlackout(api);
        int closed = 0;
        blackout.Closed += (_, _) => closed++;
        Throws<IOException>(blackout.Show);
        AssertClean(blackout, api);
        Equal(1, closed);
    }
}
static void AwakeFailure()
{
    var api = new FakeBlackoutPlatform { FailAwake = true };
    using var blackout = new ScreenBlackout(api);
    Throws<IOException>(blackout.Show);
    True(!blackout.IsActive);
    Equal(0, api.Covers.Count);
    Equal(0, api.HideCount);
    Equal(0, api.ShowCount);
    Equal(1, api.States.Count);
}
static void DisposeTwice()
{
    var api = new FakeBlackoutPlatform();
    var blackout = new ScreenBlackout(api);
    int closed = 0;
    blackout.Closed += (_, _) => closed++;
    blackout.Show();
    blackout.Dispose();
    blackout.Dispose();
    AssertClean(blackout, api);
    Equal(1, closed);
    Equal(2, api.States.Count);
    Throws<ObjectDisposedException>(blackout.Show);
}
static void CleanupFailure()
{
    var api = new FakeBlackoutPlatform();
    using var blackout = new ScreenBlackout(api);
    blackout.Show();
    api.Covers[0].FailDispose = true;
    Throws<AggregateException>(blackout.Close);
    AssertClean(blackout, api);
}
static void AssertClean(ScreenBlackout blackout, FakeBlackoutPlatform api)
{
    True(!blackout.IsActive);
    True(api.Covers.All(c => c.DisposeCount == 1));
    Equal(api.PreviousState, api.States[^1]);
    Equal(1, api.ShowCount);
}
static void True(bool condition)
{
    if (!condition) throw new Exception("Assertion failed.");
}

static void IdleNotEarly()
{
    var trigger = new IdleScreenOffTrigger();
    True(!trigger.ShouldTrigger(100, 0, 60, true));
    True(!trigger.ShouldTrigger(100, 59, 60, true));
    True(trigger.ShouldTrigger(100, 60, 60, true));
}

static void IdleOnce()
{
    var trigger = new IdleScreenOffTrigger();
    True(trigger.ShouldTrigger(100, 60, 60, true));
    True(!trigger.ShouldTrigger(100, 61, 60, true));
    True(!trigger.ShouldTrigger(100, 90, 60, true));
    True(!trigger.ShouldTrigger(100, 600, 60, true));
}

static void IdleNewInput()
{
    var trigger = new IdleScreenOffTrigger();
    True(trigger.ShouldTrigger(100, 60, 60, true));
    True(trigger.ShouldTrigger(200, 65, 60, true));
    True(!trigger.ShouldTrigger(200, 66, 60, true));
    True(!trigger.ShouldTrigger(300, 5, 60, true));
    True(trigger.ShouldTrigger(300, 60, 60, true));
}

static void IdleDisabled()
{
    var trigger = new IdleScreenOffTrigger();
    True(!trigger.ShouldTrigger(100, 120, 60, false));
    True(!trigger.ShouldTrigger(100, 120, 0, true));
    True(!trigger.ShouldTrigger(100, 120, -1, true));
    True(trigger.ShouldTrigger(100, 120, 60, true));
    True(!trigger.ShouldTrigger(100, 120, 0, true));
    True(trigger.ShouldTrigger(100, 120, 60, true));
}

static void IdleReenabled()
{
    var trigger = new IdleScreenOffTrigger();
    True(trigger.ShouldTrigger(100, 60, 60, true));
    True(!trigger.ShouldTrigger(100, 90, 60, false));
    True(trigger.ShouldTrigger(100, 91, 60, true));
    True(!trigger.ShouldTrigger(100, 92, 60, true));
}

static void RemoteStartup()
{
    var guard = new LocalSessionGuard(() => false, () => 1000);
    var api = new FakeBlackoutPlatform();
    using var blackout = new ScreenBlackout(api);
    var trigger = new IdleScreenOffTrigger();
    True(!guard.IsLocal);
    True(!guard.Refresh());
    if (trigger.ShouldTrigger(100, guard.GetIdleSeconds(600), 60, guard.IsLocal)) blackout.Show();
    True(!blackout.IsActive);
    Equal(0, api.Covers.Count);
}

static void SessionTransition()
{
    bool local = true;
    bool savedEnabled = true;
    long ticks = 1000;
    var guard = new LocalSessionGuard(() => local, () => ticks);
    var trigger = new IdleScreenOffTrigger();
    var api = new FakeBlackoutPlatform();
    using var blackout = new ScreenBlackout(api);

    void Tick()
    {
        guard.Refresh();
        if (!guard.IsLocal) blackout.Close();
        if (trigger.ShouldTrigger(100, guard.GetIdleSeconds(600), 60, savedEnabled && guard.IsLocal))
            blackout.Show();
    }

    Tick();
    ticks += 60000;
    Tick();
    True(blackout.IsActive);
    local = false;
    Tick();
    AssertClean(blackout, api);
    ticks += 600000;
    Tick();
    Equal(2, api.Covers.Count);
    True(savedEnabled);
    local = true;
    Tick();
    True(!blackout.IsActive);
    ticks += 60000;
    Tick();
    True(blackout.IsActive);
    Equal(4, api.Covers.Count);

    local = false;
    Tick();
    savedEnabled = false;
    local = true;
    Tick();
    ticks += 60000;
    Tick();
    True(!blackout.IsActive);
    True(!savedEnabled);
    Equal(4, api.Covers.Count);
}

static void SessionClassification()
{
    True(LocalSessionGuard.IsLocalSession(0, 0, 2, 2));
    True(!LocalSessionGuard.IsLocalSession(2, 0, 2, 2));
    True(!LocalSessionGuard.IsLocalSession(0, 4, 2, 2));
    True(!LocalSessionGuard.IsLocalSession(0, 0, 2, 3));
    True(!LocalSessionGuard.IsLocalSession(0, 0, 2, uint.MaxValue));
    True(!LocalSessionGuard.IsLocalSession(1, 0, 2, 2));
}

static void SessionQueryFailure()
{
    bool fail = true;
    var guard = new LocalSessionGuard(() => fail ? throw new IOException("Session unavailable") : true, () => 1000);
    True(!guard.Refresh());
    True(!guard.IsLocal);
    fail = false;
    True(guard.Refresh());
    True(guard.IsLocal);
    fail = true;
    True(guard.Refresh());
    True(!guard.IsLocal);
    Equal(0, guard.GetIdleSeconds(600));
    fail = false;
    True(guard.Refresh());
    True(guard.IsLocal);
}

static void SessionRepeatedRefresh()
{
    long ticks = 1000;
    var guard = new LocalSessionGuard(() => true, () => ticks);
    True(guard.Refresh());
    ticks += 30000;
    True(!guard.Refresh());
    Equal(30, guard.GetIdleSeconds(600));
    ticks += 30000;
    True(!guard.Refresh());
    Equal(60, guard.GetIdleSeconds(600));
}

static void SessionIdleResume()
{
    bool local = true;
    long ticks = 1000;
    var guard = new LocalSessionGuard(() => local, () => ticks);
    guard.Refresh();
    ticks += 600000;
    Equal(600, guard.GetIdleSeconds(900));
    local = false;
    guard.Refresh();
    ticks += 600000;
    Equal(0, guard.GetIdleSeconds(900));
    local = true;
    guard.Refresh();
    Equal(0, guard.GetIdleSeconds(900));
    ticks += 59999;
    Equal(59, guard.GetIdleSeconds(900));
    ticks++;
    Equal(60, guard.GetIdleSeconds(900));
    Equal(5, guard.GetIdleSeconds(5));
    Equal(0, guard.GetIdleSeconds(-1));
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}; got {actual}.");
}

static T Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T error) { return error; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

internal sealed class FakeBlackoutPlatform : IBlackoutPlatform
{
    public Point CursorPosition { get; set; } = new(100, 100);
    public Rectangle[] Bounds { get; } = { new(-1920, 0, 1920, 1080), new(0, 0, 2560, 1440) };
    public IEnumerable<Rectangle> ScreenBounds => Bounds;
    public List<FakeCover> Covers { get; } = new();
    public List<uint> States { get; } = new();
    public uint PreviousState { get; } = ScreenBlackout.Continuous | 0x00000040;
    public int HideCount { get; private set; }
    public int ShowCount { get; private set; }
    public bool FailCreate { get; init; }
    public bool FailShow { get; init; }
    public bool FailAwake { get; init; }
    public bool SyntheticMove { get; init; }
    public IBlackoutCover CreateCover(Rectangle bounds)
    {
        if (FailCreate && Covers.Count == 1) throw new IOException("Create failed");
        var cover = new FakeCover(bounds, FailShow && Covers.Count == 1, SyntheticMove);
        Covers.Add(cover);
        return cover;
    }
    public uint SetExecutionState(uint state)
    {
        States.Add(state);
        if (FailAwake) throw new IOException("Awake request failed");
        return PreviousState;
    }
    public void HideCursor() => HideCount++;
    public void ShowCursor() => ShowCount++;
}
internal sealed class FakeCover(Rectangle bounds, bool failShow, bool syntheticMove) : IBlackoutCover
{
    public Rectangle Bounds { get; } = bounds;
    public int ShowCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool FailDispose { get; set; }
    public event EventHandler? Wake;
    public event EventHandler? MouseMoved;
    public event EventHandler? Closed;
    public void Show()
    {
        ShowCount++;
        if (failShow) throw new IOException("Show failed");
        if (syntheticMove) RaiseMove();
    }
    public void RaiseWake() => Wake?.Invoke(this, EventArgs.Empty);
    public void RaiseMove() => MouseMoved?.Invoke(this, EventArgs.Empty);
    public void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);
    public void Dispose()
    {
        DisposeCount++;
        RaiseClosed();
        if (FailDispose) throw new IOException("Dispose failed");
    }
}
