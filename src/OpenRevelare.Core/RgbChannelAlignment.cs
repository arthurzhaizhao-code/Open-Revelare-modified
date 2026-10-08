namespace OpenRevelare.Core;

/// <summary>
/// Independent RGB alignment in normalised Cineon code values.
///
/// This is the signal the dedicated alignment parade reads: after the measured Dmin/Dmax endpoint
/// map, before a print LUT, display rendering, output-space matrix or Stage 2. The endpoint values
/// themselves are never rewritten. Gain pivots around Cineon black (code 95), so a neutral gain
/// edit keeps the measured Dmin anchor fixed.
/// </summary>
public static class RgbChannelAlignment
{
    public const double CodeScale = 1023.0;
    private const float BlackCodeNormalised = (float)(FrameParams.CineonBlackCode / CodeScale);

    /// <summary>
    /// Apply the controls to an already Cineon-encoded RGB buffer. Shift is stated in code values;
    /// gain is a scale about code 95. The three assignments are deliberately independent.
    /// </summary>

    public static void Apply(float[] data, FrameParams cal, bool clampToUnit = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(cal);
        if (data.Length % 3 != 0) throw new ArgumentException("RGB data must contain triples", nameof(data));

        double[] shift = cal.RgbAlignShift;
        double[] gain = cal.RgbAlignGain;
        if (shift.Length != 3 || gain.Length != 3)
            throw new ArgumentException("RGB alignment requires three shifts and three gains", nameof(cal));

        bool active0 = Math.Abs(shift[0]) > 1e-12 || Math.Abs(gain[0] - 1.0) > 1e-12;
        bool active1 = Math.Abs(shift[1]) > 1e-12 || Math.Abs(gain[1] - 1.0) > 1e-12;
        bool active2 = Math.Abs(shift[2]) > 1e-12 || Math.Abs(gain[2] - 1.0) > 1e-12;
        if (!active0 && !active1 && !active2) return;

        float s0 = (float)(shift[0] / CodeScale), s1 = (float)(shift[1] / CodeScale), s2 = (float)(shift[2] / CodeScale);
        float g0 = (float)gain[0], g1 = (float)gain[1], g2 = (float)gain[2];
        ParallelSweep.OverPixels(data.Length / 3, (from, to) =>
        {
            for (int i = from; i < to; i += 3)
            {
                if (active0)
                {
                    data[i] = BlackCodeNormalised + (data[i] - BlackCodeNormalised) * g0 + s0;
                    if (clampToUnit) data[i] = Math.Clamp(data[i], 0.0f, 1.0f);
                }
                if (active1)
                {
                    data[i + 1] = BlackCodeNormalised + (data[i + 1] - BlackCodeNormalised) * g1 + s1;
                    if (clampToUnit) data[i + 1] = Math.Clamp(data[i + 1], 0.0f, 1.0f);
                }
                if (active2)
                {
                    data[i + 2] = BlackCodeNormalised + (data[i + 2] - BlackCodeNormalised) * g2 + s2;
                    if (clampToUnit) data[i + 2] = Math.Clamp(data[i + 2], 0.0f, 1.0f);
                }
            }
        });
    }

    /// <summary>
    /// Apply the same Cineon-domain operation to Stage-1's linear-positive carrier. The log pair
    /// is exact to float precision and lets all existing display and print-LUT exits consume the
    /// adjusted Cineon signal without each exit reimplementing the alignment slot.
    /// </summary>
    public static void ApplyToLinearPositive(float[] data, FrameParams cal)
    {
        if (!IsActive(cal)) return;
        LogEncoding.ToCineon(data);
        Apply(data, cal);
        LogEncoding.FromCineon(data);
    }

    private static bool IsActive(FrameParams cal)
    {
        double[] shift = cal.RgbAlignShift;
        double[] gain = cal.RgbAlignGain;
        if (shift.Length != 3 || gain.Length != 3)
            throw new ArgumentException("RGB alignment requires three shifts and three gains", nameof(cal));
        for (int c = 0; c < 3; c++)
            if (Math.Abs(shift[c]) > 1e-12 || Math.Abs(gain[c] - 1.0) > 1e-12)
                return true;
        return false;
    }
}
