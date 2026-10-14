using Android.App;
using Android.Content.PM;
using Android.Hardware.Display;
using Android.OS;
using Android.Content;
using Android.Views;
using PKForge.Domain;

[assembly: UsesPermission(Android.Manifest.Permission.Vibrate)]
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

namespace PKForge.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    Icon = "@mipmap/pkforge", RoundIcon = "@mipmap/pkforge_round",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density
        | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
public sealed class MainActivity : MauiAppCompatActivity
{
    private const string MovedToMainScreen = "pkforge.moved-to-main-screen";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (savedInstanceState is null) MoveToMainScreenIfLaunchedBelow();
    }

    /// <summary>
    /// A dual-screen handheld can start PKForge on its lower panel, which then took the top
    /// panel for the second screen: the whole layout upside down. Relaunch once on the main
    /// display; if Android keeps it below, say so (the lower screen stays off, see
    /// <see cref="AndroidSecondaryDisplayHost.LaunchedOnLowerScreen"/>).
    /// </summary>
    private void MoveToMainScreenIfLaunchedBelow()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30) || !AndroidSecondaryDisplayHost.LaunchedOnLowerScreen(this)) return;
        if (Intent?.GetBooleanExtra(MovedToMainScreen, false) == true)
        {
            Services.AppLog.Warn("second", $"Still on display {Display?.DisplayId} after moving to the main screen");
            Android.Widget.Toast.MakeText(this,
                "PKForge opened on the lower screen. For the two-screen layout, launch it from the main screen.",
                Android.Widget.ToastLength.Long)?.Show();
            return;
        }
        try
        {
            var relaunch = new Intent(this, typeof(MainActivity))
                .AddFlags(ActivityFlags.NewTask | ActivityFlags.MultipleTask)
                .PutExtra(MovedToMainScreen, true);
            if (ActivityOptions.MakeBasic() is not { } options) return;
            options.SetLaunchDisplayId(Display.DefaultDisplay);
            Services.AppLog.Info("second", $"Launched on display {Display?.DisplayId}; moving to the main screen");
            StartActivity(relaunch, options.ToBundle());
            FinishAndRemoveTask();
        }
        catch (Exception error) when (error is Java.Lang.SecurityException or ActivityNotFoundException)
        {
            Services.AppLog.Error("second", "Could not move to the main screen", error);
        }
    }

    protected override void OnPause()
    {
        // A Presentation owns a separate window, so Android does not reliably hide it
        // when the launcher backgrounds the main activity (notably on the AYN Thor).
        GetPump()?.Reset();
        SecondaryDisplayHost()?.SuspendForActivityPause();
        base.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        OpenPokeparkFromIntent();
        SecondaryDisplayHost()?.ResumeAfterActivityPause();
    }

    /// <summary>
    /// Pairing or unplugging a controller changes the keyboard and navigation configuration.
    /// The activity is kept (see ConfigurationChanges): recreating it tore down the MAUI window
    /// under pages still loading images, and dropped the lower screen. A pad that goes away
    /// mid-hold never sends its key-up, so its repeat stops here.
    /// </summary>
    public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        GetPump()?.Reset();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        OpenPokeparkFromIntent();
    }

    private void OpenPokeparkFromIntent()
    {
        if (Intent?.GetBooleanExtra("pokepark", false) != true) return;
        Intent.RemoveExtra("pokepark");
        // A launcher widget click can arrive while the secondary Presentation still
        // owns focus. Bring the primary activity to the foreground before pushing
        // the park page so the tap never strands the user on the lower display.
        Window?.DecorView?.Post(() =>
        {
            Window?.DecorView?.RequestFocus();
            Window?.DecorView?.ClearFocus();
        });
        Microsoft.Maui.Controls.Application.Current?.Dispatcher.Dispatch(async () =>
        {
            var secondary = SecondaryDisplayHost();
            if (secondary is not null)
                await secondary.DismissAsync();
            var navigation = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
            if (navigation is null || navigation.NavigationStack.LastOrDefault() is Views.PokeparkPage) return;
            var page = IPlatformApplication.Current?.Services.GetRequiredService<Views.PokeparkPage>();
            if (page is not null) await navigation.PushAsync(page);
        });
    }

    private static AndroidSecondaryDisplayHost? SecondaryDisplayHost() =>
        IPlatformApplication.Current?.Services.GetService<ISecondaryDisplayHost>() as AndroidSecondaryDisplayHost;

    /// <summary>Console apps are fullscreen: hide status/navigation bars (swipe reveals them transiently).</summary>
    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (!hasFocus || Window is not { DecorView: { } decorView } window) return;
        if (AndroidX.Core.View.WindowCompat.GetInsetsController(window, decorView) is not { } controller) return;
        controller.SystemBarsBehavior = AndroidX.Core.View.WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        controller.Hide(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is null) return base.DispatchKeyEvent(e);

        if (ResolveButton(e.KeyCode) is { } button)
        {
            if (e.Action == KeyEventActions.Down)
            {
                // Direction and L/R repeats are owned by the pump's timers; framework
                // repeats would stack on top and double the navigation speed.
                if (e.RepeatCount > 0)
                    return Services.PadInputPump.IsRepeatable(button);

                if (GetPump()?.Press(button) == true)
                    return true;
            }
            else if (e.Action == KeyEventActions.Up)
            {
                GetPump()?.Release(button);
            }
        }
        return base.DispatchKeyEvent(e);
    }

    public override bool OnGenericMotionEvent(MotionEvent? e)
    {
        if (e is { Action: MotionEventActions.Move })
        {
            var x = e.GetAxisValue(Axis.HatX);
            var y = e.GetAxisValue(Axis.HatY);
            if (GetPump()?.Motion(x, y) == true)
                return true;
        }
        return base.OnGenericMotionEvent(e);
    }

    private static Services.PadInputPump? GetPump() =>
        IPlatformApplication.Current?.Services.GetService<Services.PadInputPump>();

    private static Services.PadButton? ResolveButton(Keycode code) => code switch
    {
        Keycode.ButtonA or Keycode.Enter or Keycode.DpadCenter => Services.PadButton.A,
        Keycode.ButtonB or Keycode.Back => Services.PadButton.B,
        Keycode.ButtonX => Services.PadButton.X,
        Keycode.ButtonY => Services.PadButton.Y,
        Keycode.ButtonStart or Keycode.Menu => Services.PadButton.Start,
        Keycode.ButtonSelect => Services.PadButton.Select,
        Keycode.ButtonL1 or Keycode.Button5 => Services.PadButton.L,
        Keycode.ButtonR1 or Keycode.Button6 => Services.PadButton.R,
        Keycode.DpadUp => Services.PadButton.Up,
        Keycode.DpadDown => Services.PadButton.Down,
        Keycode.DpadLeft => Services.PadButton.Left,
        Keycode.DpadRight => Services.PadButton.Right,
        _ => null,
    };

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        var services = IPlatformApplication.Current?.Services;
        var filePicker = services?.GetService<IDocumentPicker>() as AndroidDocumentPicker;
        var folderPicker = services?.GetService<IFolderPicker>() as AndroidFolderPicker;
        if (filePicker?.HandleActivityResult(requestCode, resultCode, data) != true
            && folderPicker?.HandleActivityResult(requestCode, resultCode, data) != true)
            base.OnActivityResult(requestCode, resultCode, data);
    }
}

