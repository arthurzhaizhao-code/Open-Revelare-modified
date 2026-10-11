# NegBase 1.3.1 comparison notes

This document records reproducible, read-only observations of a locally installed NegBase build
and compares its calibration model with the Resolve prototype. It contains no NegBase code.

## Reference build

- App: NegBase 1.3.1 (build 19), Apple Silicon arm64
- Executable SHA-256: `f64d8a4072b4b9f6046cac99650451a91e67ac7f3a3aa44f9164063da3856503`
- Metal library SHA-256: `3791c37ad4357ecadf47ab6a94c28887cc38217b9315bef6e56e36941143c641`
- UI/runtime: SwiftUI, Metal, Core Image and a statically linked LibRaw

## Verified processing structure

The Metal library exposes separate kernels for:

1. RAW conversion and optional demosaic
2. `cineonInvert`
3. `cineonPreview`
4. LUT preview/export and gamma/sRGB export
5. Histogram, RGB parade and vectorscope construction
6. Geometry and median chroma filtering

This confirms that inversion is kept separate from the display/LUT and creative rendering stages.

The `cineonInvert` kernel has the following arguments:

```text
input texture
output texture
dmin      float3
scale     float3
steps     float
offset    float
bitdepth  float
flipY     uint
```

Synthetic-pixel probing establishes its per-channel transform exactly:

```text
output_c = (offset + scale_c * log10(dmin_c / input_c) / steps) / bitdepth
```

For a conventional Cineon call, `offset = 95`, `steps = 0.002`, and `bitdepth = 1023`.
Values are not clipped by this kernel: transmission brighter than the sampled film base produces
sub-black values, while denser values can produce super-white values.

Although the UI calls the displayed endpoint numbers “density values”, the values supplied as
`dmin` to the Metal kernel are linear transmission samples. A current example showed:

```text
dmin (film-base transmission): 0.3591, 0.2138, 0.1160
dmax (dense-negative sample):  0.0480, 0.0236, 0.0080
scale:                         1.4903, 1.2813, 1.0110
```

The endpoint density spans implied by those samples are:

```text
log10(dmin / dmax): 0.8740, 0.9571, 1.1614
```

## Dmax placement

NegBase provides two explicit Dmax modes:

- **90% white card**: target Cineon code **685**
- **True dmax**: target Cineon code **1000**

The UI describes the first as the mode for a diffuse white reference such as paper or white
clothing, and the second as the mode for actual maximum density such as a specular highlight.

This validates code 685 when the measured sample is known to be diffuse white. The automatic default mode has not been established by the observations below. Neither the
existence of these controls nor an automatically selected bright endpoint proves that it is a
physical maximum-density measurement. The earlier recommendation to default to 1000 was an
unverified design inference, not a verified NegBase behavior.

## Mathematical comparison with RevelareNegative

The Resolve prototype currently computes:

```text
density_c = -log10(input_c)
position_c = (density_c - Dmin_c) / (Dmax_c - Dmin_c)
output_c = (95 + shift_c
            + (whiteCode - 95) * gain_c * midtone(position_c)) / 1023
```

Ignoring the optional shift and midtone curve, this is algebraically the same transform as
NegBase. The correspondence is:

```text
NegBase dmin_c = 10^(-Revelare Dmin_c)
NegBase scale_c = 0.002 * (whiteCode - 95) * gain_c
                  / (Revelare Dmax_c - Revelare Dmin_c)
```

Therefore the NegBase inversion kernel itself is not a better replacement for our current core.
Both hold the film-base point fixed and change each channel's slope independently. This also
explains the observed UI behaviour: changing one channel scale stretches only that channel around
the locked base.

## Automatic analysis difference

Swift reflection metadata shows that NegBase's automatic result contains:

```text
AutoDetectResult
  dmin
  dmax
  scale
  rect
  angle
  confidence
  message
```

Our Resolve host analysis returns Dmin and Dmax, then resets all three gain controls to 1. NegBase
stores the derived endpoint slope directly as `scale`. A controlled mode change on the same image
kept Dmin and Dmax fixed and recomputed scale as:

```text
True-dmax scale: 2.2158, 2.0222, 1.6698
```

