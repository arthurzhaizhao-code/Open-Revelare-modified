#pragma once
#include <algorithm>
#include <cmath>

namespace revelare {
// Input and output have identical RGB primaries. Density inversion ONLY, no CST/LUT.
// 1032 is the upstream endpoint code, not diffuse white. Preserve values outside [0,1].
inline double midtoneCurve(double x, double gamma) {
    // Gamma is the third, independent calibration degree of freedom.  Keep both
    // measured endpoints exact and extend linearly outside them so super-white
    // and sub-black values remain finite rather than being clipped by pow().
    if (gamma == 1.0) return x; // preserve the old affine path exactly
    if (x < 0.0) return x;
    if (x > 1.0) return 1.0 + gamma * (x - 1.0);
    return std::pow(x, gamma);
}
inline float logChannel(float transmission, double dmin, double dmax, double slope,
                        double shiftCode = 0.0, double midtoneGamma = 1.0,
                        double whiteCode = 1032.0) {
    const double density = -std::log10(std::max(static_cast<double>(transmission), 1e-4));
    const double position = (density - dmin) / (dmax - dmin);
    return static_cast<float>((95.0 + shiftCode
        + (whiteCode - 95.0) * slope * midtoneCurve(position, midtoneGamma)) / 1023.0);
}
inline bool validEndpoints(const double* dmin, const double* dmax, const double* slope,
                           const double* shiftCode = nullptr, const double* midtoneGamma = nullptr) {
    for (int c = 0; c < 3; ++c)
        if (!std::isfinite(dmin[c]) || !std::isfinite(dmax[c]) || !std::isfinite(slope[c])
            || (shiftCode && !std::isfinite(shiftCode[c]))
            || (midtoneGamma && (!std::isfinite(midtoneGamma[c]) || midtoneGamma[c] <= 0))
            || dmax[c] <= dmin[c] || slope[c] <= 0) return false;
    return true;
}
}
