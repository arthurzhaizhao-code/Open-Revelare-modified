# Revelare Negative OFX — 0.1 prototype

Input: linear negative RGB prepared by Resolve. Output: Cineon-style Log in the
SAME primaries. No RAW decoder, input colour conversion, display LUT or Rec.709
conversion is included. In the user's existing Rec.2020/Cineon → ARRI LogC3 CST
chain, the upstream linear input must actually be Rec.2020. This plugin does not
infer that from project settings.

For Resolve interoperability, automatic Dmax is placed at Cineon code 685 by default.
This leaves 685–1023 as highlight headroom for a downstream Cineon display transform or
film-look LUT instead of expanding the detected picture range to full scale first. The
"Cineon white placement" control can be raised toward 1032 for an extended-range workflow.

This prototype replaces the Nami node. It does NOT stack on top of Nami inversion.
Enable inversion defaults off. Analyze frame takes the current upstream image,
box downsamples to at most 1024 per edge, and calls the existing OpenRevelare
calibration core via NativeAOT. Successful analysis writes Dmin/Dmax, resets gain and shift,
enables inversion and locks Dmin. With the lock on, further analysis only updates
Dmax. Unlock explicitly to measure film base again. Reset gain + shift preserves both
endpoints. Results live in ordinary persistent, nonanimated OFX parameters.

Each gain stretches that channel about its fixed Dmin. Each Shift then moves the
complete output channel in 10-bit code values without changing the stored Dmin.
These are not the raw Nami parameter values. Output is unclamped; the measured Dmax lands
at the selected Cineon white placement before Shift.
Inspect channel independence BEFORE downstream CST/LUT. Final Rec.709 may mix RGB.

ROI controls are normalized left, bottom, width, height. They restrict highlight
analysis, NOT output geometry; base analysis retains the whole input to find film
edges. Coordinates use OFX bottom-left origin. The yellow viewer marker sets the neutral
sample point. Input to automatic analysis must be finite, nonnegative and opaque; place it
before alpha-generating geometry effects. Source quantisation is unknown (0).
The existing detector also uses its default 5% inset inside the selected ROI.

Implemented: CPU float32 RGB/RGBA rendering, preserved alpha, single-frame automatic
analysis, Dmin lock, density endpoints, independent gain and Shift, numeric ROI, native
calibration error reporting. No per-render auto-analysis or file access.

Neutral-grey calibration adds a genuine third point without moving Dmin or Dmax. Select a
known neutral patch with the yellow viewer marker, then press Sample neutral grey. Enable
"Click or drag sample point" and click directly in the Resolve viewer to reposition it; dragging moves
the marker, while Sample size controls the sampled area. By default the common
level is the mean of the three readings, so an ordinary neutral object changes colour without
claiming a reflectance. Enable Known grey card only for a measured card; 470 is the standard
18% grey target. The plugin reports the three current sample codes and solves
one bounded midtone gamma per channel. Gamma is applied between the endpoints and extended
linearly outside them, so black, white, sub-black and super-white remain finite. A random parade
extreme is not a neutral reference; use a grey card or an object whose neutrality is known.

Not implemented: roll/Photo Album acquisition and sharing, interactive ROI overlay,
Metal acceleration and explicit input gamut management. Analysis is
synchronous and may briefly block the UI. Host integration remains experimental;
compilation and ABI tests do not establish that Resolve accepts every callback.

## Build

On macOS with Command Line Tools and .NET 8 SDK:

    bash plugins/resolve/build-macos.sh /private/tmp/revelare-plugin

No third-party package dependency in the calibration library. NativeAOT publish
requires its .NET build/runtime packs. The OFX headers are vendored from the local
Resolve 21.1.1 SDK (OpenFX-1.4/include); original BSD-3-Clause notices are retained,
with the accompanying SDK license in include/LICENSE. Plugin and calibration code
are distributed under the repository GPL-3.0; preserve upstream attribution.

CI produces RevelareNegative-osx-arm64.zip and a double-click `.pkg` on macos-latest. The script also supports
an Intel host but this workflow does not validate Intel. The bundle contains the
OFX and its NativeAOT dylib. Ad-hoc signed, not notarized. Does not require a .NET
runtime installation on the user's machine.

## Install and inspect

After saving work and quitting Resolve, either open the generated `.pkg`, or copy
RevelareNegative.ofx.bundle to /Library/OFX/Plugins manually, then reopen Resolve. The package
is not Developer ID signed or notarized; if macOS blocks it, use Privacy & Security → Open
Anyway. Find “Revelare Negative (Prototype)” under
“Film Negative”. Use a duplicate grade/version for inspection. Installing alone
does not change the existing project. No script here quits Resolve, edits the
user's grade, or installs system-wide automatically.

The build runs Log endpoint/anchor/channel-isolation tests, NativeAOT ABI and
single-frame success/error/transaction checks, then validates plugin exports and
bundle signature. Resolve host loading and real image comparisons must be reported
separately from those tests.
