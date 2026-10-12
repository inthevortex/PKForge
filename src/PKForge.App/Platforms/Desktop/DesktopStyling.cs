using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls.Maui.Handlers;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PKForge.App.Theme;
using PKForge.App.Views;
using PKForge.Chrome;
using ABorder = Avalonia.Controls.Border;
using AButton = Avalonia.Controls.Button;
using AColor = Avalonia.Media.Color;
using AContentPresenter = Avalonia.Controls.Presenters.ContentPresenter;
using ACornerRadius = Avalonia.CornerRadius;
using AGradientStop = Avalonia.Media.GradientStop;
using AImage = Avalonia.Controls.Image;
using ALinearGradientBrush = Avalonia.Media.LinearGradientBrush;
using APanel = Avalonia.Controls.Panel;
using ASolidColorBrush = Avalonia.Media.SolidColorBrush;
using AStackPanel = Avalonia.Controls.StackPanel;
using ATextBlock = Avalonia.Controls.TextBlock;
using AThickness = Avalonia.Thickness;
using MButton = Microsoft.Maui.Controls.Button;
using MColor = Microsoft.Maui.Graphics.Color;
using MColors = Microsoft.Maui.Graphics.Colors;
using MLabel = Microsoft.Maui.Controls.Label;

namespace PKForge.App.Platforms.Desktop;

