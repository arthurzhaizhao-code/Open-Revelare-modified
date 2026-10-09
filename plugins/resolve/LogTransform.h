#pragma once
#include <algorithm>
#include <cmath>

namespace revelare {
// Input and output have identical RGB primaries. Density inversion ONLY, no CST/LUT.
// 1032 is the upstream endpoint code, not diffuse white. Preserve values outside [0,1].
inline float logChannel(float transmission, double dmin, double dmax, double slope,
                        double shiftCode = 0.0) {
    const double density = -std::log10(std::max(static_cast<double>(transmission), 1e-4));
    return static_cast<float>((95.0 + shiftCode
        + 937.0 * slope * (density - dmin) / (dmax - dmin)) / 1023.0);
}
inline bool validEndpoints(const double* dmin, const double* dmax, const double* slope,
                           const double* shiftCode = nullptr) {
    for (int c = 0; c < 3; ++c)
        if (!std::isfinite(dmin[c]) || !std::isfinite(dmax[c]) || !std::isfinite(slope[c])
            || (shiftCode && !std::isfinite(shiftCode[c]))
            || dmax[c] <= dmin[c] || slope[c] <= 0) return false;
    return true;
}
}
