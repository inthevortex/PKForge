using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Maui.Devices;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace PKForge.App.Platforms.Desktop;

public sealed class DesktopDeviceDisplay : IDeviceDisplay
{
    private AvaloniaWindow? _hookedWindow;

    private static AvaloniaWindow? MainWindow =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public bool KeepScreenOn { get; set; }

    public DisplayInfo MainDisplayInfo
    {
        get
        {
            EnsureWindowHooked();
            var window = MainWindow;
            var screens = window?.Screens;
            var screen = window != null ? (screens?.ScreenFromWindow(window) ?? screens?.Primary) : null;
            double width = screen?.Bounds.Width ?? 1920;
            double height = screen?.Bounds.Height ?? 1080;
            double density = window?.RenderScaling ?? screen?.Scaling ?? 1.0;
            if (density <= 0) density = 1.0;
            var orientation = width >= height ? DisplayOrientation.Landscape : DisplayOrientation.Portrait;
            return new DisplayInfo(width, height, density, orientation, DisplayRotation.Rotation0);
        }
    }

    public event EventHandler<DisplayInfoChangedEventArgs>? MainDisplayInfoChanged;

    private void EnsureWindowHooked()
    {
        var window = MainWindow;
        if (window != null && window != _hookedWindow)
        {
            _hookedWindow = window;
            window.ScalingChanged += OnDisplayChanged;
            window.PositionChanged += OnDisplayChanged;
            if (window.Screens != null)
            {
                window.Screens.Changed += OnDisplayChanged;
            }
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        var info = MainDisplayInfo;
        MainDisplayInfoChanged?.Invoke(this, new DisplayInfoChangedEventArgs(info));
    }
}
