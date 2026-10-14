using System.Collections.Concurrent;

namespace PKForge.App.Services;

/// <summary>
/// An item icon on disk. Icons are written whole or not at all, and an icon cut short (by a
/// crash mid-write, or by an older build that wrote in place) is recognised so it can be
/// fetched again rather than shown broken for good.
/// </summary>
internal static class ItemIconFile
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The IEND chunk's type and CRC: the last thing a complete PNG holds.</summary>
    private static ReadOnlySpan<byte> End => [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    /// <summary>How far from the end the IEND chunk is looked for, allowing a little trailing data.</summary>
    private const int TailLength = 256;

    /// <summary>Icons found or recorded whole this run, so a screen's repaint does not reread them.</summary>
    private static readonly ConcurrentDictionary<string, byte> KnownWhole = new(StringComparer.Ordinal);

    /// <summary>
    /// True when the icon at <paramref name="path"/> can be shown. One that is not a whole PNG is
    /// deleted, so it counts as missing and is fetched again.
    /// </summary>
    public static bool IsUsable(string path)
    {
        if (!File.Exists(path)) return false;
        if (KnownWhole.ContainsKey(path)) return true;
        if (IsWhole(path))
        {
            KnownWhole.TryAdd(path, 0);
            return true;
        }
        TryDelete(path);
        return false;
    }

    /// <summary>
    /// Records <paramref name="path"/> as whole without reading it, for an icon whose bytes were
    /// verified another way (the sprite pack archive's SHA-256).
    /// </summary>
    public static void MarkWhole(string path) => KnownWhole.TryAdd(path, 0);

    /// <summary>
    /// Checks every icon in <paramref name="directory"/> except <paramref name="skip"/> as
    /// <see cref="IsUsable"/> does, so pickers find them known whole instead of reading them
    /// on the UI thread. Returns how many damaged icons were deleted. Call off the UI thread.
    /// </summary>
    public static int Warm(string directory, string skip)
    {
        var deleted = 0;
        try
        {
            if (!Directory.Exists(directory)) return 0;
            foreach (var path in Directory.EnumerateFiles(directory, "*.png"))
            {
                if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.Ordinal)
                    || string.Equals(Path.GetFileName(path), skip, StringComparison.Ordinal)) continue;
                if (!IsUsable(path)) deleted++;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Unwarmed icons are still checked when first shown.
        }
        return deleted;
    }

    /// <summary>True for a PNG that runs from its signature through to its IEND chunk.</summary>
    public static bool IsWhole(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= Signature.Length + End.Length
        && bytes.StartsWith(Signature)
        && bytes[Math.Max(Signature.Length, bytes.Length - TailLength)..].IndexOf(End) >= 0;

    /// <summary>
    /// True when the file at <paramref name="path"/> is a whole PNG; false when it is missing,
    /// cut short, not a PNG, or unreadable.
    /// </summary>
    public static bool IsWhole(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
            var length = file.Length;
            if (length < Signature.Length + End.Length) return false;
            Span<byte> head = stackalloc byte[8];
            file.ReadExactly(head);
            if (!head.SequenceEqual(Signature)) return false;
            var tail = new byte[(int)Math.Min(TailLength, length - Signature.Length)];
            file.Seek(-tail.Length, SeekOrigin.End);
            file.ReadExactly(tail);
            return tail.AsSpan().IndexOf(End) >= 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/> through a temporary file renamed
    /// into place, so the path never holds part of an icon. Throws
    /// <see cref="InvalidDataException"/>, writing nothing, when the bytes are not a whole PNG.
    /// </summary>
    public static async Task WriteAsync(string path, byte[] bytes)
    {
        if (!IsWhole(bytes)) throw new InvalidDataException($"{Path.GetFileName(path)} is not a complete PNG.");
        var temporary = $"{path}.{Guid.NewGuid():N}.part";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
            KnownWhole.TryAdd(path, 0);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>Deletes the file at <paramref name="path"/>; best effort.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
