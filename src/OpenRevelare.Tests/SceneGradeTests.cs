using OpenRevelare.Core;
using Xunit;

namespace OpenRevelare.Tests;

public sealed class SceneGradeTests
{
    [Theory]
    [InlineData(0.02f)]
    [InlineData(0.08f)]
    [InlineData(0.18f)]
    [InlineData(0.45f)]
    public void Tonal_controls_preserve_linear_rgb_chromaticity(float scale)
    {
        float[] pixel = { 0.72f * scale, 0.41f * scale, 0.19f * scale };
        float rg = pixel[0] / pixel[1];
        float bg = pixel[2] / pixel[1];
        var grade = new FrameParams
        {
            Contrast = 0.22,
            Shadows = 0.35,
            Highlights = -0.28,
            BlackPoint = -0.04,
            WhitePoint = 1.08,
        };

        SceneGrade.Apply(pixel, grade, ColorSpaces.Srgb);

        Assert.InRange(Math.Abs(pixel[0] / pixel[1] - rg), 0f, 2e-5f);
        Assert.InRange(Math.Abs(pixel[2] / pixel[1] - bg), 0f, 2e-5f);
    }

    [Fact]
    public void Neutral_input_stays_neutral_under_every_tonal_control()
    {
        float[] pixels =
        {
            0.018f, 0.018f, 0.018f,
            0.075f, 0.075f, 0.075f,
            0.20f, 0.20f, 0.20f,
        };
        var grade = new FrameParams
        {
            ExposureEv = 0.4,
            Contrast = 0.3,
            Shadows = 0.5,
            Highlights = -0.4,
            BlackPoint = -0.03,
            WhitePoint = 1.07,
        };

        SceneGrade.Apply(pixels, grade, ColorSpaces.Srgb);

        for (int i = 0; i < pixels.Length; i += 3)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(pixels[i]), BitConverter.SingleToInt32Bits(pixels[i + 1]));
            Assert.Equal(BitConverter.SingleToInt32Bits(pixels[i]), BitConverter.SingleToInt32Bits(pixels[i + 2]));
        }
    }

    [Fact]
    public void Shadow_and_highlight_adjustments_are_additive_from_the_same_source_tone()
    {
        float[] neutral = { 0.075f, 0.075f, 0.075f };
        float[] shadow = (float[])neutral.Clone();
        float[] highlight = (float[])neutral.Clone();
        float[] both = (float[])neutral.Clone();

        SceneGrade.Apply(shadow, new FrameParams { Shadows = 0.25 }, ColorSpaces.Srgb);
        SceneGrade.Apply(highlight, new FrameParams { Highlights = -0.20 }, ColorSpaces.Srgb);
        SceneGrade.Apply(both, new FrameParams { Shadows = 0.25, Highlights = -0.20 }, ColorSpaces.Srgb);

        float t0 = Proxy(neutral[0]);
        float expected = t0 + (Proxy(shadow[0]) - t0) + (Proxy(highlight[0]) - t0);
        Assert.InRange(Math.Abs(Proxy(both[0]) - expected), 0f, 2e-5f);
    }

    [Fact]
    public void Shadow_control_is_local_and_does_not_regrade_bright_values()
    {
        float[] pixels = { 0.075f, 0.075f, 0.075f, 0.40f, 0.40f, 0.40f };
        float bright = pixels[3];

        SceneGrade.Apply(pixels, new FrameParams { Shadows = 0.8 }, ColorSpaces.Srgb);

        Assert.True(pixels[0] > 0.075f);
        Assert.InRange(Math.Abs(pixels[3] - bright), 0f, 1e-7f);
    }

    private static float Proxy(float value)
    {
        const float blackLin = 0.010797751f;
        float printed = 14.343881f * MathF.Pow(value, 1f / 0.6f);
        float tone = MathF.Max((printed - blackLin) / (1f - blackLin), 0f);
        if (tone < 0.05f) tone = 0.05f * MathF.Pow(tone / 0.05f, 1.3f);
        return HighlightRolloff.Of(tone, 1f);
    }
}
