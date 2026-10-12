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

Repeated user tests establish that NegBase's normal automatic workflow defaults to **90% white
card / 685**. OpenRevelare therefore couples its automatic neutral picture-white detector to 685.
True dmax / 1000 remains a separate explicit mode for a measured maximum-density or specular
reference. Endpoint detection and Cineon placement must use the same standard; switching only the
placement number while retaining the other mode's measurement assumption is invalid.

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

## Seven-frame real-RAW checkpoint — 2026-10-11

The same seven Fujifilm GFX100S RAF files were re-analysed in the installed NegBase 1.3.1 build.
Its active preference was `dmaxTarget = 685`, so these results cannot be compared with an
OpenRevelare render whose measured endpoint is placed at 1000 or 1032. NegBase reported:

| RAF | scale R/G/B | displayed dmin R/G/B | displayed dmax R/G/B |
|---|---|---|---|
| 0017 | 2.0077 / 1.5497 / 1.2097 | 1.0223 / 0.7338 / 0.4017 | 0.2404 / 0.1125 / 0.0364 |
| 0018 | 0.7842 / 0.8628 / 1.0765 | 0.8650 / 0.5299 / 0.2575 | 0.0213 / 0.0183 / 0.0173 |
| 0019 | 1.8132 / 1.5182 / 1.1909 | 1.0720 / 0.6804 / 0.3579 | 0.2158 / 0.1004 / 0.0312 |
| 0020 | 1.6914 / 1.4957 / 1.1699 | 1.4216 / 0.8693 / 0.5502 | 0.2551 / 0.1246 / 0.0459 |
| 0021 | 0.6975 / 0.7367 / 0.8347 | 1.3711 / 0.9230 / 0.5469 | 0.0213 / 0.0179 / 0.0168 |
| 0022 | 1.6224 / 1.4327 / 1.0997 | 1.4406 / 0.8669 / 0.5340 | 0.2403 / 0.1140 / 0.0380 |
| 0023 | 1.7250 / 1.4880 / 1.1061 | 1.4210 / 0.8676 / 0.5525 | 0.2636 / 0.1231 / 0.0399 |

The previous OpenRevelare parity build selected, on 0020, a neutral component beginning exactly at
the 5% analysis inset, only 1.6% of the frame wide and 59.2% high. Its endpoint density was about
2.8, while NegBase's displayed endpoint implies a much shorter picture-reference span. This is a
film-frame/blocking-strip false positive, not evidence that NegBase uses a different inversion
formula. The spatial candidate now rejects boundary-attached, line-shaped and maximum-density
components before ranking the remaining neutral regions.

Follow-up comparison of 0018, 0019 and 0023 found that OpenRevelare's stored density spans were
much longer than NegBase's implied spans on 0018 and 0019, while 0023 was close. The old spatial
test required a neutral candidate to have nearly equal raw RGB densities. That is physically
incorrect before channel alignment: film dyes, camera filters and copy light give the channels
different slopes. Candidate ranking and chroma rejection now operate after normalising each
channel by its frame-level high-density scale. Coloured regions remain rejectable, while a neutral
white region may have different raw density spans in R, G and B.

The first normalised build still fell back on 0018 and 0019. Replaying the exact UniWB, half-size
and integer-box preview path showed why: residual opaque rails inside the saved crop occupied
nearly all of the top 5% population. Rejecting rail components after thresholding could never
recover picture pixels that had already been excluded from that population. Opaque-density pixels
are now removed before channel references and thresholds are computed, and the picture analysis
uses a 10% inset. On the three real RAFs this exposes internal neutral candidates with density spans
of approximately 0018 `0.97/1.17/1.43`, 0019 `0.92/1.10/1.29`, and 0023 `0.80/0.96/1.11`, instead
of falling back to the much denser frame rails. These values still require installed-build visual
comparison and are recorded as measurements, not a claim of complete NegBase parity.

## Installed-build validation — 2026-10-12

Commit `70854d8` was built on GitHub's Apple Silicon runner, installed, and run twice on 0018 and
once on 0019/0023. The repeated 0018 run produced no parameter change, so the earlier
first-run/reset/second-run dependence remains fixed. All three frames selected an internal
spatial-neutral component rather than a rail:

| RAF | OpenRevelare Dmin | OpenRevelare Dmax | measured span | selected region |
|---|---|---|---|---|
| 0018 | 0.011 / 0.069 / 0.515 | 0.980 / 1.244 / 1.949 | 0.969 / 1.174 / 1.434 | x=52.0%, y=25.0%, 4225 px |
| 0019 | 0.026 / 0.086 / 0.540 | 0.950 / 1.185 / 1.829 | 0.924 / 1.099 / 1.289 | x=42.9%, y=10.6%, 5757 px |
| 0023 | 0.057 / 0.118 / 0.568 | 0.852 / 1.077 / 1.683 | 0.796 / 0.959 / 1.115 | x=59.0%, y=69.7%, 2638 px |

The installed result is visibly much closer to NegBase on 0018/0019, but it is not yet a pixel
match. NegBase's currently displayed 0018/0019/0023 endpoints imply spans of approximately
`0.672/0.815/1.105`, `0.696/0.831/1.060`, and `0.732/0.848/1.141`. Its displayed channel scale
multiplied by each implied span is consistently about `1.262`; that stable relationship is useful
formula evidence, but does not reveal how NegBase selected the endpoint pixels.

0023 remains the closest endpoint match and is also the scene-rich frame the user identified as
the best roll reference. 0018/0019 still measure a deeper picture reference in OpenRevelare. The
next parity work must isolate candidate selection and input decode domain; changing the Cineon
placement constant to force these three images to agree would mix measurement with placement and
repeat the standard mismatch this branch removed.

## 0020 input-domain diagnosis — 2026-10-12

The installed build repeatedly produced the same 0020 endpoints: Dmin
`0.031/0.093/0.541`, Dmax `0.831/1.048/1.642`, spans `0.800/0.955/1.101`, and a 685 target. The
previously stored NegBase result displayed scale `1.691/1.496/1.170` and implied spans of roughly
`0.746/0.844/1.079`. Those slope differences do not predict OpenRevelare's observed warm cast:
relative to green, OpenRevelare's endpoint mapping strengthens blue rather than suppressing it.
Overall contrast is therefore not the cause of the yellow/orange difference.

An offline render then held the RAW decode, crop, endpoints, Cineon placement and analytic display
curve constant and changed only the interpretation of the three channels at the output matrix.
The existing path treated LibRaw's uncharacterized camera-native UniWB channels as ACEScg and
applied the large ACEScg-to-sRGB rotation. Treating the already endpoint-calibrated channel RGB as
sRGB instead removed the orange cast and closely approached the stored NegBase appearance. The
managed pipeline now makes that distinction from source provenance: camera-native RAW uses the
selected output primaries for the analytic rendering, while characterized sources that really
entered the ACEScg working space retain the ACEScg-to-output conversion. Print-film LUTs continue
to consume the same untouched Cineon signal.

Re-running NegBase automatic on 0020 replaced the stored preferred result with a dark blue result
(`dmin 1.3691/0.9220/0.5499`, `dmax 0.0261/0.0182/0.0127`, scale
`0.734/0.741/0.771`) and repeated identically. This proves the preferred NegBase frame contained
previous endpoint or channel work; it must not be cited as NegBase's current pure-auto output.