The ratios of those values match the inverse ratios of the three measured density spans. This
shows that automatic `scale` is the per-channel endpoint normalisation, not evidence of an extra
neutral-point solve. The earlier visible values `1.4903, 1.2813, 1.0110` included a retained manual
channel alignment.

Our representation stores the same normalisation in Dmin/Dmax and leaves gain at 1. NegBase
stores it in Dmin plus scale and then exposes scale itself as the manual control. The two designs
have the same degrees of freedom.

NegBase builds per-channel histograms whose bins retain count and RGB sums, with a separate
cropped-histogram path. This is consistent with statistical endpoint detection rather than a
learned model. OpenRevelare already has substantially more explicit roll-level base and highlight
evidence handling, so the existence of NegBase's automatic detector alone is not a reason to
replace ours.

## Features worth adopting

1. Present code 685 as **Diffuse white / 90% white card**.
2. Add a one-click **True Dmax / code 1000** placement while retaining the advanced numeric field.
3. Keep Dmin locked while channel gains stretch independently; the current implementation already
   has the correct pivot and channel isolation.
4. Keep automatic endpoint normalisation separate from later manual gain trimming. The current
   Resolve prototype already does this; the UI can explain the relationship more clearly.

## Features not worth copying yet

- The inversion formula: it is already equivalent.
- Printer-light and creative grading controls: NegBase applies these after inversion, and Resolve
  already provides a stronger downstream grading environment.
- RAW decoding and export infrastructure: the Resolve workflow deliberately receives linear RGB
  from Resolve and does not need NegBase's LibRaw/export path.

## Next controlled experiment

Use synthetic or measured inputs with a known film-base patch, a diffuse neutral patch, a specular
patch and coloured scene regions. For each input, record NegBase's automatic Dmin and Dmax, then
compare them with OpenRevelare's endpoint estimator. The remaining useful question is how NegBase
chooses the two endpoint samples, especially when the frame contains no reliable diffuse white.

## Real-RAW observation: 翻拍胶片0023.raf

The isolated research copy of NegBase loaded the 129.2 MB Fujifilm RAF directly. Before automatic
analysis it displayed the expected orange negative. A single automatic pass reported both Dmin
and Dmax as found and produced a plausible positive with neutral concrete, blue sky and separated
green foliage, while its creative controls remained at their defaults (exposure 0, contrast 1,
saturation 1, temperature 0 and tint 0).

The frame includes a broad, continuous film border. This is strong evidence for the normal,
high-confidence path but it does not distinguish NegBase's fallback when no carrier is visible.
It also does not support an extra hidden neutral-grey solve: the visible result is fully explained
by the already verified per-channel endpoint normalisation. A useful follow-up is the same-frame
comparison after excluding the border, followed by a frame without any reliable diffuse white.

The test supports adopting NegBase's explicit endpoint-placement language. The Resolve plug-in now
offers one-click **Diffuse white 685** and **True Dmax 1000** actions while retaining its custom
numeric Cineon placement. No NegBase processing code is copied.

## Audit correction — 2026-10-10

The recorded kernel probes establish the inversion formula, not the automatic endpoint selection
algorithm. Metadata and plausible output do not establish its sample masks, thresholds, default
white placement, or absence of other CPU-side processing. These require controlled input/output
comparisons. The prior claim that OpenRevelare should be preferred because its implementation is
more explicit is not accuracy evidence. See the local calibration-audit results for a same-input
regression observed between actual OpenRevelare plugin libraries.

## Controlled candidate-selection experiment — 2026-10-10

The first controlled NegBase run used identical synthetic negatives containing a uniform film-base
border, a broad diffuse-white patch, a smaller neutral high-density patch, a strongly coloured
high-density patch, and variants where the neutral patch was 8×8, 16×16, 32×32 or 64×64 pixels.
The auto result changed when the neutral patch reached 32×32, but not at 8×8 or 16×16. A
strongly coloured dense patch did not change the result. The two inputs with the 32×32 neutral
patch produced identical output for every pixel shared by the inputs, even when one also contained
the diffuse patch.

This is evidence for a minimum-area, same-source, chroma-consistent candidate, followed by a
density choice. It is not evidence for a fixed 685 or 1000 placement. The Resolve single-frame
path now has a guarded experimental candidate detector with the existing percentile estimator as
fallback; it has not yet been packaged or installed.
