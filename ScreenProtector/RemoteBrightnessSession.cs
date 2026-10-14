namespace ScreenProtector;

internal sealed class RemoteBrightnessSession(Func<int?> readBrightness, Action<int> writeBrightness)
{
    private int? savedBrightness;

    public int? SavedBrightness => savedBrightness;
    public bool HasSavedBrightness => savedBrightness.HasValue;
    public bool IsDimmed { get; private set; }

    public void Enter()
    {
        if (IsDimmed) return;
        if (!savedBrightness.HasValue)
        {
            int? brightness = readBrightness();
            if (brightness is null or < 0 or > 100)
                throw new InvalidOperationException("Cannot read a valid hardware brightness to restore.");
            savedBrightness = brightness;
        }

        // Keep the original value even if the driver changes brightness before throwing.
        writeBrightness(0);
        IsDimmed = true;
    }

    public void Restore()
    {
        if (!savedBrightness.HasValue) return;
        // A failed restore may still change the hardware; a later Enter must lower it again.
        IsDimmed = false;
        writeBrightness(savedBrightness.Value);
        savedBrightness = null;
    }
}
