namespace ScreenProtector;

internal static class ProtectionModeSelector
{
    public static bool UseRemote(bool automatic, bool manualRemote, SessionKind session, bool previousRemote)
    {
        if (!automatic) return manualRemote;
        return session switch
        {
            SessionKind.Remote => true,
            SessionKind.Local => false,
            _ => previousRemote,
        };
    }
}
