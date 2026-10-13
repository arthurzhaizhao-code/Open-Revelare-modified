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
        float contrast = MathF.Pow(2f, (float)cal.Contrast);
        float black = (float)cal.BlackPoint;
        float white = (float)cal.WhitePoint;
        bool levels = (black != 0f || white != 1f) && black < white;
        float shadows = (float)cal.Shadows;
        float highlights = (float)cal.Highlights;
        float saturation = 1f + (float)cal.Saturation;
        float[]? curveM = Stage2.BuildLut(cal.CurvePointsM, cal.CurveHasEndpoints);
        float[]? curveR = Stage2.BuildLut(cal.CurvePointsR, cal.CurveHasEndpoints);
        float[]? curveG = Stage2.BuildLut(cal.CurvePointsG, cal.CurveHasEndpoints);
        float[]? curveB = Stage2.BuildLut(cal.CurvePointsB, cal.CurveHasEndpoints);
        bool doTone = levels || cal.Contrast != 0.0 || shadows != 0f || highlights != 0f;
        bool doCurves = curveM is not null || curveR is not null || curveG is not null || curveB is not null;
        bool doWb = cal.WbGains.Any(value => Math.Abs(value - 1.0) > 1e-8);
        bool doExposure = cal.ExposureEv != 0.0;
        if (!doWb && !doExposure && !doTone && !doCurves && saturation == 1f) return;

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

                    if (levels)
                        targetTone = (targetTone - black) / (white - black);

                    // Contrast pivots around photographic mid-grey rather than encoded 0.5.
                    const float midGreyTone = 0.183f;
                    targetTone = midGreyTone + (targetTone - midGreyTone) * contrast;

                    // Both masks are derived from sourceTone.  Their soft zones overlap around
                    // the mid-tones, as photographic controls do, but neither adjustment changes
                    // the mask used by the other one.
                    float shadowMask = Band(sourceTone, 0.015f, 0.55f, 0.11f);
                    float highlightMask = Band(sourceTone, 0.40f, 0.985f, 0.14f);
                    targetTone += shadows * 0.22f * shadowMask;
                    targetTone += highlights * 0.22f * highlightMask;
                    targetTone = Math.Clamp(targetTone, 0f, ToneCeiling);

                    float targetY = ToneToLinearPositive(targetTone);
                    float scale = targetY / sourceY;
                    r *= scale;
                    g *= scale;
                    b *= scale;
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

                if (saturation != 1f)
                {
                    float y = luma.Of(r, g, b);
                    r = y + (r - y) * saturation;
                    g = y + (g - y) * saturation;
                    b = y + (b - y) * saturation;
                }

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
