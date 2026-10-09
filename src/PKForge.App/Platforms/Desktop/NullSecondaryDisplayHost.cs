using PKForge.Domain;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Null implementation of ISecondaryDisplayHost for desktop platforms
/// where second-screen presentation is not active.
/// </summary>
public sealed class NullSecondaryDisplayHost : ISecondaryDisplayHost
{
    public bool IsAvailable => false;

    public ValueTask ShowAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask DismissAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
