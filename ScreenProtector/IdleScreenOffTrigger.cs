namespace ScreenProtector;

internal sealed class IdleScreenOffTrigger
{
    private uint? _lastInputTick;
    private bool _triggered;

    public void Reset()
    {
        _lastInputTick = null;
        _triggered = false;
    }

    public bool ShouldTrigger(uint lastInputTick, int idleSeconds, int thresholdSeconds, bool enabled)
    {
        if (_lastInputTick != lastInputTick)
        {
            _lastInputTick = lastInputTick;
            _triggered = false;
        }

        if (!enabled || thresholdSeconds <= 0)
        {
            _triggered = false;
            return false;
        }

        if (_triggered || idleSeconds < thresholdSeconds)
            return false;

        // One attempt per idle period, including failed or already-pending requests.
        _triggered = true;
        return true;
    }
}