/// <summary>
/// Renders the second-screen box mirror via DisplayManager + Presentation.
/// The AYN Thor does NOT tag its bottom screen as a presentation-category display,
/// so detection falls back to GetDisplays()[1] when the category query is empty.
/// </summary>
public sealed class AndroidSecondaryDisplayHost(IServiceProvider services) : ISecondaryDisplayHost
{
    private readonly object _showGate = new();
    private PagePresentation? _presentation;
    private ContentPage? _page;
    private bool _resumeAfterActivityPause;

    public bool IsAvailable => Services.SecondScreenMode.Allowed && ResolveDisplay() is not null;

    /// <summary>True on a dual-screen handheld when PKForge runs on a panel other than the main one.</summary>
    internal static bool LaunchedOnLowerScreen(Activity activity) =>
        OperatingSystem.IsAndroidVersionAtLeast(30)
        && activity.Display is { } display && display.DisplayId != Display.DefaultDisplay
        && DualScreenMakers.Any(m => (Android.OS.Build.Manufacturer ?? "").Contains(m, StringComparison.OrdinalIgnoreCase));

    public ValueTask ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var display = ResolveDisplay() ?? throw new InvalidOperationException("No secondary display is available.");
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No foreground Android activity is available.");

        lock (_showGate)
        {
            if (_presentation is { IsShowing: true })
            {
                if (_page is Views.PokeparkJournalPage journalPage)
                    _ = journalPage.RefreshAsync(cancellationToken);
                return ValueTask.CompletedTask;
            }

            // A dismissed presentation (SAF picker, sleep) cannot be reshown;
            // rebuild the page and presentation as one serialized operation.
            _presentation?.Dismiss();
            if (_page is Views.SecondScreenBoxPage boxPage)
                boxPage.Cleanup();
            else if (_page is Views.PokeparkJournalPage journalPage)
                journalPage.Cleanup();
            _presentation = null;
            _page = null;
            if (!Services.SecondScreenMode.Allowed) return ValueTask.CompletedTask;

            // Built and shown as locals: a display Android refuses (a virtual or removed one
            // raises BadToken/InvalidDisplay) leaves nothing half-built, and PKForge carries
            // on with one screen instead of crashing.
            var page = services.GetRequiredService<Views.SecondScreenBoxPage>();
            var presentation = new PagePresentation(activity, display, page, services);
            try
            {
                presentation.Show();
            }
            catch (Exception error) when (error is WindowManagerBadTokenException or WindowManagerInvalidDisplayException or Java.Lang.Exception)
            {
                page.Cleanup();
                Services.SecondScreenMode.DisableForSession($"display {display.DisplayId} ({display.Name}) refused the lower screen", error);
                return ValueTask.CompletedTask;
            }
            _page = page;
            _presentation = presentation;
            Services.AppLog.Info("second", $"Lower screen on display {display.DisplayId} ({display.Name}, flags 0x{(int)display.Flags:X})");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ShowPokeparkJournalAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The lower display is created by HomePage. Never construct a cold Android
        // Presentation while navigating into Poképark: Presentation/MAUI inflation
        // is synchronous on the UI thread and can stall the primary window.
        lock (_showGate)
        {
            if (_presentation is { IsShowing: true })
            {
                if (_page is Views.SecondScreenBoxPage boxPage)
                    _ = boxPage.RefreshPokeparkJournalAsync(cancellationToken);
                else if (_page is Views.PokeparkJournalPage journalPage)
                    _ = journalPage.RefreshAsync(cancellationToken);
                return ValueTask.CompletedTask;
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask RefreshPokeparkJournalAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_showGate)
        {
            if (_presentation is not { IsShowing: true } || _page is not Views.PokeparkJournalPage)
                return ValueTask.CompletedTask;
            return ((Views.PokeparkJournalPage)_page).RefreshAsync(cancellationToken);
        }
    }

    public ValueTask DismissAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dismiss();
        _resumeAfterActivityPause = false;
        StopWaitingForDisplay();
        return ValueTask.CompletedTask;
    }

