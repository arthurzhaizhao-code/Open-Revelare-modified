namespace OpenRevelare.Core;

/// <summary>Shared measurement limits, independent of rendering parameters.</summary>
public static class DensityMath
{
    public const double DensityCeiling = 4.0;
    public const double RealDensityCeiling = 3.0;
    private static readonly double Floor = Math.Pow(10.0, -DensityCeiling);
    public static double DensityOf(double transmittance) =>
        -Math.Log10(Math.Max(transmittance, Floor));
}
