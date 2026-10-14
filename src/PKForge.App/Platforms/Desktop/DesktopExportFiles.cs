namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// File placement for desktop exports. An export never replaces an earlier file of the same
/// name (it gets a " (2)" style suffix instead) and never leaves a partly written file behind.
/// </summary>
internal static class DesktopExportFiles
{
    private const int MaxNameAttempts = 10_000;

    /// <summary>The default export folder: ~/Documents/PKForge/exports.</summary>
    public static string DefaultDirectory()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(docs))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            docs = Path.Combine(home, "Documents");
        }
        return Path.Combine(docs, "PKForge", "exports");
    }

    /// <summary>
    /// The first of <c>name.ext</c>, <c>name (2).ext</c>, <c>name (3).ext</c>, ... for which
    /// <paramref name="exists"/> is false.
    /// </summary>
    public static string UniqueName(string fileName, Func<string, bool> exists)
    {
        foreach (var candidate in CandidateNames(fileName))
        {
            if (!exists(candidate)) return candidate;
        }
        throw new IOException($"No free file name for {fileName}.");
    }

    /// <summary><c>name.ext</c>, then <c>name (2).ext</c>, <c>name (3).ext</c>, ... up to a fixed limit.</summary>
    public static IEnumerable<string> CandidateNames(string fileName)
    {
        yield return fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var n = 2; n < MaxNameAttempts; n++) yield return $"{stem} ({n}){extension}";
    }

    /// <summary>The path a file named <paramref name="fileName"/> would get in <paramref name="directory"/> right now.</summary>
    public static string UniquePath(string directory, string fileName) =>
        Path.Combine(directory, UniqueName(fileName, name => Path.Exists(Path.Combine(directory, name))));

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into <paramref name="directory"/> under a name no
    /// existing file uses, and returns the new path. The data is written to a temporary file
    /// first and moved into place without overwriting.
    /// </summary>
    public static string CopyUnique(string sourcePath, string directory)
    {
        Directory.CreateDirectory(directory);
        var fileName = Path.GetFileName(sourcePath);
        var temp = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.part");
        try
        {
            File.Copy(sourcePath, temp);
            for (var attempt = 0; ; attempt++)
            {
                var destination = UniquePath(directory, fileName);
                try
                {
                    File.Move(temp, destination, overwrite: false);
                    return destination;
                }
                catch (IOException) when (Path.Exists(destination) && attempt < MaxNameAttempts)
                {
                    // Another writer took the name between the check and the move.
                }
            }
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// Copies every source into <paramref name="directory"/> with <see cref="CopyUnique"/>.
    /// When one copy fails, the copies already made are removed again.
    /// </summary>
    public static IReadOnlyList<string> CopyAllUnique(IReadOnlyList<string> sourcePaths, string directory)
    {
        var written = new List<string>(sourcePaths.Count);
        try
        {
            foreach (var source in sourcePaths) written.Add(CopyUnique(source, directory));
            return written;
        }
        catch
        {
            foreach (var path in written) TryDelete(path);
            throw;
        }
    }

    /// <summary>
    /// Writes <paramref name="sourcePath"/> to a destination the user picked (and already
    /// agreed to replace), through a temporary file in the same folder so a failure leaves
    /// any existing file intact.
    /// </summary>
    public static void CopyReplacing(string sourcePath, string destinationPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath))!;
        var temp = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.part");
        try
        {
            File.Copy(sourcePath, temp);
            File.Move(temp, destinationPath, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary><paramref name="path"/> with the home folder shown as <c>~</c>.</summary>
    public static string ForDisplay(string path, string? home)
    {
        if (string.IsNullOrEmpty(home)) return path;
        var trimmed = home.TrimEnd(Path.DirectorySeparatorChar);
        if (trimmed.Length == 0) return path;
        if (string.Equals(path, trimmed, StringComparison.Ordinal)) return "~";
        return path.StartsWith(trimmed + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? "~" + path[trimmed.Length..]
            : path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
