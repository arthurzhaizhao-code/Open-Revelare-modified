using OpenRevelare.Core;
using Xunit;

namespace OpenRevelare.Tests;

public sealed class RgbChannelAlignmentTests
{
    [Fact]
    public void ChangingRedLeavesGreenAndBlueBitIdentical()
    {
        var pixels = new[] { 0.10f, 0.20f, 0.30f, 0.70f, 0.40f, 0.05f };
        var before = (float[])pixels.Clone();
        var cal = new FrameParams
        {
            RgbAlignShift = new[] { 102.3, 0.0, 0.0 },
            RgbAlignGain = new[] { 1.2, 1.0, 1.0 },
        };

        RgbChannelAlignment.Apply(pixels, cal);

        Assert.NotEqual(before[0], pixels[0]);
        Assert.NotEqual(before[3], pixels[3]);
        Assert.Equal(before[1], pixels[1]);
        Assert.Equal(before[2], pixels[2]);
        Assert.Equal(before[4], pixels[4]);
        Assert.Equal(before[5], pixels[5]);
    }

    [Fact]
    public void NeutralTrimIsIdentityAndDoesNotTouchDmin()
    {
        var cal = new FrameParams
        {
            DMinPerChannel = new[] { 0.09, 0.29, 0.54 },
            RgbAlignShift = new[] { 0.0, 0.0, 0.0 },
            RgbAlignGain = new[] { 1.0, 1.0, 1.0 },
        };
        var pixels = new[] { 0.1f, 0.2f, 0.3f };
        RgbChannelAlignment.Apply(pixels, cal);
        Assert.Equal(new[] { 0.09, 0.29, 0.54 }, cal.DMinPerChannel);
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, pixels);
    }
}
