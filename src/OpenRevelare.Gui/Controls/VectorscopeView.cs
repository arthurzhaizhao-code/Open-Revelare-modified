using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OpenRevelare.Core;

namespace OpenRevelare.Gui.Controls;

/// <summary>Density and average colour for a Rec.709 YCbCr vectorscope.</summary>
public sealed class VectorscopeData
{
    public required int Size { get; init; }
    public required int[] Counts { get; init; }
    public required float[] SumR { get; init; }
    public required float[] SumG { get; init; }
    public required float[] SumB { get; init; }
    public required int MaxCount { get; init; }

    public const int DefaultSize = 256;
    private const int MaximumSamples = 300_000;

    public static VectorscopeData FromBuffer(ImageBuffer image, int size = DefaultSize)
        => FromBuffer(image.Data, image.Width, image.Height, size);

    public static VectorscopeData FromBuffer(float[] data, int width, int height,
                                             int size = DefaultSize)
    {
        size = Math.Clamp(size, 32, 512);
        var counts = new int[size * size];
        var sumR = new float[counts.Length];
        var sumG = new float[counts.Length];
        var sumB = new float[counts.Length];

        long pixels = (long)width * height;
        int step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(pixels / (double)MaximumSamples)));
        int maxCount = 0;
        for (int y = 0; y < height; y += step)
            for (int x = 0; x < width; x += step)
            {
                int source = (y * width + x) * 3;
                float r = FiniteClamp(data[source]);
                float g = FiniteClamp(data[source + 1]);
                float b = FiniteClamp(data[source + 2]);
                float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                float cb = (b - luma) / 1.8556f;
                float cr = (r - luma) / 1.5748f;
                int px = Math.Clamp((int)((cb + 0.5f) * size), 0, size - 1);
                int py = Math.Clamp((int)((0.5f - cr) * size), 0, size - 1);
                int cell = py * size + px;
                int count = ++counts[cell];
                sumR[cell] += r;
                sumG[cell] += g;
                sumB[cell] += b;
                if (count > maxCount) maxCount = count;
            }

        return new VectorscopeData
        {
            Size = size,
            Counts = counts,
            SumR = sumR,
            SumG = sumG,
            SumB = sumB,
            MaxCount = Math.Max(1, maxCount),
        };
    }

    private static float FiniteClamp(float value)
        => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
}

/// <summary>
/// Displays hue as angle and saturation as distance from the centre. The diagonal guide is the
/// conventional skin-tone direction; it is a direction reference, not an instruction to make all
/// skin samples land on one exact saturation.
/// </summary>
public sealed class VectorscopeView : Control
{
    public static readonly StyledProperty<VectorscopeData?> DataProperty =
        AvaloniaProperty.Register<VectorscopeView, VectorscopeData?>(nameof(Data));

