namespace PKForge.App;

/// <summary>
/// Desktop implementation of the Poképark widget publisher.
/// On desktop there are no Android home-screen widgets, so this is a clean no-op.
/// </summary>
public static class PokeparkWidgetPublisher
{
    public const int FrameWidth = 640;
    public const int FrameHeight = 360;
    public const int FrameCount = 8;
    public const int FrameIntervalMilliseconds = 400;

    public static bool HasActiveWidgets() => false;

    public static void Publish(IReadOnlyList<byte[]> pngFrames)
    {
    }
}
