namespace OpenRevelare.Core;

/// <summary>
/// Independent final-output RGB alignment.
///
/// This deliberately runs after colour conversion, the display rendering and Stage 2. Applying
/// it earlier lets a later 3x3 colour matrix or a luma-driven operation spread an edit from one
/// component into the other two, which makes manual parade alignment impossible to reason about.
/// </summary>
public static class RgbChannelAlignment
{
    public const double CodeScale = 1023.0;

    public static void Apply(float[] data, FrameParams cal, bool clampToUnit = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(cal);
        if (data.Length % 3 != 0) throw new ArgumentException("RGB data must contain triples", nameof(data));

        double[] shift = cal.RgbAlignShift;
        double[] gain = cal.RgbAlignGain;
        if (shift.Length != 3 || gain.Length != 3)
            throw new ArgumentException("RGB alignment requires three shifts and three gains", nameof(cal));

        bool active = false;
        for (int c = 0; c < 3; c++)
            active |= Math.Abs(shift[c]) > 1e-12 || Math.Abs(gain[c] - 1.0) > 1e-12;
        if (!active) return;

        float s0 = (float)(shift[0] / CodeScale), s1 = (float)(shift[1] / CodeScale), s2 = (float)(shift[2] / CodeScale);
        float g0 = (float)gain[0], g1 = (float)gain[1], g2 = (float)gain[2];
        ParallelSweep.OverPixels(data.Length / 3, (from, to) =>
        {
            for (int i = from; i < to; i += 3)
            {
                data[i] = (data[i] + s0) * g0;
                data[i + 1] = (data[i + 1] + s1) * g1;
                data[i + 2] = (data[i + 2] + s2) * g2;
                if (clampToUnit)
                {
                    data[i] = Math.Clamp(data[i], 0.0f, 1.0f);
                    data[i + 1] = Math.Clamp(data[i + 1], 0.0f, 1.0f);
                    data[i + 2] = Math.Clamp(data[i + 2], 0.0f, 1.0f);
                }
            }
        });
    }
}
