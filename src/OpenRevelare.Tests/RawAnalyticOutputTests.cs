using OpenRevelare.ColorManagement;
using OpenRevelare.Core;
using Xunit;

namespace OpenRevelare.Tests;

public sealed class RawAnalyticOutputTests
{
    [Fact]
    public void Camera_native_RAW_keeps_calibrated_channels_in_output_primaries()
    {
        ImageBuffer pixels = MakeAsymmetricNegative();
        WorkingFrame raw = Working(pixels, CaptureKind.RawCameraNative, "raw");
        WorkingFrame syntheticAces = Working(pixels, CaptureKind.Synthetic, "aces");
        var parameters = new FrameParams
        {
            OutputSpace = "sRGB",
            PrintLut = "",
            DMinPerChannel = new[] { 0.03, 0.09, 0.54 },
            DMaxPerChannel = new[] { 0.83, 1.05, 1.64 },
            HighlightTargetCode = 685,
        };
        using var engine = new LittleCmsEngine();

        RenderedFrame rawResult = Pipeline.Render(
            raw, parameters, ColorPipelineVersion.ManagedV2, engine);
        RenderedFrame acesResult = Pipeline.Render(
            syntheticAces, parameters, ColorPipelineVersion.ManagedV2, engine);

        // Reproduce the managed standard exit explicitly with the output space as the channel
        // interpretation. This is the path camera-native RAW must take: no camera/film primaries
        // were solved, so an ACEScg matrix would be invented rather than colour-managed.
        FrameParams scene = parameters.Clone();
        scene.OutputIntent = OutputIntent.None;
        ImageBuffer expected = Pipeline.ProcessFrame(pixels, scene);
        ColorPipeline.ToOutputTargetFor(
            expected.Data,
            parameters,
            parameters.ResolvedOutputTarget,
            ColorPipelineVersion.ManagedV2,
            engine,
            ColorSpaces.Srgb);

        AssertPixelsClose(expected.Data, rawResult.Pixels.Data, 2e-6f);
        Assert.Contains(
            rawResult.Pixels.Data.Zip(acesResult.Pixels.Data),
            pair => Math.Abs(pair.First - pair.Second) > 0.02f);
    }

    private static WorkingFrame Working(ImageBuffer pixels, CaptureKind kind, string id)
    {
        var encoding = new UncharacterizedPixelEncoding(
            kind,
            "test:" + id,
            CompatibilityPolicy.LegacyTreatNumbersAsWorking,
            TransferState.Unknown,
            NumericRange.Normalized);
        var source = new SourceDescriptor(
            "test:" + id,
            id,
            encoding,
            "test camera-channel negative");
        return new WorkingFrame(
            pixels,
            WorkingSpaceId.LinearAcesCgV1,
            WorkingAdmission.LegacyUncharacterizedPassthrough,
            source);
    }

    private static ImageBuffer MakeAsymmetricNegative()
    {
        var image = new ImageBuffer(3, 2);
        float[] values =
        {
            0.74f, 0.48f, 0.18f,
            0.52f, 0.31f, 0.08f,
            0.33f, 0.19f, 0.035f,
            0.68f, 0.42f, 0.12f,
            0.45f, 0.25f, 0.055f,
            0.27f, 0.14f, 0.022f,
        };
        values.CopyTo(image.Data, 0);
        return image;
    }

    private static void AssertPixelsClose(float[] expected, float[] actual, float tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(
                Math.Abs(expected[i] - actual[i]) <= tolerance,
                $"pixel component {i}: expected {expected[i]}, actual {actual[i]}");
    }
}
