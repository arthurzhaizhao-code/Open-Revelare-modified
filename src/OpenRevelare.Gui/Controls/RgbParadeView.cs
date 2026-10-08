using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Globalization;

namespace OpenRevelare.Gui.Controls;

/// <summary>
/// Draws luma followed by the three channels of an existing <see cref="WaveformData"/>. Each panel
/// keeps the source image's horizontal position and uses the same vertical level and trace scale,
/// so channel height differences remain directly comparable while calibrating a negative.
/// </summary>
public sealed class RgbParadeView : Control
{
    public static readonly StyledProperty<WaveformData?> DataProperty =
        AvaloniaProperty.Register<RgbParadeView, WaveformData?>(nameof(Data));

    public WaveformData? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }

    public static readonly StyledProperty<bool> ShowScaleLabelsProperty =
        AvaloniaProperty.Register<RgbParadeView, bool>(nameof(ShowScaleLabels));

    public bool ShowScaleLabels
    {
        get => GetValue(ShowScaleLabelsProperty);
        set => SetValue(ShowScaleLabelsProperty, value);
    }

    static RgbParadeView() => AffectsRender<RgbParadeView>(DataProperty, ShowScaleLabelsProperty);

    private WriteableBitmap? _bitmap;
    private int _bitmapColumns, _bitmapLevels;
    private const float TraceGamma = 0.3f;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(21, 23, 26)), new Rect(0, 0, w, h));
        if (w < 4 || h < 2) return;

        double left = ShowScaleLabels ? 34d : 0d;
        double bottom = ShowScaleLabels ? 20d : 14d;
        var plot = new Rect(left, 0, Math.Max(1d, w - left), Math.Max(1d, h - bottom));

        WaveformData? d = Data;
        if (d is not null && Paint(d) is { } bitmap)
            ctx.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                          plot);

        var grid = new Pen(new SolidColorBrush(Color.FromArgb(70, 120, 126, 134)), 1);
        for (int q = 1; q < 4; q++)
        {
            double y = plot.Y + plot.Height * q / 4d;
            ctx.DrawLine(grid, new Point(plot.X, y), new Point(plot.Right, y));
        }

        var divider = new Pen(new SolidColorBrush(Color.FromArgb(115, 120, 126, 134)), 1);
        for (int panel = 1; panel < 4; panel++)
        {
            double x = plot.X + plot.Width * panel / 4d;
            ctx.DrawLine(divider, new Point(x, plot.Y), new Point(x, plot.Bottom));
        }

        DrawChannelLabels(ctx, plot, h);
        if (ShowScaleLabels) DrawScale(ctx, plot);
    }

    private static void DrawChannelLabels(DrawingContext ctx, Rect plot, double height)
    {
        string[] labels = ["Y", "R", "G", "B"];
        Color[] colours = [Color.FromRgb(225, 225, 225), Color.FromRgb(255, 105, 105),
                           Color.FromRgb(105, 235, 125), Color.FromRgb(105, 155, 255)];
        for (int i = 0; i < labels.Length; i++)
        {
            var ft = new FormattedText(labels[i], CultureInfo.InvariantCulture,
                                       FlowDirection.LeftToRight, Typeface.Default, 10,
                                       new SolidColorBrush(colours[i]));
            double centre = plot.X + plot.Width * (i + 0.5d) / 4d;
            ctx.DrawText(ft, new Point(centre - ft.Width / 2d, height - ft.Height - 1));
        }
    }

    private static void DrawScale(DrawingContext ctx, Rect plot)
    {
        var brush = new SolidColorBrush(Color.FromArgb(170, 230, 232, 235));
        for (int q = 0; q <= 4; q++)
        {
            var ft = new FormattedText((100 - q * 25) + "%", CultureInfo.InvariantCulture,
                                       FlowDirection.LeftToRight, Typeface.Default, 10, brush);
            double y = Math.Clamp(plot.Y + plot.Height * q / 4d - ft.Height / 2d,
                                  0, plot.Bottom - ft.Height);
            ctx.DrawText(ft, new Point(Math.Max(1, plot.X - ft.Width - 4), y));
        }
    }

    private WriteableBitmap? Paint(WaveformData d)
    {
        int paradeColumns = d.Columns * 4;
        if (_bitmap is null || _bitmapColumns != paradeColumns || _bitmapLevels != d.Levels)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(paradeColumns, d.Levels), new Vector(96, 96),
                                          PixelFormat.Bgra8888, AlphaFormat.Premul);
            _bitmapColumns = paradeColumns;
            _bitmapLevels = d.Levels;
        }

        using ILockedFramebuffer buffer = _bitmap.Lock();
        unsafe
        {
            var row = (byte*)buffer.Address;
            float inverseColumn = 1f / d.ColumnSamples;
            for (int level = 0; level < d.Levels; level++)
            {
                int y = d.Levels - 1 - level;
                byte* line = row + (long)y * buffer.RowBytes;
                for (int column = 0; column < d.Columns; column++)
                {
                    int cell = column * d.Levels + level;
                    byte luma = Trace(d.Y[cell], inverseColumn);
                    WriteCell(line, column, luma, luma, luma);
                    WriteCell(line, d.Columns + column, Trace(d.R[cell], inverseColumn), 0, 0);
                    WriteCell(line, d.Columns * 2 + column, 0, Trace(d.G[cell], inverseColumn), 0);
                    WriteCell(line, d.Columns * 3 + column, 0, 0, Trace(d.B[cell], inverseColumn));
                }
            }
        }
        return _bitmap;
    }

    private static unsafe void WriteCell(byte* line, int x, byte r, byte g, byte b)
    {
        line[x * 4 + 0] = b;
        line[x * 4 + 1] = g;
        line[x * 4 + 2] = r;
        line[x * 4 + 3] = Math.Max(r, Math.Max(g, b));
    }

    private static byte Trace(float count, float inverseColumn)
    {
        if (count <= 0f) return 0;
        float v = MathF.Pow(Math.Min(1f, count * inverseColumn), TraceGamma);
        return (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
    }
}
