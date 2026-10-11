namespace OpenRevelare.Core;

/// <summary>
/// Places the measured highlight endpoint on a chosen Cineon code while keeping the measured
/// film base fixed at code 95. This is the same independent placement step exposed by NegBase:
/// 685 for a 90% diffuse-white sample and 1000 for a true maximum-density sample.
/// </summary>
public static class CineonHighlightPlacement
{
    public static void ApplyToLinearPositive(float[] data, double targetCode)
    {
        double black = FrameParams.CineonBlackCode;
        double ceiling = FrameParams.CineonWhiteCode;
        double target = Math.Clamp(targetCode, black + 1.0, ceiling);
        if (target == ceiling) return;

        double scale = (target - black) / (ceiling - black);
        double densityPerCode = FrameParams.CineonDensityPerCode;
        for (int i = 0; i < data.Length; i++)
        {
            double code = ceiling + Math.Log10(Math.Max(data[i], 1e-10f)) / densityPerCode;
            double placed = black + (code - black) * scale;
            data[i] = (float)Math.Pow(10.0, (placed - ceiling) * densityPerCode);
        }
    }
}
