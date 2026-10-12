using OpenRevelare.Core;
using OpenRevelare.Gui.Models;
using Xunit;

namespace OpenRevelare.Tests;

public class NegBaseParityTests
{
    private static float T(double density) => (float)Math.Pow(10.0, -density);

    [Fact]
    public void New_gui_frame_uses_negbase_diffuse_white_target_without_changing_legacy_core_default()
    {
        Assert.Equal(FrameParams.CineonWhiteCode, new FrameParams().HighlightTargetCode);
        Assert.Equal(FrameParams.CineonDiffuseWhiteCode,
            new RollFrame("/tmp/new-negative.raf").Params.HighlightTargetCode);
    }

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

    [Fact]
    public void Neutral_region_with_unequal_channel_slopes_is_accepted()
    {
        NeutralHighlightCandidate? candidate = FilmBase.DetectNeutralHighlightFromFrame(
            UnequalSlopeFrame(coloured: false), [1.0, 1.0, 1.0], edgeInset: 0.0);

        Assert.NotNull(candidate);
        Assert.InRange(candidate!.Density[0], 1.39, 1.41);
        Assert.InRange(candidate.Density[1], 1.81, 1.83);
        Assert.InRange(candidate.Density[2], 2.23, 2.25);
        Assert.True(candidate.PixelCount >= 32 * 32);
    }

    [Fact]
    public void Unequal_channel_normalisation_still_rejects_coloured_region()
    {
        NeutralHighlightCandidate? candidate = FilmBase.DetectNeutralHighlightFromFrame(
            UnequalSlopeFrame(coloured: true), [1.0, 1.0, 1.0], edgeInset: 0.0);

        Assert.True(candidate is null || candidate.MeanDensity < 1.2,
            $"coloured block should not control the endpoint, got {candidate?.MeanDensity:F3}");
    }

    [Fact]
    public void Narrow_neutral_frame_line_is_not_a_white_reference()
    {
        ImageBuffer image = GradientFrame(32, coloured: false);
        for (int y = 26; y < 486; y++)
        for (int x = 0; x < 8; x++)
        {
            int i = (y * image.Width + x) * 3;
            image.Data[i] = image.Data[i + 1] = image.Data[i + 2] = T(2.8);
        }

        NeutralHighlightCandidate? candidate = FilmBase.DetectNeutralHighlightFromFrame(
            image, [1.0, 1.0, 1.0], edgeInset: 0.0);

        Assert.NotNull(candidate);
        Assert.InRange(candidate!.MeanDensity, 1.39, 1.41);
        Assert.True(candidate.Region.W > 0.05 && candidate.Region.H > 0.05);
    }

    [Fact]
    public void Overdense_neutral_block_is_not_a_diffuse_white_reference()
    {
        ImageBuffer image = GradientFrame(32, coloured: false);
        for (int y = 100; y < 180; y++)
        for (int x = 100; x < 180; x++)
        {
            int i = (y * image.Width + x) * 3;
            image.Data[i] = image.Data[i + 1] = image.Data[i + 2] = T(2.8);
        }

        NeutralHighlightCandidate? candidate = FilmBase.DetectNeutralHighlightFromFrame(
            image, [1.0, 1.0, 1.0], edgeInset: 0.0);

        Assert.NotNull(candidate);
        Assert.InRange(candidate!.MeanDensity, 1.39, 1.41);
    }

    [Fact]
    public void Opaque_frame_rails_are_removed_before_candidate_thresholding()
    {
        ImageBuffer image = GradientFrame(32, coloured: false);
        for (int y = 0; y < image.Height; y++)
        for (int x = 60; x < 115; x++)
        {
            int left = (y * image.Width + x) * 3;
            int right = (y * image.Width + image.Width - 1 - x) * 3;
            for (int c = 0; c < 3; c++)
            {
                image.Data[left + c] = T(2.6);
                image.Data[right + c] = T(2.6);
            }
        }

        NeutralHighlightCandidate? candidate = FilmBase.DetectNeutralHighlightFromFrame(
            image, [1.0, 1.0, 1.0]);

        Assert.NotNull(candidate);
        Assert.InRange(candidate!.MeanDensity, 1.39, 1.41);
        Assert.True(candidate.Region.X > 0.3 && candidate.Region.X < 0.6);
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

    private static ImageBuffer UnequalSlopeFrame(bool coloured)
    {
        const int width = 512, height = 512;
        double[] slopes = [1.0, 1.3, 1.6];
        var image = new ImageBuffer(width, height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double baseDensity = 0.25 + 0.55 * (x + y) / (width + height - 2.0);
            int i = (y * width + x) * 3;
            for (int c = 0; c < 3; c++) image.Data[i + c] = T(baseDensity * slopes[c]);
        }

        for (int y = 220; y < 252; y++)
        for (int x = 220; x < 252; x++)
        {
            int i = (y * width + x) * 3;
            if (coloured)
            {
                image.Data[i] = T(1.80);
                image.Data[i + 1] = T(1.35);
                image.Data[i + 2] = T(1.10);
            }
            else
            {
                for (int c = 0; c < 3; c++) image.Data[i + c] = T(1.4 * slopes[c]);
            }
        }
        return image;
    }
}
