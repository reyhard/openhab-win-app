using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace OpenHab.Windows.Tray.Voice;

[ExcludeFromCodeCoverage(Justification = "WinUI voice command confirmation window glue.")]
public sealed class VoiceCommandConfirmationWindow : Window
{
    private readonly TaskCompletionSource<bool> decisionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool isClosing;

    public VoiceCommandConfirmationWindow(string recognizedText)
    {
        Title = "Confirm voice command";
        Content = BuildContent(recognizedText);
        ConfigureWindow();
        Closed += OnClosed;
    }

    public async Task<bool> WaitForDecisionAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() =>
        {
            decisionSource.TrySetResult(false);
            if (isClosing)
            {
                return;
            }

            if (DispatcherQueue is { } dispatcher)
            {
                _ = dispatcher.TryEnqueue(() =>
                {
                    if (isClosing)
                    {
                        return;
                    }

                    isClosing = true;
                    Close();
                });
                return;
            }

            isClosing = true;
            Close();
        });

        Activate();
        return await decisionSource.Task.ConfigureAwait(true);
    }

    private void ConfigureWindow()
    {
        AppWindow.Resize(new SizeInt32(420, 210));
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
    }

    private Border BuildContent(string recognizedText)
    {
        var transcript = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(recognizedText) ? "(empty)" : recognizedText.Trim(),
            TextWrapping = TextWrapping.WrapWholeWords,
            MaxLines = 4
        };

        var sendButton = new Button
        {
            Content = "Send",
            MinWidth = 88
        };
        sendButton.Click += (_, _) => CompleteAndClose(true);

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 88
        };
        cancelButton.Click += (_, _) => CompleteAndClose(false);

        var title = new TextBlock
        {
            Text = "Recognized command",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        Grid.SetRow(title, 0);

        var transcriptBorder = new Border
        {
            Margin = new Thickness(0, 8, 0, 12),
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 220, 225, 233)),
            Background = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            Child = transcript
        };
        Grid.SetRow(transcriptBorder, 1);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, sendButton }
        };
        Grid.SetRow(actions, 3);

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };
        grid.Children.Add(title);
        grid.Children.Add(transcriptBorder);
        grid.Children.Add(actions);

        return new Border
        {
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 210, 216, 226)),
            Background = new SolidColorBrush(Color.FromArgb(255, 252, 252, 253)),
            Child = grid
        };
    }

    private void CompleteAndClose(bool accepted)
    {
        decisionSource.TrySetResult(accepted);
        if (!isClosing)
        {
            isClosing = true;
            Close();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        isClosing = true;
        decisionSource.TrySetResult(false);
    }
}
