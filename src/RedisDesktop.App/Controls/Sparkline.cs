using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RedisDesktop.App.Controls;

public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    static Sparkline()
    {
        AffectsRender<Sparkline>(ValuesProperty, StrokeProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var values = Values;
        if (values is null || values.Count == 0)
        {
            return;
        }

        var pad = 6.0;
        var width = Math.Max(1, Bounds.Width - pad * 2);
        var height = Math.Max(1, Bounds.Height - pad * 2);
        var max = 1d;
        foreach (var value in values)
        {
            if (value > max)
            {
                max = value;
            }
        }

        var color = (Stroke as ISolidColorBrush)?.Color ?? Color.FromRgb(22, 119, 255);
        var pen = new Pen(new SolidColorBrush(color), 1.6);

        if (values.Count == 1)
        {
            var y = pad + height - Clamp(values[0] / max) * height;
            context.DrawLine(pen, new Point(pad, y), new Point(pad + width, y));
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < values.Count; i++)
            {
                var x = pad + width * i / (values.Count - 1);
                var y = pad + height - Clamp(values[i] / max) * height;
                var point = new Point(x, y);
                if (i == 0)
                {
                    ctx.BeginFigure(point, false);
                }
                else
                {
                    ctx.LineTo(point);
                }
            }

            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static double Clamp(double ratio)
        => ratio < 0 ? 0 : ratio > 1 ? 1 : ratio;
}
