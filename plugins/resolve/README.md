# Revelare Negative OFX — 0.1 prototype

Input: linear negative RGB prepared by Resolve. Output: Cineon-style Log in the
SAME primaries. No RAW decoder, input colour conversion, display LUT or Rec.709
conversion is included. In the user's existing Rec.2020/Cineon → ARRI LogC3 CST
chain, the upstream linear input must actually be Rec.2020. This plugin does not
infer that from project settings.

This prototype replaces the Nami node. It does NOT stack on top of Nami inversion.
Enable inversion defaults off. Analyze frame takes the current upstream image,
box downsamples to at most 1024 per edge, and calls the existing OpenRevelare
calibration core via NativeAOT. Successful analysis writes Dmin/Dmax, resets gain and shift,
enables inversion and locks Dmin. With the lock on, further analysis only updates
Dmax. Unlock explicitly to measure film base again. Reset gain + shift preserves both
endpoints. Results live in ordinary persistent, nonanimated OFX parameters.

Each gain stretches that channel about its fixed Dmin. Each Shift then moves the
complete output channel in 10-bit code values without changing the stored Dmin.
These are not the raw Nami parameter values. Output is unclamped (the upstream
white endpoint is code 1032 before Shift).
Inspect channel independence BEFORE downstream CST/LUT. Final Rec.709 may mix RGB.

ROI controls are normalized left, bottom, width, height. They restrict highlight
analysis, NOT output geometry; base analysis retains the whole input to find film
edges. Coordinates use OFX bottom-left origin. There is no drag rectangle overlay
yet. Input to automatic analysis must be finite, nonnegative and opaque; place it
before alpha-generating geometry effects. Source quantisation is unknown (0).
The existing detector also uses its default 5% inset inside the selected ROI.

Implemented: CPU float32 RGB/RGBA rendering, preserved alpha, single-frame automatic
analysis, Dmin lock, density endpoints, independent gain and Shift, numeric ROI, native
calibration error reporting. No per-render auto-analysis or file access.

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

CI produces RevelareNegative-osx-arm64.zip on macos-latest. The script also supports
an Intel host but this workflow does not validate Intel. The bundle contains the
OFX and its NativeAOT dylib. Ad-hoc signed, not notarized. Does not require a .NET
runtime installation on the user's machine.

## Install and inspect

After saving work and quitting Resolve, copy RevelareNegative.ofx.bundle to
/Library/OFX/Plugins and reopen Resolve. Find “Revelare Negative (Prototype)” under
“Film Negative”. Use a duplicate grade/version for inspection. Installing alone
does not change the existing project. No script here quits Resolve, edits the
user's grade, or installs system-wide automatically.

The build runs Log endpoint/anchor/channel-isolation tests, NativeAOT ABI and
single-frame success/error/transaction checks, then validates plugin exports and
bundle signature. Resolve host loading and real image comparisons must be reported
separately from those tests.