    public VectorscopeData? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }

    public static readonly StyledProperty<bool> ShowScaleLabelsProperty =
        AvaloniaProperty.Register<VectorscopeView, bool>(nameof(ShowScaleLabels));

    public bool ShowScaleLabels
    {
        get => GetValue(ShowScaleLabelsProperty);
        set => SetValue(ShowScaleLabelsProperty, value);
    }

    static VectorscopeView() =>
        AffectsRender<VectorscopeView>(DataProperty, ShowScaleLabelsProperty);

    private WriteableBitmap? _bitmap;
    private int _bitmapSize;

    public override void Render(DrawingContext ctx)
    {
        double width = Bounds.Width, height = Bounds.Height;
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(21, 23, 26)),
                          new Rect(0, 0, width, height));
        if (width < 4 || height < 4) return;

        double labelMargin = ShowScaleLabels ? 26d : 10d;
        double side = Math.Max(1d, Math.Min(width, height) - labelMargin * 2d);
        var plot = new Rect((width - side) / 2d, (height - side) / 2d, side, side);
        if (Data is { } data && Paint(data) is { } bitmap)
            ctx.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), plot);

        DrawGraticule(ctx, plot);
    }

    private void DrawGraticule(DrawingContext ctx, Rect plot)
    {
        Point centre = plot.Center;
        double radius = plot.Width / 2d;
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(90, 150, 155, 164)), 1);
        ctx.DrawEllipse(null, grid, centre, radius, radius);
        ctx.DrawEllipse(null, grid, centre, radius / 2d, radius / 2d);
        ctx.DrawLine(grid, new Point(plot.Left, centre.Y), new Point(plot.Right, centre.Y));
        ctx.DrawLine(grid, new Point(centre.X, plot.Top), new Point(centre.X, plot.Bottom));

        // Standard vectorscope skin-tone direction, about 123° from +Cb in screen coordinates.
        double skinAngle = 123d * Math.PI / 180d;
        var skinPen = new Pen(new SolidColorBrush(Color.FromArgb(210, 245, 170, 115)), 1.4)
        {
            DashStyle = new DashStyle(new double[] { 5d, 4d }, 0),
        };
        ctx.DrawLine(skinPen, centre,
                     new Point(centre.X + Math.Cos(skinAngle) * radius,
                               centre.Y - Math.Sin(skinAngle) * radius));

        (string Label, Color Colour, double Cb, double Cr)[] targets =
        [
            ("R", Color.FromRgb(255, 100, 100), -0.115f, 0.500f),
            ("Yl", Color.FromRgb(245, 225, 90), -0.500f, 0.057f),
            ("G", Color.FromRgb(90, 235, 110), -0.385f, -0.454f),
            ("Cy", Color.FromRgb(90, 230, 230), 0.115f, -0.500f),
            ("B", Color.FromRgb(100, 145, 255), 0.500f, -0.046f),
            ("Mg", Color.FromRgb(235, 100, 235), 0.385f, 0.454f),
        ];
        foreach (var target in targets)
        {
            Point p = ToPoint(plot, target.Cb, target.Cr);
            var colour = new SolidColorBrush(target.Colour);
            ctx.DrawEllipse(null, new Pen(colour, 1), p, 3, 3);
            var ft = Text(target.Label, 10, colour);
            Point lp = LabelPoint(centre, p, ft, 7);
            ctx.DrawText(ft, lp);
        }

        if (!ShowScaleLabels) return;
        var labelBrush = new SolidColorBrush(Color.FromArgb(180, 230, 232, 235));
        DrawCentred(ctx, "75%", new Point(centre.X, centre.Y - radius * 0.75d), labelBrush, 10);
        DrawCentred(ctx, "50%", new Point(centre.X, centre.Y - radius * 0.50d), labelBrush, 10);
        DrawCentred(ctx, "25%", new Point(centre.X, centre.Y - radius * 0.25d), labelBrush, 10);
        var skin = Text(Loc.T("肤色线"), 10,
                        new SolidColorBrush(Color.FromRgb(245, 170, 115)));
        Point skinEnd = new(centre.X + Math.Cos(skinAngle) * radius * 0.76d,
                            centre.Y - Math.Sin(skinAngle) * radius * 0.76d);
        ctx.DrawText(skin, LabelPoint(centre, skinEnd, skin, 4));
    }

    private static Point ToPoint(Rect plot, double cb, double cr) =>
        new(plot.Center.X + cb * plot.Width, plot.Center.Y - cr * plot.Height);

    private static Point LabelPoint(Point centre, Point point, FormattedText text, double gap)
    {
        double dx = point.X - centre.X, dy = point.Y - centre.Y;
        double length = Math.Max(1d, Math.Sqrt(dx * dx + dy * dy));
        return new Point(point.X + dx / length * gap - text.Width / 2d,
                         point.Y + dy / length * gap - text.Height / 2d);
    }

    private static void DrawCentred(DrawingContext ctx, string value, Point point,
                                    IBrush brush, double size)
    {
        var text = Text(value, size, brush);
        ctx.DrawText(text, new Point(point.X - text.Width / 2d, point.Y - text.Height / 2d));
    }

    private static FormattedText Text(string value, double size, IBrush brush) =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush);

    private WriteableBitmap? Paint(VectorscopeData data)
    {
        if (_bitmap is null || _bitmapSize != data.Size)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(data.Size, data.Size), new Vector(96, 96),
                                          PixelFormat.Bgra8888, AlphaFormat.Premul);
            _bitmapSize = data.Size;
        }

        using ILockedFramebuffer buffer = _bitmap.Lock();
        unsafe
        {
            var start = (byte*)buffer.Address;
            for (int y = 0; y < data.Size; y++)
            {
                byte* row = start + (long)y * buffer.RowBytes;
                for (int x = 0; x < data.Size; x++)
                {
                    int cell = y * data.Size + x;
                    int count = data.Counts[cell];
                    int pixel = x * 4;
                    if (count == 0)
                    {
                        row[pixel] = row[pixel + 1] = row[pixel + 2] = row[pixel + 3] = 0;
                        continue;
                    }
                    float density = MathF.Pow(count / (float)data.MaxCount, 0.28f);
                    byte alpha = (byte)Math.Clamp((int)(density * 245f + 10f), 0, 255);
                    float scale = alpha / (255f * count);
                    row[pixel] = (byte)Math.Clamp((int)(data.SumB[cell] * scale * 255f), 0, alpha);
                    row[pixel + 1] = (byte)Math.Clamp((int)(data.SumG[cell] * scale * 255f), 0, alpha);
                    row[pixel + 2] = (byte)Math.Clamp((int)(data.SumR[cell] * scale * 255f), 0, alpha);
                    row[pixel + 3] = alpha;
                }
            }
        }
        return _bitmap;
    }
}
