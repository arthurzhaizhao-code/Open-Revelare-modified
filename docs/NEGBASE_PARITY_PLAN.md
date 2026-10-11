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

## Rules for parity work

1. Record input colour domain, crop, Dmin, Dmax, target code, per-channel scale and output profile.
2. Do not infer automatic masks from a visually plausible positive.
3. Keep Dmin measurement, Dmax measurement, highlight placement and manual RGB alignment as four
   independent states.
4. Any automatic result must expose its evidence source, support and candidate region.
5. Preserve the native OpenRevelare ROI tool; the abandoned OFX overlay is not a reference
   implementation.
