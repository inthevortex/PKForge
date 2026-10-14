namespace PKForge.Domain;

/// <summary>Which art the boxes, the party and the summary draw a Pokémon with.</summary>
public enum SpriteStyle
{
    /// <summary>The bundled pixel sprites and box icons.</summary>
    Pixel,

    /// <summary>Pokémon HOME's high-resolution renders, from the sprite pack.</summary>
    Home,
}

public static class SpriteStyles
{
    /// <summary>The smallest side a HOME render is prepared at; smaller draws still use this one.</summary>
    public const int MinPreparedSide = 32;

    /// <summary>Prepared sides are a multiple of this, so draws a few pixels apart share one.</summary>
    public const int PreparedStep = 16;

    /// <summary>
    /// The style in effect. Only the desktop offers a choice: the player's pick when there is
    /// one, otherwise HOME renders once the sprite pack is on the device. Elsewhere it is always
    /// the pixel sprites.
    /// </summary>
    public static SpriteStyle Resolve(bool desktop, SpriteStyle? picked, bool packInstalled) =>
        !desktop ? SpriteStyle.Pixel : picked ?? (packInstalled ? SpriteStyle.Home : SpriteStyle.Pixel);

    /// <summary>
    /// The long side to prepare a render at, for drawing it <paramref name="pixels"/> device
    /// pixels long: rounded up to a <see cref="PreparedStep"/>, so the draw is all but one to
    /// one and stays sharp, from <see cref="MinPreparedSide"/> up to the render's own
    /// <paramref name="nativeSide"/>, past which it can only be enlarged.
    /// </summary>
    public static int PreparedSide(int pixels, int nativeSide)
    {
        if (nativeSide <= 0) return 0;
        var stepped = (Math.Clamp(pixels, 1, nativeSide) + PreparedStep - 1) / PreparedStep * PreparedStep;
        return Math.Min(Math.Max(stepped, MinPreparedSide), nativeSide);
    }
}
