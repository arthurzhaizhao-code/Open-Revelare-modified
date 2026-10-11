using OpenRevelare.Core;
using Xunit;

namespace OpenRevelare.Tests;

public class NegBaseParityTests
{
    private static float T(double density) => (float)Math.Pow(10.0, -density);

    [Fact]
    public void Diffuse_white_placement_keeps_base_at_95_and_places_endpoint_at_685()
    {
        float[] signal =
        [
            (float)Math.Pow(10.0, -FrameParams.OutputRange),
            1.0f,
        ];

        CineonHighlightPlacement.ApplyToLinearPositive(
            signal, FrameParams.CineonDiffuseWhiteCode);

        Assert.Equal(FrameParams.CineonBlackCode, Code(signal[0]), 5);
        Assert.Equal(FrameParams.CineonDiffuseWhiteCode, Code(signal[1]), 5);
    }

    [Fact]
    public void Encoding_ceiling_placement_is_bit_identical()
    {
        float[] signal = [0.0123f, 0.2f, 1.0f, 1.4f];
        float[] before = (float[])signal.Clone();

        CineonHighlightPlacement.ApplyToLinearPositive(signal, FrameParams.CineonWhiteCode);

        Assert.Equal(before, signal);
    }

    [Fact]
    public void Thirty_two_pixel_neutral_region_controls_candidate_but_sixteen_does_not()
    {
        NeutralHighlightCandidate? small = FilmBase.DetectNeutralHighlightFromFrame(
            GradientFrame(16, coloured: false), [1.0, 1.0, 1.0], edgeInset: 0.0);
        NeutralHighlightCandidate? large = FilmBase.DetectNeutralHighlightFromFrame(
            GradientFrame(32, coloured: false), [1.0, 1.0, 1.0], edgeInset: 0.0);

        Assert.True(small is null || small.MeanDensity < 1.0,
            $"16x16 should not control the endpoint, got {small?.MeanDensity:F3}");
        Assert.NotNull(large);
        Assert.InRange(large!.MeanDensity, 1.39, 1.41);
        Assert.True(large.PixelCount >= 32 * 32);
    }

    [Fact]
    public void Denser_coloured_region_is_rejected()
    {
        NeutralHighlightCandidate? candidate = FilmBase.DetectNeutralHighlightFromFrame(
            GradientFrame(64, coloured: true), [1.0, 1.0, 1.0], edgeInset: 0.0);

        Assert.True(candidate is null || candidate.MeanDensity < 1.0,
            $"coloured block should not control the endpoint, got {candidate?.MeanDensity:F3}");
    }

    private static double Code(float linear) =>
        FrameParams.CineonWhiteCode
        + Math.Log10(Math.Max(linear, 1e-10f)) / FrameParams.CineonDensityPerCode;

    private static ImageBuffer GradientFrame(int patchSize, bool coloured)
    {
        const int width = 512, height = 512;
        var image = new ImageBuffer(width, height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double d = 0.25 + 0.55 * (x + y) / (width + height - 2.0);
            int i = (y * width + x) * 3;
            image.Data[i] = image.Data[i + 1] = image.Data[i + 2] = T(d);
        }

        int x0 = 220, y0 = 220;
        for (int y = y0; y < y0 + patchSize; y++)
        for (int x = x0; x < x0 + patchSize; x++)
        {
            int i = (y * width + x) * 3;
            if (coloured)
            {
                image.Data[i] = T(1.8);
                image.Data[i + 1] = T(1.2);
                image.Data[i + 2] = T(0.9);
            }
            else
            {
                image.Data[i] = image.Data[i + 1] = image.Data[i + 2] = T(1.4);
            }
        }
        return image;
    }
}
