using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Raffica.Shell.Controls;

/// <summary>
/// Draws a stroke icon whose geometry lives in a 24-unit space (the Lucide grid) at 16 or 20 device-independent pixels.
/// The stroke is always 1.5 px on screen, with round caps and joins and no fill.
/// </summary>
public sealed class StrokeIcon : Control
{
    /// <summary>The path data of the icon, in 24-unit space (resources <c>Raffica.Icon.*</c>).</summary>
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<StrokeIcon, Geometry?>(nameof(Data));

    /// <summary>The edge length on screen: 16 or 20.</summary>
    public static readonly StyledProperty<int> SizeProperty =
        AvaloniaProperty.Register<StrokeIcon, int>(nameof(Size), 16, coerce: CoerceSize);

    /// <summary>The brush of the stroke.</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<StrokeIcon, IBrush?>(nameof(Stroke));

    private const double GridUnits = 24;
    private const double ScreenThickness = 1.5;

    static StrokeIcon()
    {
        AffectsMeasure<StrokeIcon>(SizeProperty);
        AffectsRender<StrokeIcon>(DataProperty, SizeProperty, StrokeProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <exception cref="ArgumentOutOfRangeException">The value is neither 16 nor 20.</exception>
    public int Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>The stroke thickness in the 24-unit space that comes out as 1.5 px on screen at <paramref name="size"/>.</summary>
    public static double StrokeThicknessFor(int size) => ScreenThickness * GridUnits / size;

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Data is not { } data || Stroke is not { } stroke)
        {
            return;
        }

        var pen = new Pen(stroke, StrokeThicknessFor(Size), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var scale = Size / GridUnits;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(null, pen, data);
        }
    }

    private static int CoerceSize(AvaloniaObject sender, int value) =>
        value is 16 or 20
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The icon size is 16 or 20.");
}
