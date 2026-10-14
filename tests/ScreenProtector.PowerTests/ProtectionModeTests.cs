using ScreenProtector;
using System.Text.Json;

internal static class ProtectionModeTests
{
    public static readonly (string Name, Action Run)[] Tests =
    {
        ("Manual mode overrides every session state", ManualOverrides),
        ("Automatic mode follows local and remote sessions", AutomaticSelection),
        ("Automatic mode retains selection while session is unavailable", UnavailableRetainsSelection),
        ("Mode transitions dim remotely and restore on local return", RemoteTransitionRestores),
        ("Manual local selection during RDP does not dim", ManualLocalDuringRemote),
        ("Manual switching to local restores the saved brightness during RDP", ManualSwitchRestores),
        ("Manual local return restarts the idle countdown", ManualLocalRestartsIdle),
        ("Older settings default to automatic switching", OlderSettingsDefault),
        ("Manual mode and automatic preference survive serialization", SettingsRoundTrip),
    };

    private static void ManualOverrides()
    {
        foreach (SessionKind session in Enum.GetValues<SessionKind>())
        {
            Equal(false, ProtectionModeSelector.UseRemote(false, false, session, true));
            Equal(true, ProtectionModeSelector.UseRemote(false, true, session, false));
        }
    }

    private static void AutomaticSelection()
    {
        Equal(true, ProtectionModeSelector.UseRemote(true, false, SessionKind.Remote, false));
        Equal(false, ProtectionModeSelector.UseRemote(true, true, SessionKind.Local, true));
    }

    private static void UnavailableRetainsSelection()
    {
        Equal(true, ProtectionModeSelector.UseRemote(true, false, SessionKind.Unavailable, true));
        Equal(false, ProtectionModeSelector.UseRemote(true, true, SessionKind.Unavailable, false));
    }

    private static void RemoteTransitionRestores()
    {
        int brightness = 50;
        var brightnessSession = new RemoteBrightnessSession(() => brightness, value => brightness = value);
        bool remote = false;

        void Select(SessionKind session)
        {
            bool nextRemote = ProtectionModeSelector.UseRemote(true, false, session, remote);
            if (nextRemote && !remote) brightnessSession.Enter();
            if (!nextRemote && remote) brightnessSession.Restore();
            remote = nextRemote;
        }

        Select(SessionKind.Local);
        Equal(50, brightness);
        Select(SessionKind.Remote);
        Equal(0, brightness);
        Select(SessionKind.Unavailable);
        Equal(0, brightness);
        Select(SessionKind.Local);
        Equal(50, brightness);
    }

    private static void ManualLocalDuringRemote()
    {
        bool remote = ProtectionModeSelector.UseRemote(false, false, SessionKind.Remote, true);
        Equal(false, remote);
        int brightness = 50;
        var brightnessSession = new RemoteBrightnessSession(() => brightness, value => brightness = value);
        if (remote) brightnessSession.Enter();
        Equal(50, brightness);
    }

    private static void ManualSwitchRestores()
    {
        int brightness = 50;
        var dimming = new RemoteBrightnessSession(() => brightness, value => brightness = value);
        if (ProtectionModeSelector.UseRemote(false, true, SessionKind.Remote, false)) dimming.Enter();
        Equal(0, brightness);
        if (!ProtectionModeSelector.UseRemote(false, false, SessionKind.Remote, true)) dimming.Restore();
        Equal(50, brightness);
        Equal(false, dimming.HasSavedBrightness);
    }

    private static void ManualLocalRestartsIdle()
    {
        long ticks = 1000;
        var guard = new LocalSessionGuard(() => SessionKind.Local, () => ticks);
        guard.Refresh();
        ticks += 600000;
        Equal(600, guard.GetIdleSeconds(900));
        guard.RestartIdleCountdown();
        Equal(0, guard.GetIdleSeconds(900));
        ticks += 30000;
        Equal(30, guard.GetIdleSeconds(900));
    }

    private static void OlderSettingsDefault()
    {
        var settings = JsonSerializer.Deserialize<ScreenProtector.Properties.Settings>("{\"EnableIdleScreenOff\":true}")!;
        Equal(true, settings.AutoSwitchMode);
        Equal(false, settings.UseRemoteMode);
        Equal(true, settings.EnableIdleScreenOff);
    }

    private static void SettingsRoundTrip()
    {
        var settings = new ScreenProtector.Properties.Settings { AutoSwitchMode = false, UseRemoteMode = true };
        var restored = JsonSerializer.Deserialize<ScreenProtector.Properties.Settings>(JsonSerializer.Serialize(settings))!;
        Equal(false, restored.AutoSwitchMode);
        Equal(true, restored.UseRemoteMode);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}; got {actual}.");
    }
}
