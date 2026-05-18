using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace OpenHab.Windows.Tray.Voice;

public sealed class VoiceListeningOverlayWindow : Window
{
    private readonly DispatcherTimer animationTimer = new();
    private readonly Ellipse pulseRing;
    private readonly ScaleTransform pulseScale;
    private readonly FontIcon microphoneIcon;
    private readonly TextBlock statusText;
    private double animationPhase;
    private double activityBoost;

    public VoiceListeningOverlayWindow()
    {
        Title = "Voice command";

        pulseScale = new ScaleTransform
        {
            ScaleX = 0.8d,
            ScaleY = 0.8d
        };
        pulseRing = new Ellipse
        {
            Width = 82,
            Height = 82,
            Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
            StrokeThickness = 3,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5d, 0.5d),
            RenderTransform = pulseScale
        };
        microphoneIcon = new FontIcon
        {
            Glyph = "\uE720",
            FontSize = 34,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
        };
        statusText = new TextBlock
        {
            Text = "Listening...",
            TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            Opacity = 0.88
        };

        Content = BuildContent();
        ConfigureWindow();

        animationTimer.Interval = TimeSpan.FromMilliseconds(80);
        animationTimer.Tick += AnimationTimer_Tick;
        Closed += (_, _) => animationTimer.Stop();
    }

    public void ShowOverlay()
    {
        Activate();
        PositionNearTopCenter();
    }

    public void SetListening(bool isListening)
    {
        animationPhase = 0d;
        activityBoost = isListening ? 1d : 0d;
        if (isListening)
        {
            animationTimer.Start();
            UpdateVisual();
            return;
        }

        animationTimer.Stop();
    }

    public void PulseVoiceActivity()
    {
        activityBoost = 1d;
        UpdateVisual();
    }

    public void SetStatus(string text)
    {
        statusText.Text = string.IsNullOrWhiteSpace(text) ? "Listening..." : text;
    }

    private UIElement BuildContent()
    {
        var iconHost = new Grid
        {
            Width = 92,
            Height = 92,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                pulseRing,
                new Border
                {
                    Width = 58,
                    Height = 58,
                    CornerRadius = new CornerRadius(29),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
                    Child = microphoneIcon
                }
            }
        };

        return new Border
        {
            Width = 220,
            Height = 148,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(238, 28, 31, 36)),
            Child = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    iconHost,
                    statusText
                }
            }
        };
    }

    private void ConfigureWindow()
    {
        AppWindow.Resize(new SizeInt32(220, 148));
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
    }

    private void PositionNearTopCenter()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = AppWindow.Size;
        var x = displayArea.X + ((displayArea.Width - size.Width) / 2);
        var y = displayArea.Y + Math.Min(96, Math.Max(24, displayArea.Height / 10));
        AppWindow.Move(new PointInt32(x, y));
    }

    private void AnimationTimer_Tick(object? sender, object e)
    {
        animationPhase += 0.22d;
        activityBoost = Math.Max(0d, activityBoost - 0.09d);
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        var wave = (Math.Sin(animationPhase) + 1d) / 2d;
        var intensity = Math.Clamp(0.35d + (wave * 0.35d) + (activityBoost * 0.3d), 0d, 1d);
        var scale = 0.86d + (intensity * 0.44d);
        pulseRing.Opacity = 0.26d + (intensity * 0.54d);
        pulseScale.ScaleX = scale;
        pulseScale.ScaleY = scale;
        microphoneIcon.FontSize = 34d + (activityBoost * 6d);
    }
}
