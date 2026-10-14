using System.Runtime.CompilerServices;
using Android.Animation;
using Android.Views;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using AView = Android.Views.View;

namespace PKForge.App.Platforms.Android;

/// <summary>
/// Replaces the ScrollView handler's scroll-request mapping on Android.
/// </summary>
/// <remarks>
/// MAUI 11 RC1 defers a programmatic scroll by re-posting it until
/// <c>IsLaidOut &amp;&amp; !IsLayoutRequested</c>. Android can leave <c>IsLayoutRequested</c> set
/// with no traversal coming (a request swallowed during a layout-inside-layout pass), and then
/// every <c>ScrollToAsync</c> spins on the UI thread forever without scrolling. Here a view that
/// has been laid out is scrolled at once, and one that has not waits for its first layout, with
/// a timeout so a request is always served.
/// <para>
/// MAUI's own smooth scroll runs a one-second animator that nothing can cancel, so an instant
/// scroll issued during it is overwritten on the next frame. Smooth scrolls run here instead,
/// and every new request stops the one in flight. Each request completes the awaited
/// <c>ScrollToAsync</c> exactly once unless a newer request on the same view replaces it,
/// in which case the newer one completes it.
/// </para>
/// </remarks>
public static class AndroidScrollTo
{
    private const int LayoutWaitTimeoutMs = 4000;

    // Same duration as MauiScrollView's built-in smooth scroll.
    private const long SmoothScrollDurationMs = 1000;

    private static readonly ConditionalWeakTable<MauiScrollView, ScrollState> States = new();

    public static void Register() =>
        ScrollViewHandler.CommandMapper.ModifyMapping<IScrollView, IScrollViewHandler>(
            nameof(IScrollView.RequestScrollTo),
            (handler, view, args, _) => MapRequestScrollTo(handler, view, args));

    private static void MapRequestScrollTo(IScrollViewHandler handler, IScrollView view, object? args)
    {
        if (args is not ScrollToRequest request) return;
        if (handler.PlatformView is not { } platformView)
        {
            view.ScrollFinished();
            return;
        }

        var state = States.GetOrCreateValue(platformView);
        var generation = state.Supersede();
        if (platformView.IsLaidOut)
            Serve(platformView, view, request, state);
        else
            WaitForFirstLayout(platformView, handler, view, request, state, generation);
    }

    private static void Serve(MauiScrollView platformView, IScrollView view, ScrollToRequest request, ScrollState state)
    {
        if (platformView.Context is not { } context)
        {
            view.ScrollFinished();
            return;
        }

        var x = (int)context.ToPixels(request.HorizontalOffset);
        var y = (int)context.ToPixels(request.VerticalOffset);
        if (request.Instant)
        {
            platformView.ScrollTo(x, y, true, view.ScrollFinished);
            return;
        }

        // The handler keeps these offsets current on every scroll change, in either orientation.
        var fromX = (int)context.ToPixels(view.HorizontalOffset);
        var fromY = (int)context.ToPixels(view.VerticalOffset);
        state.Animate(platformView, fromX, fromY, x, y, view.ScrollFinished);
    }

    private static void WaitForFirstLayout(
        MauiScrollView platformView, IScrollViewHandler handler, IScrollView view,
        ScrollToRequest request, ScrollState state, int generation)
    {
        var waiter = new LayoutWaiter(platformView, handler, view, request, state, new(state.Requests, generation));
        if (platformView.ViewTreeObserver is { IsAlive: true } observer)
            observer.AddOnGlobalLayoutListener(waiter);
        platformView.PostDelayed(waiter.Fire, LayoutWaitTimeoutMs);

        // A set IsLayoutRequested flag is not proof that a traversal is coming. Requesting again
        // from outside the current pass, on this view and every ancestor, re-arms any latched
        // ancestor and reaches the view root, which schedules one.
        platformView.Post(() =>
        {
            if (platformView.IsLaidOut) return;
            for (AView? current = platformView; current is not null; current = current.Parent as AView)
                current.RequestLayout();
        });
    }

    private sealed class ScrollState
    {
        private static readonly Action Ignore = static () => { };

        public ScrollRequestTracker<ValueAnimator> Requests { get; } = new();

        /// <summary>Stops the smooth scroll in flight; its completion passes to the new request.</summary>
        public int Supersede()
        {
            var generation = Requests.Supersede(out var running);
            running?.Cancel();
            return generation;
        }

        public void Animate(MauiScrollView platformView, int fromX, int fromY, int toX, int toY, Action finished)
        {
            if (ValueAnimator.OfFloat(0f, 1f) is not { } animation)
            {
                platformView.ScrollTo(toX, toY, true, finished);
                return;
            }

            animation.SetDuration(SmoothScrollDurationMs);
            animation.Update += (_, e) =>
            {
                var t = e.Animation.AnimatedFraction;
                platformView.ScrollTo(Lerp(fromX, toX, t), Lerp(fromY, toY, t), true, Ignore);
            };
            // Also raised by Cancel, after Supersede has already released the animation.
            animation.AnimationEnd += (_, _) =>
            {
                if (Requests.Ended(animation)) finished();
            };
            Requests.Started(animation);
            animation.Start();
        }

        private static int Lerp(int from, int to, float t) => (int)(from + (to - from) * t);
    }

    private sealed class LayoutWaiter(
        MauiScrollView platformView, IScrollViewHandler handler, IScrollView view,
        ScrollToRequest request, ScrollState state, ScrollLayoutWait<ValueAnimator> wait)
        : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        // Fires for every layout in the window, not only this view's.
        public void OnGlobalLayout()
        {
            if (platformView.IsLaidOut) Fire();
        }

        public void Fire()
        {
            if (!wait.TryFire()) return;
            if (platformView.ViewTreeObserver is { IsAlive: true } observer)
                observer.RemoveOnGlobalLayoutListener(this);

            switch (wait.Resolve(ReferenceEquals(handler.PlatformView, platformView)))
            {
                case ScrollWaitOutcome.Finish:
                    view.ScrollFinished();
                    break;
                case ScrollWaitOutcome.Serve:
                    Serve(platformView, view, request, state);
                    break;
            }
        }
    }
}
