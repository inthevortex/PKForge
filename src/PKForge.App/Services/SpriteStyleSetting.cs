using PKForge.Domain;

namespace PKForge.App.Services;

/// <summary>
/// The art Pokémon are drawn with in the boxes, the party and the summary (see
/// <see cref="SpriteStyles.Resolve"/>). A player setting on the desktop, where HOME renders
/// are the default once the sprite pack is in; Android always draws the pixel sprites.
/// </summary>
public static class SpriteStyleSetting
{
    private const string Key = "sprite_style";

    private static readonly bool Desktop =
#if PKFORGE_DESKTOP
        true;
#else
        false;
#endif

    private static SpriteStyle? _picked;
    private static bool _pickedRead;

    /// <summary>Whether this platform lets the player choose.</summary>
    public static bool Offered => Desktop;

    public static SpriteStyle Current => SpriteStyles.Resolve(Desktop, Picked, SpritePackDownloader.MainPackInstalled);

    /// <summary>The player's own pick, read once; null while they have not chosen.</summary>
    private static SpriteStyle? Picked
    {
        get
        {
            if (!Desktop) return null;
            if (!_pickedRead)
            {
                _picked = Enum.TryParse<SpriteStyle>(Preferences.Default.Get(Key, ""), out var style) ? style : null;
                _pickedRead = true;
            }
            return _picked;
        }
    }

    public static void Set(SpriteStyle style)
    {
        Preferences.Default.Set(Key, style.ToString());
        _picked = style;
        _pickedRead = true;
    }

    public static string Name(SpriteStyle style) => style == SpriteStyle.Home ? "HOME renders" : "Pixel sprites";
}
