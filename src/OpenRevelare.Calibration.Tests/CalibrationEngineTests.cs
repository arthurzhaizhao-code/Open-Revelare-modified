extern alias isolated;
using OpenRevelare.Core;
using Engine = isolated::OpenRevelare.Calibration.CalibrationEngine;
using Frame = isolated::OpenRevelare.Calibration.CalibrationFrame;
using Buffer = isolated::OpenRevelare.Core.ImageBuffer;
using Xunit;

namespace OpenRevelare.Calibration.Tests;

public class CalibrationEngineTests
{
    private static ImageBuffer Image(int seed)
    {
        var rng = new Random(seed);
        var image = new ImageBuffer(100, 100);
        for (int p = 0; p < image.PixelCount; p++)
        {
            double d = 0.1 + rng.NextDouble() * 1.5;
            for (int c = 0; c < 3; c++)
                image.Data[p * 3 + c] = (float)Math.Pow(10, -(d * (1 + c * 0.08) + c * 0.12));
        }
        return image;
    }
    private static Buffer Convert(ImageBuffer b) => new(b.Width, b.Height, (float[])b.Data.Clone())
        { SourceQuantisationStep = b.SourceQuantisationStep };

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void LockedBaseMatchesOriginalHighlightChainAndDoesNotMutateInputs(int count)
    {
        var originals = Enumerable.Range(0, count).Select(Image).ToArray();
        var buffers = originals.Select(Convert).ToArray();
        double[] dmin = [0.05, 0.17, 0.29];
        double[] before = (double[])dmin.Clone();
        var tb = dmin.Select(v => Math.Pow(10, -v)).ToArray();
        var expected = FilmBase.DetectDMaxPerChannelFromRollDetailed(originals, tb,
            90, originals, null, protectIndependentChannelExtrema: true);
        var span = expected?.Density ?? FilmBase.AutoWbHighFromRoll(originals, tb, null, originals);
        var result = Engine.Analyze(buffers.Select(b => new Frame(b, b)).ToArray(), dmin);
        Assert.Equal(before, result.DMin);
        Assert.Equal(before, dmin);
        Assert.NotSame(dmin, result.DMin);
        Assert.True(result.UsedLockedDMin);
        Assert.Null(result.BaseEvidence);
        Assert.Equal(span.Select((v, c) => v + dmin[c]).ToArray(), result.DMax);
        Assert.Equal(expected?.Confidence, result.HighlightDiagnostics?.Confidence);
        Assert.Equal(expected?.RepresentativeFrame, result.HighlightDiagnostics?.RepresentativeFrame);
        for (int i = 0; i < count; i++) Assert.Equal(originals[i].Data, buffers[i].Data);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void FullCalibrationPreservesOriginalBaseSelectionAndHighlightResults(int count)
    {
        var images = Enumerable.Range(10, count).Select(Image).ToArray();
        double[] tb;
        if (count == 1)
            tb = FilmBase.EstimateTBaseByMode(images[0], null)
                ?? FilmBase.EstimateTBaseFromEdgeSliver(images[0])
                ?? FilmBase.EstimateTBaseFromRoll(images);
        else
            tb = FilmBase.EstimateTBaseFromRollDetailed(images, valueImages: images).TBase;
        var dmin = tb.Select(v => -Math.Log10(Math.Max(v, 1e-10))).ToArray();
        var normalizedBase = count > 1 ? tb : dmin.Select(v => Math.Pow(10, -v)).ToArray();
        var h = FilmBase.DetectDMaxPerChannelFromRollDetailed(images, normalizedBase, 90, images);
        var span = h?.Density ?? FilmBase.AutoWbHighFromRoll(images, normalizedBase, null, images);
        var buffers = images.Select(Convert).ToArray();
        var result = Engine.Analyze(buffers.Select(b => new Frame(b, b)).ToArray());
        Assert.False(result.UsedLockedDMin);
        Assert.Equal(dmin, result.DMin);
        Assert.Equal(span.Select((v, c) => v + dmin[c]).ToArray(), result.DMax);
    }

    [Fact]
    public void MissingHighlightFailsWithoutChangingLockedBase()
    {
        var b = new Buffer(100, 100); // opaque, no resolvable picture
        double[] dmin = [0.1, 0.2, 0.3];
        Assert.ThrowsAny<Exception>(() => Engine.Analyze([new Frame(b, b)], dmin));
        Assert.Equal(new[] { 0.1, 0.2, 0.3 }, dmin);
        Assert.All(b.Data, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void InvalidBuffersAndCancelledRequestsAreRejected()
    {
        var b = Convert(Image(42));
        Assert.Throws<ArgumentException>(() => Engine.Analyze([new Frame(b, b)], [double.NaN, 0, 0]));
        Assert.Throws<ArgumentException>(() => Engine.Analyze([new Frame(b, b, new Buffer(20, 20))]));
        Assert.Throws<OperationCanceledException>(() => Engine.Analyze([new Frame(b, b)],
            cancellationToken: new CancellationToken(true)));
        b.Data[0] = float.NaN;
        Assert.Throws<ArgumentException>(() => Engine.Analyze([new Frame(b, b)]));
    }

    [Fact]
    public void DensityLimitsRetainOriginalNumericalBehavior()
    {
        foreach (double t in new[] { 0.0, 1e-8, 1e-4, 0.2, 1.0, 2.0 })
            Assert.Equal(-Math.Log10(Math.Max(t, Math.Pow(10, -4.0))),
                isolated::OpenRevelare.Core.DensityMath.DensityOf(t));
    }
}
