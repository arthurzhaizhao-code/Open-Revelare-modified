# NegBase parity plan

This file separates observed behaviour from hypotheses. A feature is not called “matched” until
the same linear input produces recorded endpoint values and output pixels in both applications.

| Area | NegBase observation | Evidence | OpenRevelare status | Next verification |
|---|---|---|---|---|
| Inversion | Per-channel Cineon affine; film base is fixed while channel slope changes | Synthetic kernel probes | Mathematically equivalent | Pixel comparison on identical linear TIFF |
| Highlight placement | 90% white card → 685; true Dmax → 1000 | UI and controlled mode switch | Implemented as an independent mapping | Confirm endpoints remain byte-identical when switching |
| Manual channel alignment | Each channel moves independently around the fixed base | UI observation and formula | Existing Cineon shift/gain preserved | Compare YRGB movement channel by channel |
| Dmin | Spatially continuous film rebate is preferred; value-only bright tails are insufficient | Real RAF crop experiment | Inset edge rebate recognition added; manual native ROI already works | Record automatic sample mask on `0023.raf` |
| Dmax candidate | Neutral connected 32×32/64×64 regions can win; 8×8/16×16 and coloured dense blocks do not | Controlled synthetic TIFFs | Single-frame spatial candidate implemented with visible diagnostics | Run the same fixture set through both apps |
| No reliable base | NegBase has a fallback, but its exact refusal/fallback boundary is unknown | Limited black-box observation | OpenRevelare reports content inference explicitly | Build no-border and partial-border fixture matrix |
| Roll analysis | Exact aggregation across frames is unknown | Not yet isolated | Existing confidence-weighted roll estimator retained | Repeat one controlled frame across a synthetic roll and vary one frame at a time |
| Scopes | RGB parade and vectorscope are pre-display tools | UI observation | YRGB, skin-tone vectorscope, 0–1023 scale, enlarged window implemented | Compare values using the same synthetic ramps |

The 2026-10-11 seven-frame RAF checkpoint established that the active NegBase automatic workflow
used Cineon target 685. New OpenRevelare frames therefore start at 685; legacy projects without an
explicit target retain 1032 to preserve their saved appearance. It also exposed a false positive
in the first spatial Dmax candidate: a narrow, boundary-attached density-2.8 frame line on 0020 was
chosen as white. Such line-shaped and maximum-density components are now excluded.

The automatic detector and placement now use one stated standard together: it searches for a
broad neutral picture-white region and maps that reference to 685. The 1000 option is retained for
an explicit true-Dmax measurement; it is not silently substituted into the diffuse-white auto
path. Automatic runs also clear prior RGB alignment and display adjustments so the same source
cannot change merely because the user previously adjusted and reset it.

## Rules for parity work

1. Record input colour domain, crop, Dmin, Dmax, target code, per-channel scale and output profile.
2. Do not infer automatic masks from a visually plausible positive.
3. Keep Dmin measurement, Dmax measurement, highlight placement and manual RGB alignment as four
   independent states.
4. Any automatic result must expose its evidence source, support and candidate region.
5. Preserve the native OpenRevelare ROI tool; the abandoned OFX overlay is not a reference
   implementation.

## `0023.raf` Dmin checkpoint

The stored camera-native analysis buffer is 971×729. With the inset edge band widened to include
copy-stand rebates, the spatial edge detector selects 6.36% of the frame, with 99.91% of those
pixels inside the edge band, and estimates absolute Dmin as:

```text
automatic edge region: 0.05578 / 0.11644 / 0.56742
manual right rebate:   0.05143 / 0.11222 / 0.56382
absolute difference:   0.00435 / 0.00422 / 0.00360
```

This verifies the geometry fix on the reference frame. It does not establish NegBase's hidden
thresholds, and it does not make a content-inference fallback a physical Dmin measurement.
