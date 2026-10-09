using PKForge.Domain;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Flat folder file access for desktop POSIX filesystems.
/// Tree IDs and document IDs are canonical absolute paths.
/// </summary>
public sealed class PosixFolderFileAccess(ISaveFileAccess saveFileAccess) : PKForge.Infrastructure.PosixFolderFileAccess(saveFileAccess)
{
}