    internal void SuspendForActivityPause()
    {
        // Not IsShowing: going to sleep turns the lower display off, and Android may already
        // have dismissed the Presentation by the time the activity pauses.
        _resumeAfterActivityPause |= _presentation is not null;
        StopWaitingForDisplay();
        Dismiss();
    }

    internal void ResumeAfterActivityPause()
    {
        if (!_resumeAfterActivityPause)
            return;
        if (ResolveDisplay() is null)
        {
            // Waking from sleep resumes the activity before the lower display is back on:
            // wait for it instead of giving the lower screen up.
            WaitForDisplay();
            return;
        }

        _resumeAfterActivityPause = false;
        StopWaitingForDisplay();
        try { _ = ShowAsync(); }
        catch { /* A removed or unavailable secondary display must not break resume. */ }
    }

    private DisplayWaiter? _displayWaiter;

    private void WaitForDisplay()
    {
        if (_displayWaiter is not null) return;
        if (Platform.AppContext.GetSystemService(Android.Content.Context.DisplayService) is not DisplayManager manager) return;
        _displayWaiter = new DisplayWaiter(manager, ResumeAfterActivityPause);
        manager.RegisterDisplayListener(_displayWaiter, new Handler(Looper.MainLooper!));
    }

    private void StopWaitingForDisplay()
    {
        if (_displayWaiter is null) return;
        _displayWaiter.Manager.UnregisterDisplayListener(_displayWaiter);
        _displayWaiter = null;
    }

