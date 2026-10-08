# Decoder-free calibration core

This library compiles the existing FilmBase, Sprocket, ImageBuffer, NumpyStats,
ParallelSweep, CoreText and DensityMath sources by reference. It has no package,
GUI, decoder, file-loading or colour-management dependency. The desktop Core
continues compiling those same sources; estimator math is not forked.

Namespaces of shared types remain OpenRevelare.Core. Do not reference both
assemblies without aliases: they intentionally contain the same shared types.
The parity test project demonstrates an `isolated` extern alias. The eventual
OFX host should reference only this library, not the desktop Core.

CalibrationEngine.Analyze takes prepared linear buffers. Base buffers retain
film edges; picture buffers contain the analysis region. Optional mask buffers
must match dimensions and the threshold domain. Crop, downsample and colour
conversion are caller responsibilities. Unknown source quantisation is 0.

Passing lockedDMin skips base estimation; the result owns a cloned Dmin array.
A single frame preserves the GUI's mode/edge/tail fallback order; multiple frames
use the detailed roll estimator. Highlights preserve the detailed detector and
AutoWbHighFromRoll fallback. Outputs are absolute density endpoints, NOT Nami
slider values or display RGB. No old state is changed: apply a candidate only
after the entire call succeeds. Cancellation is cooperative between estimators,
not during their unchanged loops. Results and buffers are not immutable; hosts
must own/synchronize them and not mutate inputs during a call.

Build independently:

    dotnet build src/OpenRevelare.Calibration -c Release

Run isolated-vs-desktop parity and boundary tests:

    dotnet test src/OpenRevelare.Calibration.Tests -c Release

The parity suite checks call orchestration and separately compiled shared source,
not a different-language port or equivalence of the full colour pipelines.
Existing desktop regression tests remain the historical algorithm guard.

Not implemented here: OFX native ABI, Metal rendering, ROI controls, album image
access, grade application, parameter persistence, or a NativeAOT packaging choice.
This is an extraction boundary, not an installable Resolve plugin.

Source license: repository GPL-3.0 (see ../../LICENSE). Preserve upstream authorship.
