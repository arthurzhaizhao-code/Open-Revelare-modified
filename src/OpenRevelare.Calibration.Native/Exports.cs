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
    /// Output: 3 Dmin, 3 Dmax, highlight confidence (-1 for fallback), fallback flag.
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
            var candidate = CalibrationEngine.Analyze([new CalibrationFrame(full, picture)], dmin);
            for (int c = 0; c < 3; c++)
            {
                result[c] = candidate.DMin[c];
                result[c + 3] = candidate.DMax[c];
            }
            result[6] = candidate.HighlightDiagnostics?.Confidence ?? -1;
            result[7] = candidate.UsedHighlightFallback ? 1 : 0;
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
