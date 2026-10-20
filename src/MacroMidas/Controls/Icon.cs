using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace MacroMidas.Controls;

// Ícone de traço (grade 24×24) que herda a cor do texto do pai.
public class Icon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeThickness), 2.0);

    public static readonly StyledProperty<bool> FilledProperty =
        AvaloniaProperty.Register<Icon, bool>(nameof(Filled));

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, ForegroundProperty, StrokeThicknessProperty, FilledProperty);
        AffectsMeasure<Icon>(DataProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool Filled
    {
        get => GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsNaN(Width) ? 16 : Width;
        var h = double.IsNaN(Height) ? 16 : Height;
        return new Size(w, h);
    }

    public override void Render(DrawingContext context)
    {
        var data = Data;
        var brush = Foreground;
        if (data == null || brush == null) return;

        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var scale = size / 24.0;
        var dx = (Bounds.Width - size) / 2;
        var dy = (Bounds.Height - size) / 2;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
        {
            if (Filled)
                context.DrawGeometry(brush, new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), data);
            else
                context.DrawGeometry(null, new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), data);
        }
    }
}