    private sealed class DisplayWaiter(DisplayManager manager, Action retry) : Java.Lang.Object, DisplayManager.IDisplayListener
    {
        public DisplayManager Manager => manager;
        public void OnDisplayAdded(int displayId) => retry();
        public void OnDisplayChanged(int displayId) => retry();
        public void OnDisplayRemoved(int displayId) { }
    }

    private void Dismiss()
    {
        lock (_showGate)
        {
            _presentation?.Dismiss();
            _presentation = null;
            if (_page is Views.SecondScreenBoxPage boxPage)
                boxPage.Cleanup();
            else if (_page is Views.PokeparkJournalPage journalPage)
                journalPage.Cleanup();
            _page = null;
        }
    }

    // Dual-screen handhelds whose built-in lower panel is not tagged as a presentation
    // display (the AYN Thor's is not): only on these may an untagged display be used.
    private static readonly string[] DualScreenMakers = ["AYN", "AYANEO", "Retroid", "Anbernic"];

    // Names Android gives displays that are not a second panel of this device: casting,
    // screen recording, desktop modes (Motorola Ready For, Samsung DeX), developer overlays.
    private static readonly string[] VirtualHints = ["virtual", "overlay", "cast", "wifi", "wireless", "ready for", "dex", "mirror", "record"];

    /// <summary>
    /// The display for the lower screen, or null. Never the display PKForge itself is on,
    /// never an invalid, off or private one, and never one whose name says it is virtual:
    /// phones expose casting and desktop-mode displays that a Presentation cannot live on.
    /// </summary>
    private static Display? ResolveDisplay()
    {
        var manager = (DisplayManager?)Platform.AppContext.GetSystemService(Android.Content.Context.DisplayService);
        if (manager is null) return null;
        // Before Android 11 an activity cannot say its display; it is then the default one.
        var own = OperatingSystem.IsAndroidVersionAtLeast(30)
            ? Platform.CurrentActivity?.Display?.DisplayId ?? Display.DefaultDisplay
            : Display.DefaultDisplay;
        // Running on the lower panel: the main one must never become the "second screen".
        if (Platform.CurrentActivity is { } current && LaunchedOnLowerScreen(current)) return null;

        bool Usable(Display d) =>
            d.DisplayId != own && d.IsValid && d.State != DisplayState.Off
            && (d.Flags & DisplayFlags.Private) == 0
            && !VirtualHints.Any(hint => (d.Name ?? "").Contains(hint, StringComparison.OrdinalIgnoreCase));

        var presentation = manager.GetDisplays(DisplayManager.DisplayCategoryPresentation)?.FirstOrDefault(Usable);
        if (presentation is not null)
            return presentation;

        // Thor fallback: its built-in bottom screen is not presentation-tagged.
        var maker = Android.OS.Build.Manufacturer ?? "";
        if (!DualScreenMakers.Any(m => maker.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return null;
        return manager.GetDisplays()?.FirstOrDefault(Usable);
    }

    private sealed class PagePresentation(Activity activity, Display display, ContentPage page, IServiceProvider services)
        : Presentation(activity, display)
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Window?.AddFlags(WindowManagerFlags.Fullscreen);
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
            // Touch on the lower screen must not steal key focus from the main window: a focused
            // Presentation took the pad's B/back itself and dismissed as a dialog, leaving the
            // launcher on the lower screen. It still receives touch while unfocusable.
            Window?.AddFlags(WindowManagerFlags.NotFocusable);

            // Inflate with the Activity as context (not the Presentation's dialog context)
            // so MAUI handlers resolve fonts/drawables registered against the Activity.
            var mauiContext = new Microsoft.Maui.MauiContext(services, activity);
            SetContentView(Microsoft.Maui.Platform.ElementExtensions.ToPlatform(page, mauiContext));
        }

        // Belt and braces for keys that still land here: they belong to the main activity.
        public override bool DispatchKeyEvent(KeyEvent e) => activity.DispatchKeyEvent(e);
    }
}
