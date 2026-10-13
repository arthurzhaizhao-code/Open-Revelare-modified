namespace OpenRevelare.Core;

/// <summary>
/// Scene-referred creative grade used by SceneGradeV3.  It runs after negative calibration,
/// endpoint placement and RGB alignment, but before either the analytic display rendering or a
/// print-film rendering.  Tonal zones are selected from one immutable pre-adjustment luminance;
/// shadows and highlights therefore never feed each other's masks.
/// </summary>
public static class SceneGrade
{
    private readonly record struct Luma(float R, float G, float B)
    {
        public static Luma For(ColorSpaceDef space)
        {
            double[,] m = space.ToXyz();
            return new((float)m[1, 0], (float)m[1, 1], (float)m[1, 2]);
        }

        public float Of(float r, float g, float b) => r * R + g * G + b * B;
    }

    private const float Epsilon = 1e-8f;
    private const float ToneCeiling = 0.9995f;

    /// <summary>
    /// Applies the complete creative grade to calibrated linear-positive RGB.  Brightness
    /// operations change one luminance value and return to RGB with a common scale, preserving
    /// linear chromaticity.  Saturation is the only neutral control that changes chroma.
    /// </summary>
    public static void Apply(float[] data, FrameParams cal, ColorSpaceDef operationalSpace)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(cal);
        if (data.Length % 3 != 0)
            throw new ArgumentException("Expected interleaved RGB samples.", nameof(data));

        Luma luma = Luma.For(operationalSpace);
        float wbR = (float)cal.WbGains[0];
        float wbG = (float)cal.WbGains[1];
        float wbB = (float)cal.WbGains[2];
        float exposure = MathF.Pow(2f, (float)cal.ExposureEv);
        float contrast = (float)cal.Contrast;
        float black = (float)cal.BlackPoint;
        float white = (float)cal.WhitePoint;
        // The UI stores the historical levels endpoints.  Recover its bipolar slider values,
        // but do not run a global levels remap: before the display rendering that would collapse
        // a broad shadow range onto Cineon 95, producing a large lifted-black shelf through 2383.
        float blackControl = Math.Clamp(-black / 0.5f, -1f, 1f);
        float whiteControl = Math.Clamp((1f - white) / 0.5f, -1f, 1f);
        float shadows = (float)cal.Shadows;
        float highlights = (float)cal.Highlights;
        float saturationControl = (float)cal.Saturation;
        float[]? curveM = Stage2.BuildLut(cal.CurvePointsM, cal.CurveHasEndpoints);
        float[]? curveR = Stage2.BuildLut(cal.CurvePointsR, cal.CurveHasEndpoints);
        float[]? curveG = Stage2.BuildLut(cal.CurvePointsG, cal.CurveHasEndpoints);
        float[]? curveB = Stage2.BuildLut(cal.CurvePointsB, cal.CurveHasEndpoints);
        bool doTone = blackControl != 0f || whiteControl != 0f || contrast != 0f
                      || shadows != 0f || highlights != 0f;
        bool doCurves = curveM is not null || curveR is not null || curveG is not null || curveB is not null;
        bool doWb = cal.WbGains.Any(value => Math.Abs(value - 1.0) > 1e-8);
        bool doExposure = cal.ExposureEv != 0.0;
        if (!doWb && !doExposure && !doTone && !doCurves && saturationControl == 0f) return;

