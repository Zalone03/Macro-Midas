using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MacroMidas.Views.Overlay;

// Contagem grande no meio da tela antes de começar a gravar ("Gravando em 3… 2… 1…").
public sealed class CountdownOverlayWindow : OverlayWindow
{
    private readonly TextBlock _number;
    private readonly TextBlock _label;

    public CountdownOverlayWindow()
    {
        Title = "Macro Midas: Contagem";

        _number = new TextBlock
        {
            FontSize = 180,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#FFE2A6")),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _label = new TextBlock
        {
            FontSize = 22,
            Foreground = new SolidColorBrush(Color.Parse("#E4E2DE")),
            HorizontalAlignment = HorizontalAlignment.Center,
            Text = "Prepare-se para gravar…"
        };

        var box = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#CC141415")),
            BorderBrush = new SolidColorBrush(Color.Parse("#40C9A75E")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(60, 36),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                Spacing = 4,
                Children = { _number, _label }
            }
        };

        Content = new Grid { IsHitTestVisible = false, Children = { box } };
    }

    public void Set(int seconds, string label)
    {
        _number.Text = seconds.ToString();
        _label.Text = label;
    }
}
