using OpenRevelare.Core;

namespace OpenRevelare.Calibration;

/// <summary>Caller-prepared buffers. Base retains film edges; picture excludes borders.
/// Mask buffers and board threshold must be in the same domain. The engine does no decoding,
/// colour conversion, geometry or UI mutation. Callers must not mutate buffers during analysis.</summary>
public sealed record CalibrationFrame(ImageBuffer Base, ImageBuffer Picture,
    ImageBuffer? BaseMask = null, ImageBuffer? PictureMask = null);

public sealed record CalibrationResult(double[] DMin, double[] DMax,
    FilmBaseEvidence? BaseEvidence, FilmBaseEstimate? RollBaseDiagnostics,
    HighlightEndpointEstimate? HighlightDiagnostics, bool UsedHighlightFallback,
    bool UsedLockedDMin);

public static class CalibrationEngine
{
    /// <summary>Returns a complete candidate or throws. Never modifies caller parameters or
    /// images. Passing lockedDMin skips ALL base estimation, including on a single frame.
    /// Cancellation is checked between estimators; their existing inner loops are unchanged.</summary>
    public static CalibrationResult Analyze(IReadOnlyList<CalibrationFrame> frames,
        double[]? lockedDMin = null, double? boardThreshold = null,
        bool allowNeutralCarrier = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0) throw new ArgumentException("No analysis frames.", nameof(frames));
        if (boardThreshold is { } threshold && (!double.IsFinite(threshold) || threshold <= 0))
            throw new ArgumentOutOfRangeException(nameof(boardThreshold));
        foreach (var f in frames)
        {
            ArgumentNullException.ThrowIfNull(f);
            ValidateImage(f.Base);
            ValidateImage(f.Picture);
            ValidatePair(f.Base, f.BaseMask);
            ValidatePair(f.Picture, f.PictureMask);
        }
        cancellationToken.ThrowIfCancellationRequested();
        double[] dmin;
        double[]? measuredRollBase = null;
        FilmBaseEvidence? evidence = null;
        FilmBaseEstimate? baseDiagnostics = null;
        if (lockedDMin is not null)
        {
            if (lockedDMin.Length != 3 || lockedDMin.Any(v => !double.IsFinite(v)))
                throw new ArgumentException("Dmin must contain three finite densities.", nameof(lockedDMin));
            dmin = (double[])lockedDMin.Clone();
        }
        else
        {
            double[] tb;
            if (frames.Count == 1)
            {
                // Preserve AutoFilmBaseFromRoll(useMode: true)'s single-frame order.
                var f = frames[0];
                var mask = f.BaseMask ?? f.Base;
                var values = ReferenceEquals(mask, f.Base) ? null : f.Base;
                var pick = FilmBase.EstimateTBaseByMode(mask, boardThreshold, values);
                evidence = FilmBaseEvidence.PhysicalMode;
                if (pick is null)
                {
                    pick = FilmBase.EstimateTBaseFromEdgeSliver(mask, values,
                        allowNeutralCarrier, boardThreshold);
                    evidence = FilmBaseEvidence.PhysicalEdgeSliver;
                }
                if (pick is null)
                {
                    pick = FilmBase.EstimateTBaseFromRoll(new[] { mask }, boardThreshold,
                        values is null ? null : new[] { values });
                    evidence = FilmBaseEvidence.ContentInference;
                }
                tb = pick;
            }
            else
            {
                baseDiagnostics = FilmBase.EstimateTBaseFromRollDetailed(
                    frames.Select(f => f.BaseMask ?? f.Base).ToArray(), boardThreshold,
                    frames.Select(f => f.Base).ToArray(), allowNeutralCarrier);
                tb = baseDiagnostics.TBase;
                measuredRollBase = tb;
                evidence = baseDiagnostics.Evidence;
            }
            dmin = tb.Select(v => -Math.Log10(Math.Max(v, 1e-10))).ToArray();
        }
        // Original roll path uses the measured transmission directly; avoid a log/pow
        // round trip there. Single-frame and locked-base paths reconstruct from Dmin.
        var tbase = measuredRollBase ?? dmin.Select(v => Math.Pow(10.0, -v)).ToArray();
        if (tbase.Any(v => !double.IsFinite(v) || v <= 0))
            throw new ArgumentException("Dmin cannot be represented as positive transmittance.");
        cancellationToken.ThrowIfCancellationRequested();
        var images = frames.Select(f => f.Picture).ToArray();
        var masks = frames.Select(f => f.PictureMask ?? f.Picture).ToArray();
        var highlight = FilmBase.DetectDMaxPerChannelFromRollDetailed(
            images, tbase, 90.0, masks, boardThreshold, protectIndependentChannelExtrema: true);
        cancellationToken.ThrowIfCancellationRequested();
        var span = highlight?.Density ?? FilmBase.AutoWbHighFromRoll(
            masks, tbase, boardThreshold, images);
        if (span.Any(v => !double.IsFinite(v) || v <= 0))
            throw new InvalidOperationException("No usable highlight span; retain previous calibration.");
        var dmax = span.Select((v, c) => v + dmin[c]).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new(dmin, dmax, evidence, baseDiagnostics, highlight,
            highlight is null, lockedDMin is not null);
    }

    private static void ValidatePair(ImageBuffer value, ImageBuffer? mask)
    {
        if (mask is null || ReferenceEquals(mask, value)) return;
        ValidateImage(mask);
        if (value.Width != mask.Width || value.Height != mask.Height)
            throw new ArgumentException("Mask and value buffers must have identical dimensions.");
    }

    private static void ValidateImage(ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width < 20 || image.Height < 20)
            throw new ArgumentException("Analysis buffers must be at least 20 by 20 pixels.");
        if (!double.IsFinite(image.SourceQuantisationStep) || image.SourceQuantisationStep < 0)
            throw new ArgumentException("Invalid source quantisation step.");
        if (image.Data.Any(v => !float.IsFinite(v) || v < 0))
            throw new ArgumentException("Analysis requires finite nonnegative linear transmission.");
    }
}
