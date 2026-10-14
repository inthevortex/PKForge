using PKForge.App.Theme;

namespace PKForge.App.Views;

/// <summary>
/// The cute waiting screen: a Pokémon strolls while progress ticks up.
/// Non-blocking cancel; closes itself when the caller reports completion.
/// </summary>
public sealed class LoadingOverlay
{
    private readonly Grid _host;
    private readonly Grid _overlay;
    private readonly Label _progress;
    private readonly ProgressBar _bar;
    private PadOverlay? _pad;

    public CancellationTokenSource Cancellation { get; } = new();

    public static LoadingOverlay Show(Grid host, string title, string subtitle) => new(host, title, subtitle);

    private LoadingOverlay(Grid host, string title, string subtitle)
    {
        _host = host;
        _progress = new Label
        {
            TextColor = UiTokens.Ink0,
            FontFamily = DsChrome.PixelFont,
            FontSize = 14,
            HorizontalTextAlignment = TextAlignment.Center,
            Text = "Starting…",
        };
        _bar = new ProgressBar { ProgressColor = UiTokens.Green };

        var cancel = Kit.Capsule("Cancel", UiTokens.Ink1);
        cancel.HorizontalOptions = LayoutOptions.Center;
        cancel.Clicked += (_, _) => Cancellation.Cancel();

        var content = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Kit.HeaderBar(title),
                new Label { Text = subtitle, TextColor = UiTokens.InkSoft, FontSize = UiTokens.TextBody, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap },
                Kit.WalkerStrip(34),
                _bar,
                _progress,
                cancel,
            },
        };

        var window = Kit.OverlayWindow(host, content, preferredMaxWidth: 440, padding: 18);
        _overlay = Kit.AttachOverlay(host, window, () => Cancellation.Cancel());
        _pad = new PadOverlay(() => Cancellation.Cancel(), () => Cancellation.Cancel());
    }

    public void Report(int done, int total) => Report($"{done} / {total}", total == 0 ? 0 : (double)done / total);

    private const long ReportIntervalMs = 100;
    private readonly ProgressCoalescer _updates = new(ReportIntervalMs, () => Environment.TickCount64);

    /// <summary>
    /// Free-form progress ("Downloading 12 / 240 MB") with a 0-1 bar. Reports from any thread
    /// coalesce into one pending update, at most every <see cref="ReportIntervalMs"/>, that shows
    /// the latest: a fast worker cannot queue enough of them to starve the UI thread's input,
    /// the Cancel button among it.
    /// </summary>
    public void Report(string text, double fraction)
    {
        if (_updates.Offer(text, fraction) is not { } wait) return;
        if (wait > 0)
            _ = Task.Delay(TimeSpan.FromMilliseconds(wait)).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(ShowLatest), TaskScheduler.Default);
        else
            MainThread.BeginInvokeOnMainThread(ShowLatest);
    }

    private void ShowLatest()
    {
        var latest = _updates.Take();
        _progress.Text = latest.Text;
        _bar.Progress = Math.Clamp(latest.Fraction, 0, 1);
    }

    public void Close() => MainThread.BeginInvokeOnMainThread(() =>
    {
        _host.Remove(_overlay);
        _pad?.Dispose();
    });
}
