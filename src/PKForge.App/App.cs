namespace PKForge.App;

public sealed class App : Application
{
    /// <summary>Raised when Android resumes the app (including after the install-permission screen).</summary>
    public static event Action? Resumed;
    public static event Action? Suspended;
    protected override void OnSleep()
    {
        Suspended?.Invoke();
        Music?.PauseForBackground();
        base.OnSleep();
    }

    private static Domain.IMusicPlayer? Music =>
        IPlatformApplication.Current?.Services.GetService<Domain.IMusicPlayer>();

    /// <summary>The user's optional default background music starts with the app, once.</summary>
    protected override void OnStart()
    {
        base.OnStart();
        Music?.MaybeAutostart();
    }

    protected override void OnResume()
    {
        base.OnResume();
        Music?.ResumeFromBackground();
        Resumed?.Invoke();
    }

    public App()
    {
        Trace("App ctor");

        // Every page reads its chrome colors when it is built, so the scheme comes first.
        Services.ColorThemeSetting.ApplyStored();

        // Warm both Skia faces off the ctor: the party view paints before the first
        // save opens, and its cached nickname font must never pin the placeholder.
        _ = Views.PixelFont.WarmAsync();
        _ = Views.PixelFont.WarmFallbackAsync();

        // The DS system font (NDS12/"PixelUI") is the app's voice everywhere. Symbol glyphs
        // that NDS12 lacks (Ⓐ, ♂, ▼, ◓ ...) are pinned to "Rounded" at their few call sites.
        Style FontStyle(Type target, BindableProperty property) =>
            new(target) { Setters = { new Setter { Property = property, Value = "PixelUI" } } };
        Resources.Add(FontStyle(typeof(Label), Label.FontFamilyProperty));
        Resources.Add(FontStyle(typeof(Button), Button.FontFamilyProperty));
        Resources.Add(FontStyle(typeof(Entry), Entry.FontFamilyProperty));
        Resources.Add(FontStyle(typeof(Editor), Editor.FontFamilyProperty));
    }

    internal static void Trace(string message)
    {
#if ANDROID
        Android.Util.Log.Info("PKForgeBoot", message);
#else
        Console.WriteLine($"[PKForgeBoot] {message}");
#endif
    }

    private static NavigationPage CreateRoot(IServiceProvider services)
    {
        Trace("resolving HomePage");
        var page = services.GetRequiredService<Views.HomePage>();
        Trace("HomePage resolved");
        return new NavigationPage(page)
        {
            BarBackgroundColor = Theme.UiTokens.Navy1,
            BarTextColor = Colors.White,
        };
    }

    /// <summary>
    /// Builds Home and the lower screen again after a color scheme change: pages take their
    /// colors when they are built. Called from Home's settings, so Home is the only page open.
    /// </summary>
    internal async Task ReloadForThemeAsync()
    {
        var services = IPlatformApplication.Current?.Services;
        if (services is null || Windows.Count == 0) return;
        Windows[0].Page = CreateRoot(services);
        var host = services.GetService<PKForge.Domain.ISecondaryDisplayHost>();
        if (host is null || Services.SecondScreenMode.UserOff) return;
        try
        {
            await host.DismissAsync();
            if (host.IsAvailable) await host.ShowAsync();
        }
        catch (InvalidOperationException error)
        {
            Services.AppLog.Warn("theme", $"Rebuilding the lower screen: {error.Message}");
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Trace("CreateWindow enter");
        // Never let a startup failure become a silent blank screen: show the error.
        try
        {
            var services = IPlatformApplication.Current?.Services
                ?? throw new InvalidOperationException("MAUI services are unavailable.");
            var root = CreateRoot(services);
#if PKFORGE_DESKTOP
            var (initW, initH) = GetInitialDesktopWindowSize();
#endif
            return new Window(root)
            {
                Title = "PKForge",
#if PKFORGE_DESKTOP
                Width = initW,
                Height = initH,
#endif
            };
        }
        catch (Exception error)
        {
            Trace($"CreateWindow FAILED: {error}");
            return new Window(new ContentPage
            {
                BackgroundColor = Theme.UiTokens.Navy0,
                Content = new ScrollView
                {
                    Content = new Label
                    {
                        Text = $"PKForge failed to start:\n\n{error}",
                        TextColor = Colors.White,
                        Margin = new Thickness(20),
                    },
                },
            })
            {
                Title = "PKForge Error",
#if PKFORGE_DESKTOP
                Width = 1280,
                Height = 800,
#endif
            };
        }
    }

#if PKFORGE_DESKTOP
    private static (double Width, double Height) GetInitialDesktopWindowSize()
    {
        try
        {
            var display = Microsoft.Maui.Devices.DeviceDisplay.MainDisplayInfo;
            if (display.Width > 0 && display.Height > 0)
            {
                double density = display.Density > 0 ? display.Density : 1.0;
                double availW = display.Width / density;
                double availH = display.Height / density;
                if (availW <= 1280 || availH <= 800)
                {
                    return (Math.Min(1280, availW), Math.Min(800, availH));
                }
                return (Math.Min(1920, availW), Math.Min(1080, availH));
            }
        }
        catch
        {
        }
        return (1280, 800);
    }
#endif
}
