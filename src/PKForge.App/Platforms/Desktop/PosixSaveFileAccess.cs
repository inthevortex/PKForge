namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// POSIX filesystem save access for Linux desktop.
/// Document IDs are canonical absolute paths. Writes are atomic via temporary file + fsync + rename.
/// Symlinks are resolved to their target before writing so the link itself is preserved.
/// </summary>
public sealed class PosixSaveFileAccess : PKForge.Infrastructure.PosixSaveFileAccess
{
}
