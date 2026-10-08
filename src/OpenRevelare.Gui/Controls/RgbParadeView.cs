using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OpenRevelare.Gui.Controls;

/// <summary>
/// Draws the three channels of an existing <see cref="WaveformData"/> side by side. Each third
/// keeps the source image's horizontal position and uses the same vertical level and trace scale,
/// so channel height differences remain directly comparable while calibrating a negative.
/// </summary>
public sealed class RgbParadeView : Control
{
    public static readonly StyledProperty<WaveformData?> DataProperty =
        AvaloniaProperty.Register<RgbParadeView, WaveformData?>(nameof(Data));

    public WaveformData? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }

    static RgbParadeView() => AffectsRender<RgbParadeView>(DataProperty);

    private WriteableBitmap? _bitmap;
    private int _bitmapColumns, _bitmapLevels;
    private const float TraceGamma = 0.3f;

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(21, 23, 26)), new Rect(0, 0, w, h));
        if (w < 3 || h < 2) return;

        WaveformData? d = Data;
        if (d is not null && Paint(d) is { } bitmap)
            ctx.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                          new Rect(0, 0, w, h));

        var grid = new Pen(new SolidColorBrush(Color.FromArgb(70, 120, 126, 134)), 1);
        for (int q = 1; q < 4; q++)
        {
            double y = h * q / 4d;
            ctx.DrawLine(grid, new Point(0, y), new Point(w, y));
        }

        var divider = new Pen(new SolidColorBrush(Color.FromArgb(115, 120, 126, 134)), 1);
        ctx.DrawLine(divider, new Point(w / 3d, 0), new Point(w / 3d, h));
        ctx.DrawLine(divider, new Point(w * 2d / 3d, 0), new Point(w * 2d / 3d, h));
    }

    private WriteableBitmap? Paint(WaveformData d)
    {
        int paradeColumns = d.Columns * 3;
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
                    WriteCell(line, column, Trace(d.R[cell], inverseColumn), 0, 0);
                    WriteCell(line, d.Columns + column, 0, Trace(d.G[cell], inverseColumn), 0);
                    WriteCell(line, d.Columns * 2 + column, 0, 0, Trace(d.B[cell], inverseColumn));
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