/// <summary>
/// Desktop UI styling: hooks Avalonia mappers for pixel-accurate buttons (CapsuleSkin)
/// and vertical text alignment on labels.
/// </summary>
public static class DesktopStyling
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        LabelHandler.Mapper.AppendToMapping(nameof(MLabel.VerticalTextAlignment), (handler, view) =>
        {
            if (handler.PlatformView is ATextBlock textBlock)
            {
                textBlock.VerticalAlignment = view.VerticalTextAlignment switch
                {
                    Microsoft.Maui.TextAlignment.Start => Avalonia.Layout.VerticalAlignment.Top,
                    Microsoft.Maui.TextAlignment.Center => Avalonia.Layout.VerticalAlignment.Center,
                    Microsoft.Maui.TextAlignment.End => Avalonia.Layout.VerticalAlignment.Bottom,
                    _ => Avalonia.Layout.VerticalAlignment.Center,
                };
            }
        });

        ButtonHandler.Mapper.AppendToMapping(nameof(Microsoft.Maui.IButton.Background), ApplyButtonSkin);
        ButtonHandler.Mapper.AppendToMapping(nameof(Microsoft.Maui.IButtonStroke.StrokeColor), ApplyButtonSkin);
        ButtonHandler.Mapper.AppendToMapping(nameof(Microsoft.Maui.IButtonStroke.StrokeThickness), ApplyButtonSkin);
        ButtonHandler.Mapper.AppendToMapping(nameof(Microsoft.Maui.IButtonStroke.CornerRadius), ApplyButtonSkin);
        ButtonHandler.Mapper.AppendToMapping(nameof(Microsoft.Maui.IView.IsEnabled), ApplyButtonSkin);
        ButtonHandler.Mapper.AppendToMapping(nameof(Microsoft.Maui.ITextStyle.TextColor), ApplyButtonSkin);
        ButtonHandler.Mapper.AppendToMapping(nameof(Microsoft.Maui.IPadding.Padding), ApplyButtonSkin);

        RegisterMissingViewMappings();
    }

    private static void RegisterMissingViewMappings()
    {
        var mapH = typeof(ViewHandler).GetMethod("MapHorizontalLayoutAlignment", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var mapV = typeof(ViewHandler).GetMethod("MapVerticalLayoutAlignment", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var mapW = typeof(ViewHandler).GetMethod("MapWidth", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var mapHgt = typeof(ViewHandler).GetMethod("MapHeight", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var mapMinW = typeof(ViewHandler).GetMethod("MapMinimumWidth", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var mapMinH = typeof(ViewHandler).GetMethod("MapMinimumHeight", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var mapMaxW = typeof(ViewHandler).GetMethod("MapMaximumWidth", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var mapMaxH = typeof(ViewHandler).GetMethod("MapMaximumHeight", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var mapMargin = typeof(ViewHandler).GetMethod("MapMargin", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var mapVis = typeof(ViewHandler).GetMethod("MapVisibility", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var mapOpac = typeof(ViewHandler).GetMethod("MapOpacity", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        void AddMappings<TVirtual, THandler>(Microsoft.Maui.IPropertyMapper<TVirtual, THandler> mapper)
            where TVirtual : Microsoft.Maui.IElement
            where THandler : Microsoft.Maui.IElementHandler
        {
            if (mapH != null)
            {
                mapper.AppendToMapping(nameof(Microsoft.Maui.IView.HorizontalLayoutAlignment), (h, v) => mapH.Invoke(null, [h, v]));
                mapper.AppendToMapping("HorizontalOptions", (h, v) => mapH.Invoke(null, [h, v]));
            }
            if (mapV != null)
            {
                mapper.AppendToMapping(nameof(Microsoft.Maui.IView.VerticalLayoutAlignment), (h, v) => mapV.Invoke(null, [h, v]));
                mapper.AppendToMapping("VerticalOptions", (h, v) => mapV.Invoke(null, [h, v]));
            }
            if (mapW != null)
            {
                mapper.AppendToMapping(nameof(Microsoft.Maui.IView.Width), (h, v) => mapW.Invoke(null, [h, v]));
                mapper.AppendToMapping("WidthRequest", (h, v) => mapW.Invoke(null, [h, v]));
            }
            if (mapHgt != null)
            {
                mapper.AppendToMapping(nameof(Microsoft.Maui.IView.Height), (h, v) => mapHgt.Invoke(null, [h, v]));
                mapper.AppendToMapping("HeightRequest", (h, v) => mapHgt.Invoke(null, [h, v]));
            }
            if (mapMinW != null)
            {
                mapper.AppendToMapping(nameof(Microsoft.Maui.IView.MinimumWidth), (h, v) => mapMinW.Invoke(null, [h, v]));
                mapper.AppendToMapping("MinimumWidthRequest", (h, v) => mapMinW.Invoke(null, [h, v]));
            }
            if (mapMinH != null)
            {
                mapper.AppendToMapping(nameof(Microsoft.Maui.IView.MinimumHeight), (h, v) => mapMinH.Invoke(null, [h, v]));
                mapper.AppendToMapping("MinimumHeightRequest", (h, v) => mapMinH.Invoke(null, [h, v]));
            }
            if (mapMaxW != null)
            {
                mapper.AppendToMapping(nameof(Microsoft.Maui.IView.MaximumWidth), (h, v) => mapMaxW.Invoke(null, [h, v]));
                mapper.AppendToMapping("MaximumWidthRequest", (h, v) => mapMaxW.Invoke(null, [h, v]));
            }
            if (mapMaxH != null)
            {
                mapper.AppendToMapping(nameof(Microsoft.Maui.IView.MaximumHeight), (h, v) => mapMaxH.Invoke(null, [h, v]));
                mapper.AppendToMapping("MaximumHeightRequest", (h, v) => mapMaxH.Invoke(null, [h, v]));
            }
            if (mapMargin != null) mapper.AppendToMapping(nameof(Microsoft.Maui.IView.Margin), (h, v) => mapMargin.Invoke(null, [h, v]));
            if (mapVis != null) mapper.AppendToMapping(nameof(Microsoft.Maui.IView.Visibility), (h, v) => mapVis.Invoke(null, [h, v]));
            if (mapOpac != null) mapper.AppendToMapping(nameof(Microsoft.Maui.IView.Opacity), (h, v) => mapOpac.Invoke(null, [h, v]));
        }

        AddMappings(BorderHandler.Mapper);
        AddMappings(Avalonia.Controls.Maui.SkiaSharp.Views.Handlers.SKCanvasViewHandler.Mapper);
    }

    private static readonly ConditionalWeakTable<AButton, DesktopSkinAttachment> Skins = new();

    private static void ApplyButtonSkin(ButtonHandler handler, Microsoft.Maui.IButton view)
    {
        if (view is not MButton button || !CapsuleSkin.TryGetState(button, out var state)) return;
        if (handler.PlatformView is not AButton native) return;

        if (!Skins.TryGetValue(native, out var attachment))
        {
            attachment = new DesktopSkinAttachment(native);
            Skins.Add(native, attachment);
        }

        attachment.Update(button, state);
    }

    private sealed class DesktopSkinAttachment
    {
        private readonly AButton _native;
        private readonly ABorder _shadowBorder;
        private readonly ABorder _bodyBorder;
        private readonly ABorder _lightBorder;
        private readonly ABorder _stripBorder;
        private readonly AImage _iconImage;
        private readonly AContentPresenter _presenter;
        private readonly AStackPanel _contentStack;

        private double _drop = CapsuleSkin.Drop;
        private IBrush _restGradient = Brushes.Transparent;
        private IBrush _pressedGradient = Brushes.Transparent;

        public DesktopSkinAttachment(AButton native)
        {
            _native = native;

            _shadowBorder = new ABorder
            {
                Name = "PART_CapsuleShadow",
                IsHitTestVisible = false,
            };

            _lightBorder = new ABorder
            {
                Name = "PART_CapsuleLight",
                Height = 1,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                IsHitTestVisible = false,
            };

            _stripBorder = new ABorder
            {
                Name = "PART_CapsuleStrip",
                Width = 3,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                IsHitTestVisible = false,
            };

            _iconImage = new AImage
            {
                Name = "PART_CapsuleIcon",
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                IsHitTestVisible = false,
            };

            _presenter = new AContentPresenter
            {
                Name = "PART_ContentPresenter",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                RecognizesAccessKey = true,
            };

            _contentStack = new AStackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children = { _iconImage, _presenter },
            };

            var bodyInner = new APanel
            {
                Children = { _lightBorder, _stripBorder, _contentStack },
            };

            _bodyBorder = new ABorder
            {
                Name = "PART_CapsuleBody",
                IsHitTestVisible = false,
                Child = bodyInner,
            };

            var root = new APanel
            {
                Children = { _shadowBorder, _bodyBorder },
            };

            _native.Template = new FuncControlTemplate<AButton>((_, scope) =>
            {
                scope.Register("PART_ContentPresenter", _presenter);
                return root;
            });

            _native.Background = Brushes.Transparent;
            _native.BorderThickness = new AThickness(0);
            _native.Padding = new AThickness(0);

            _native.PropertyChanged += (_, e) =>
            {
                if (e.Property == AButton.IsPressedProperty && e.NewValue is bool isPressed)
                {
                    UpdatePressed(isPressed);
                }
            };
        }

        public void Update(MButton button, CapsuleSkin.State state)
        {
            var enabled = button.IsEnabled;
            var body = button.BackgroundColor ?? UiTokens.ButtonTop;
            var edge = button.BorderColor ?? UiTokens.ButtonEdge;
            if (!enabled)
            {
                body = MColor.FromRgba((body.Red + Grey(body).Red) / 2 * 0.8f,
                    (body.Green + Grey(body).Green) / 2 * 0.8f,
                    (body.Blue + Grey(body).Blue) / 2 * 0.8f, 1f);
                edge = UiTokens.ButtonEdge.WithAlpha(0.5f);
            }

            var radius = button.CornerRadius >= 0 ? button.CornerRadius : UiTokens.ControlRadius;
            var stroke = Math.Max(1, button.BorderWidth > 0 ? button.BorderWidth : UiTokens.ControlEdge);
            var drop = CapsuleSkin.Drop + (state.Focused ? 1 : 0);

            _drop = drop;

            var restTop = Shade(body, enabled ? 0.16f : 0.03f);
            var restBottom = Shade(body, -0.14f);

            var pressedTop = Shade(body, -0.02f);
            var pressedBottom = Shade(body, -0.12f);

            _restGradient = MakeVerticalGradient(restTop, restBottom);
            _pressedGradient = MakeVerticalGradient(pressedTop, pressedBottom);

            var shadowColor = UiTokens.PanelShadow.WithAlpha(enabled ? 0.9f : 0.45f);
            var lightColor = UiTokens.Bright.WithAlpha(!enabled ? 0.06f : state.Primary ? 0.34f : 0.22f);

            _shadowBorder.CornerRadius = new ACornerRadius(radius);
            _shadowBorder.Background = ToSolidBrush(shadowColor);
            _shadowBorder.Margin = new AThickness(0, drop, 0, 0);

            _bodyBorder.CornerRadius = new ACornerRadius(radius);
            _bodyBorder.BorderThickness = new AThickness(stroke);
            _bodyBorder.BorderBrush = ToSolidBrush(edge);

            _lightBorder.Margin = new AThickness(stroke + radius / 2, 0, stroke + radius / 2, 0);
            _lightBorder.Background = ToSolidBrush(lightColor);

            var stripColor = state.Strip is { } sc && !state.Focused ? (enabled ? sc : sc.WithAlpha(0.35f)) : MColors.Transparent;
            if (stripColor.Alpha > 0.01f)
            {
                _stripBorder.IsVisible = true;
                _stripBorder.Background = ToSolidBrush(stripColor);
                var innerR = Math.Max(0, radius - stroke);
                _stripBorder.CornerRadius = new ACornerRadius(innerR, 0, 0, innerR);
            }
            else
            {
                _stripBorder.IsVisible = false;
            }

            if (state.Icon is { } icon)
            {
                _iconImage.IsVisible = true;
                var neutral = button.BackgroundColor is null || Near(button.BackgroundColor, UiTokens.ButtonTop);
                var tint = neutral && !state.Focused ? PksmIcons.Cyan : PksmIcons.White;
                var px = (int)Math.Round(button.FontSize + 3);
                _iconImage.Width = px;
                _iconImage.Height = px;
                _iconImage.Margin = new AThickness(0, 0, 6, 0);
                _iconImage.Source = GetIconBitmap(icon, tint);
                _iconImage.Opacity = enabled ? 1.0 : 0.45;
            }
            else
            {
                _iconImage.IsVisible = false;
            }

            var ink = button.TextColor ?? UiTokens.Ink0;
            var activeInk = enabled ? ink : ink.WithAlpha(0.45f);
            _native.Foreground = ToSolidBrush(activeInk);

            var p = button.Padding;
            _contentStack.Margin = new AThickness(p.Left, p.Top, p.Right, p.Bottom);

            UpdatePressed(_native.IsPressed);
        }

        private void UpdatePressed(bool isPressed)
        {
            if (isPressed)
            {
                _bodyBorder.Margin = new AThickness(0, _drop, 0, 0);
                _bodyBorder.Background = _pressedGradient;
                _lightBorder.Opacity = 0.3;
            }
            else
            {
                _bodyBorder.Margin = new AThickness(0, 0, 0, _drop);
                _bodyBorder.Background = _restGradient;
                _lightBorder.Opacity = 1.0;
            }
        }
    }

    private static readonly Dictionary<string, Bitmap> Bitmaps = new(StringComparer.Ordinal);

    private static Bitmap? GetIconBitmap(string icon, string tint)
    {
        var key = $"{icon}|{tint}|{ColorTheme.Version}";
        if (!Bitmaps.TryGetValue(key, out var bmp))
        {
            try
            {
                var png = PksmIcons.GetPng(icon, tint);
                using var ms = new MemoryStream(png);
                bmp = new Bitmap(ms);
                Bitmaps[key] = bmp;
            }
            catch
            {
                return null;
            }
        }
        return bmp;
    }

    private static bool Near(MColor a, MColor b) =>
        Math.Abs(a.Red - b.Red) < 0.01f && Math.Abs(a.Green - b.Green) < 0.01f && Math.Abs(a.Blue - b.Blue) < 0.01f;

    private static MColor Shade(MColor c, float t) =>
        t >= 0 ? new MColor(c.Red + (1 - c.Red) * t, c.Green + (1 - c.Green) * t, c.Blue + (1 - c.Blue) * t, c.Alpha)
               : new MColor(c.Red * (1 + t), c.Green * (1 + t), c.Blue * (1 + t), c.Alpha);

    private static MColor Grey(MColor c) { var l = c.Red * 0.3f + c.Green * 0.59f + c.Blue * 0.11f; return new MColor(l, l, l); }

    private static AColor ToAvaColor(MColor c) =>
        AColor.FromArgb((byte)Math.Round(c.Alpha * 255), (byte)Math.Round(c.Red * 255), (byte)Math.Round(c.Green * 255), (byte)Math.Round(c.Blue * 255));

    private static ASolidColorBrush ToSolidBrush(MColor c) => new(ToAvaColor(c));

    private static ALinearGradientBrush MakeVerticalGradient(MColor top, MColor bottom) =>
        new()
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new AGradientStop(ToAvaColor(top), 0),
                new AGradientStop(ToAvaColor(bottom), 1),
            },
        };
}
