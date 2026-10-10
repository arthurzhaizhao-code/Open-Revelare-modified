using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using OpenRevelare.Core;

namespace OpenRevelare.Calibration.Native;

public static unsafe class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "or_calibration_abi", CallConvs = [typeof(CallConvCdecl)])]
    public static int Abi() => 1;

    /// <summary>RGB float32 packed input; ROI in pixel coordinates, origin matches the buffer.
    /// Output: 3 Dmin, 3 Dmax, highlight confidence, diagnostic flags.
    /// Buffers remain caller owned. Result is written only on success. No exception crosses ABI.</summary>
    [UnmanagedCallersOnly(EntryPoint = "or_analyze_single", CallConvs = [typeof(CallConvCdecl)])]
    public static int Analyze(float* rgb, int width, int height,
        int x, int y, int roiWidth, int roiHeight, double* lockedDmin,
        double* result, byte* error, int errorCapacity)
    {
        try
        {
            if (rgb == null || result == null || width < 20 || height < 20
                || width > 4096 || height > 4096 || x < 0 || y < 0
                || roiWidth < 20 || roiHeight < 20 || roiWidth > width || roiHeight > height
                || x > width - roiWidth || y > height - roiHeight)
                throw new ArgumentException("Invalid analysis buffer/ROI (minimum 20 pixels, maximum 4096 per edge).");
            var data = new float[checked(width * height * 3)];
            Marshal.Copy((IntPtr)rgb, data, 0, data.Length);
            var full = new ImageBuffer(width, height, data);
            var picture = new ImageBuffer(roiWidth, roiHeight);
            for (int row = 0; row < roiHeight; row++)
                Array.Copy(data, ((row + y) * width + x) * 3,
                    picture.Data, row * roiWidth * 3, roiWidth * 3);
            double[]? dmin = lockedDmin == null ? null : [lockedDmin[0], lockedDmin[1], lockedDmin[2]];
            // The OFX picture buffer is already an explicit user-controlled ROI with a second
            // edge inset inside the endpoint detector.  Treating its darkest histogram valley
            // as an opaque mask card can delete real subject matter (hair, clothing, furniture)
            // and collapse Dmax onto Dmin, producing an all-white render.  The standalone app's
            // full-frame/roll path keeps that mask; this bounded OFX ROI deliberately does not.
            var candidate = CalibrationEngine.Analyze([new CalibrationFrame(full, picture)], dmin,
                excludeDarkValley: false, stableSingleFrameHighlight: true,
                requirePhysicalBase: dmin is null);
            for (int c = 0; c < 3; c++)
            {
                result[c] = candidate.DMin[c];
                result[c + 3] = candidate.DMax[c];
            }
            result[6] = candidate.HighlightDiagnostics?.Confidence ?? -1;
            // bit 0: legacy highlight fallback; bit 1: stable single-frame highlight;
            // bits 2-3: Dmin evidence (1=mode, 2=edge sliver, 3=content inference).
            int flags = candidate.UsedHighlightFallback ? 1 : 0;
            flags |= 2; // this ABI entry point is the bounded single-frame Resolve path
            if (candidate.BaseEvidence is { } evidence) flags |= ((int)evidence + 1) << 2;
            result[7] = flags;
            if (error != null && errorCapacity > 0) error[0] = 0;
            return 0;
        }
        catch (Exception ex)
        {
            if (error != null && errorCapacity > 0)
            {
                byte[] message = Encoding.UTF8.GetBytes(ex.Message);
                int n = Math.Min(message.Length, errorCapacity - 1);
                Marshal.Copy(message, 0, (IntPtr)error, n);
                error[n] = 0;
            }
            return 1;
        }
    }
}
