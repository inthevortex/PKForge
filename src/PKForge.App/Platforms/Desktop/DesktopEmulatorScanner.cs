using PKForge.Domain;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Desktop emulator detection service stub.
/// Full tree scanning and auto-discovery are integrated in Step 6.
/// </summary>
public sealed class DesktopEmulatorScanner : IEmulatorDetectionService
{
    public ValueTask<EmulatorScanResult> ScanAsync(string treeId, EmulatorKind kind, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new EmulatorScanResult([], 0, []));
}