        ParallelSweep.OverPixels(data.Length / 3, (from, to) =>
        {
            for (int i = from; i < to; i += 3)
            {
                float r = MathF.Max(data[i] * wbR * exposure, 0f);
                float g = MathF.Max(data[i + 1] * wbG * exposure, 0f);
                float b = MathF.Max(data[i + 2] * wbB * exposure, 0f);

                float sourceY = MathF.Max(luma.Of(r, g, b), 0f);
                if (doTone && sourceY > Epsilon)
                {
                    // A stable display proxy gives the controls photographic 0..1 coordinates
                    // without first committing the picture to the selected display rendering.
                    float sourceTone = LinearPositiveToTone(sourceY);
                    float targetTone = sourceTone;

                    // A bounded S-curve fixes both endpoints.  The old 18%-grey affine pivot put
                    // almost the whole photograph on the bright side, so adding contrast lifted
                    // most tones and read as haze.  A 0.38 perceptual pivot divides an ordinary
                    // rendered photograph near its visual centre instead.
                    const float contrastPivot = 0.38f;
                    targetTone += contrast * 2f * (sourceTone - contrastPivot)
                                  * sourceTone * (1f - sourceTone);

                    // Black/white are narrow endpoint zones, not another global levels pass.
                    // Their deliberately small amplitudes give the ±1 UI range useful travel and
                    // keep a one-step nudge from flattening an entire end of the histogram.
                    float blackMask = 1f - SmoothStep(0.025f, 0.30f, sourceTone);
                    float whiteMask = SmoothStep(0.62f, 0.96f, sourceTone);
                    targetTone += blackControl * 0.025f * blackMask;
                    targetTone += whiteControl * 0.085f * whiteMask;

                    // Both masks are derived from sourceTone.  Their soft zones overlap around
                    // the mid-tones, as photographic controls do, but neither adjustment changes
                    // the mask used by the other one.
                    float shadowMask = Band(sourceTone, 0.015f, 0.55f, 0.11f);
                    float highlightMask = Band(sourceTone, 0.40f, 0.985f, 0.14f);
                    targetTone += shadows * 0.22f * shadowMask;
                    targetTone += highlights * 0.22f * highlightMask;
                    targetTone = Math.Clamp(targetTone, 0f, ToneCeiling);

                    // The proxy deliberately maps everything below rendered black to tone zero,
                    // so its inverse is not one-to-one there.  Preserve the original sample when
                    // a control produced no tone delta; otherwise merely enabling a different
                    // tonal control could lift those values onto one common black shelf.
                    if (MathF.Abs(targetTone - sourceTone) > 1e-7f)
                    {
                        float targetY = targetTone <= 0f ? 0f : ToneToLinearPositive(targetTone);
                        float scale = targetY / sourceY;
                        r *= scale;
                        g *= scale;
                        b *= scale;
                    }
                }

                if (curveM is not null)
                {
                    if (cal.CurvePreserveHue)
                    {
                        float y = MathF.Max(luma.Of(r, g, b), 0f);
                        if (y > Epsilon)
                        {
                            float tone = LinearPositiveToTone(y);
                            float mapped = Math.Clamp(Stage2.SampleLut(curveM, tone), 0f, ToneCeiling);
                            float scale = ToneToLinearPositive(mapped) / y;
                            r *= scale;
                            g *= scale;
                            b *= scale;
                        }
                    }
                    else
                    {
                        r = ApplyChannelCurve(r, curveM);
                        g = ApplyChannelCurve(g, curveM);
                        b = ApplyChannelCurve(b, curveM);
                    }
                }

                if (curveR is not null) r = ApplyChannelCurve(r, curveR);
                if (curveG is not null) g = ApplyChannelCurve(g, curveG);
                if (curveB is not null) b = ApplyChannelCurve(b, curveB);

                if (saturationControl != 0f)
                    ApplyLogSaturation(ref r, ref g, ref b, luma, saturationControl);

                data[i] = r;
                data[i + 1] = g;
                data[i + 2] = b;
            }
        });

    }

    private static float ApplyChannelCurve(float value, float[] lut)
    {
        if (!(value > 0f)) return value;
        float tone = LinearPositiveToTone(value);
        float mapped = Math.Clamp(Stage2.SampleLut(lut, tone), 0f, ToneCeiling);
        return ToneToLinearPositive(mapped);
    }

    /// <summary>
    /// Saturation in log-light coordinates.  Scaling linear RGB away from arithmetic luma was
    /// largely undone by the following Cineon encode and print rendering; scaling channel
    /// separation in log space changes the quantity a Cineon/2383 path actually carries.  The
    /// weighted log mean stays fixed, so this is chroma strength rather than exposure.
    /// </summary>
    private static void ApplyLogSaturation(
        ref float r,
        ref float g,
        ref float b,
        Luma luma,
        float control)
    {
        float factor = control >= 0f ? 1f + 1.5f * control : 1f + control;
        factor = MathF.Max(factor, 0f);
        float lr = MathF.Log2(MathF.Max(r, Epsilon));
        float lg = MathF.Log2(MathF.Max(g, Epsilon));
        float lb = MathF.Log2(MathF.Max(b, Epsilon));
        float mean = lr * luma.R + lg * luma.G + lb * luma.B;
        r = MathF.Pow(2f, mean + (lr - mean) * factor);
        g = MathF.Pow(2f, mean + (lg - mean) * factor);
        b = MathF.Pow(2f, mean + (lb - mean) * factor);
    }

    private static float Band(float value, float low, float high, float feather)
    {
        float enter = SmoothStep(low, low + feather, value);
        float leave = 1f - SmoothStep(high - feather, high, value);
        return enter * leave;
    }

    private static float SmoothStep(float a, float b, float x)
    {
        if (a == b) return x >= b ? 1f : 0f;
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // Analytic standard-render proxy and its inverse.  These reproduce the scalar part of
    // ColorPipeline.CineonToDisplay (including toe and shoulder), but are used only as a control
    // coordinate.  The actual standard/2383 rendering still happens later and exactly once.
    internal static float LinearPositiveToTone(float value)
    {
        if (!(value > 0f)) return 0f;
        const float blackLin = 0.010797751f;
        const float span = 1f - blackLin;
        float printed = 14.343881f * MathF.Pow(value, 1f / 0.6f);
        float normalized = MathF.Max((printed - blackLin) / span, 0f);
        if (normalized < 0.05f)
            normalized = 0.05f * MathF.Pow(normalized / 0.05f, 1.3f);
        return HighlightRolloff.Of(normalized, 1f);
    }

    internal static float ToneToLinearPositive(float tone)
    {
        float t = Math.Clamp(tone, 0f, ToneCeiling);
        if (t > HighlightRolloff.Knee)
        {
            float shoulderSpan = 1f - HighlightRolloff.Knee;
            float q = t - HighlightRolloff.Knee;
            t = HighlightRolloff.Knee + q * shoulderSpan / MathF.Max(shoulderSpan - q, Epsilon);
        }
        if (t < 0.05f)
            t = 0.05f * MathF.Pow(t / 0.05f, 1f / 1.3f);

        const float blackLin = 0.010797751f;
        const float span = 1f - blackLin;
        float printed = t * span + blackLin;
        return MathF.Pow(printed / 14.343881f, 0.6f);
    }
}
