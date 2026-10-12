using OpenRevelare.Core;

namespace OpenRevelare.Gui.ViewModels;

/// <summary>
/// Resolves the roll-owned black endpoint before a current-frame highlight solve.  A RAW frame
/// whose triple is exactly zero has lost its film-base calibration (normally through an older
/// full-reset path); it must not be treated as a real clear carrier.  Prefer persisted roll
/// evidence, then the component medians of calibrated sibling frames.
/// </summary>
internal static class SingleFrameFilmBase
{
    private const double Epsilon = 1e-6;

    public static double[]? Resolve(
        double[] current,
        bool requirePhysicalRawBase,
        FilmBaseEstimate? savedRollBase,
        IEnumerable<double[]> siblingEndpoints)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(siblingEndpoints);
        if (IsUsable(current, requirePhysicalRawBase)) return (double[])current.Clone();

        if (savedRollBase is { TBase.Length: 3 }
            && savedRollBase.TBase.All(v => double.IsFinite(v) && v > 0.0))
            return savedRollBase.TBase.Select(v => -Math.Log10(v)).ToArray();

        double[][] candidates = siblingEndpoints
            .Where(v => IsUsable(v, requirePhysicalRawBase: true))
            .Select(v => (double[])v.Clone())
            .ToArray();
        if (candidates.Length == 0) return null;

        var resolved = new double[3];
        for (int c = 0; c < 3; c++)
        {
            double[] values = candidates.Select(v => v[c]).OrderBy(v => v).ToArray();
            int middle = values.Length / 2;
            resolved[c] = values.Length % 2 == 1
                ? values[middle]
                : (values[middle - 1] + values[middle]) * 0.5;
        }
        return resolved;
    }

    private static bool IsUsable(double[] values, bool requirePhysicalRawBase)
    {
        if (values.Length != 3 || values.Any(v => !double.IsFinite(v) || v < 0.0)) return false;
        return !requirePhysicalRawBase || values.Any(v => v > Epsilon);
    }
}
