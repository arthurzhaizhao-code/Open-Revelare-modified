<p align="center">
  <img src="docs/assets/logo.png" width="104" alt="OpenRevelare">
</p>

<h1 align="center">OpenRevelare Modified</h1>

<p align="center">
  <img src="src/OpenRevelare.Gui/Assets/branding/contact-sheet-wordmark.png" width="520" alt="OpenRevelare">
</p>

<p align="center"><b>An unofficial macOS-focused colour-negative workflow</b><br>OpenRevelare 1.8.4 with NegBase-parity calibration, independent RGB alignment and professional scopes.</p>

> [!IMPORTANT]
> This is an unofficial modification maintained by `arthurzhaizhao-code`; it is not a release from the original OpenRevelare author.
> Upstream is [Toshihiko-Lin/Open-Revelare](https://github.com/Toshihiko-Lin/Open-Revelare).
> The upstream GPL-3.0-only licence and attribution are preserved. This repository currently publishes macOS packages only.

<p align="center">
  <a href="https://github.com/arthurzhaizhao-code/Open-Revelare-modified/releases/latest"><img alt="macOS" src="https://img.shields.io/badge/macOS-Apple%20Silicon%20%7C%20Intel-111111?style=for-the-badge&logo=apple&logoColor=white"></a>
</p>

<p align="center">
  <a href="https://github.com/arthurzhaizhao-code/Open-Revelare-modified/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/arthurzhaizhao-code/Open-Revelare-modified?display_name=tag&sort=semver"></a>
  <a href="https://github.com/arthurzhaizhao-code/Open-Revelare-modified/actions/workflows/ci.yml"><img alt="CI build status" src="https://github.com/arthurzhaizhao-code/Open-Revelare-modified/actions/workflows/ci.yml/badge.svg?branch=codex%2Fnegbase-parity"></a>
  <a href="LICENSE"><img alt="GNU GPL v3" src="https://img.shields.io/badge/license-GPL--3.0--only-2ea44f.svg"></a>
</p>

<p align="center"><a href="README.md">中文</a> · <a href="#overview">English</a></p>

<p align="center">
  <a href="#differences-from-upstream">Modifications</a> · <a href="#overview">Overview</a> · <a href="#model">Model</a> · <a href="#workflow">Workflow</a> ·
  <a href="#interface-and-examples">Interface</a> · <a href="#features">Features</a> · <a href="#installation">Install</a> ·
  <a href="#building-from-source">Build</a> · <a href="#licence-and-third-party-components">Licence</a>
</p>

---

## Differences from upstream

The current baseline is **1.8.4.1**, based on upstream **1.8.4**.

| Area | This modified edition |
|---|---|
| Auto calibration | Uses Cineon code 685 diffuse white as the fixed default target and improves rejection of film borders, frame lines and opaque rails |
| RGB alignment | Independently adjusts R/G/B in Cineon signal space while keeping the current D_min fixed |
| Scopes | YRGB parade, 10-bit Cineon coordinates, vectorscope and skin-tone indicator, with separate enlarged windows |
| Finishing | Uses the author's display-referred Stage 2 placement and retains its full photographic control response |
| Interaction | Numeric values scrub horizontally at displayed precision with smooth, bounded acceleration |
| Releases | Builds Apple Silicon and Intel macOS DMGs only |

See [CHANGELOG.md](CHANGELOG.md) for the release history and
[docs/NEGBASE_COMPARISON.md](docs/NEGBASE_COMPARISON.md) for the behavioural comparison work.
The project does not claim access to or reproduction of NegBase internals.

## Overview

Colour negatives contain an orange base and dye layers. OpenRevelare linearises the input, removes the base, white-balances and inverts it in the Cineon log-density domain, then exports a positive.

- **Explainable**: base, maximum density and channel-balance parameters have defined meanings.
- **Reproducible**: settings are stored in a `.ncproj` beside the image; source files remain untouched.
- **Local-first**: no account or network connection; processing runs in the shared CPU Core.
- **macOS-first**: C# / .NET 8 + Avalonia, released here for Apple Silicon and Intel Macs.

The application is intended for whole-roll camera-copy and scanning workflows, archival parameters and reproducible reprocessing. HDR editing and export are supported; Linux currently keeps the on-screen preview on an explicitly labelled SDR fallback. It does not provide per-roll colour-chart calibration; use [DiVERE](https://github.com/flipswitchingmonkey/DiVERE) for heritage, commercial-archive or research work that requires it.

## Model

| Stage | Scope | Responsibility |
|---|---|---|
| **FilmBase** | Whole roll | Base transmittance, maximum density, channel balance and inversion parameters |
| **SceneBase** | Single frame | Temperature, exposure, contrast, saturation and curves |

The separation allows physical parameters to be shared across a roll while frame-level edits remain independent.

### Principles

1. **The mask is physical**: measure the base; do not replace measurement with visual guessing.
2. **Density is the computation domain**: the mask is approximately a constant offset, making white balance and inversion predictable.
3. **Restoration and creation are separate**: FilmBase provides consistency; SceneBase provides expression.

## Workflow

1. **Import** RAW, scanner TIFF or another supported image and enter roll metadata.
2. **Calibrate the roll**: estimate the base, white balance, shadow endpoint and `d_max`; correct manually when required.
3. **Apply FilmBase** to the roll or selected frames.
4. **Edit frames** in SceneBase with temperature, exposure, contrast, saturation and curves.
5. **Export** TIFF, JPEG, linear DNG or scene-linear ACEScg TIFF.

Changes are written automatically. The `.ncproj` is placed beside the source when that directory is writable; read-only media use the per-user `rolls` fallback shown below.

## Interface and examples

### Cineon calibration

<p align="center"><img src="docs/assets/editor-filmbase.jpg" width="100%" alt="Cineon roll calibration"></p>
<p align="center"><sub>Roll thumbnails, the current frame and shared physical parameters on one page.</sub></p>

### Display adjustment

<p align="center"><img src="docs/assets/editor-scenebase.jpg" width="100%" alt="Display frame editing"></p>
<p align="center"><sub>Frame-level creative adjustments without changing FilmBase.</sub></p>

<table>
  <tr>
    <td width="50%"><img src="docs/assets/library.jpg" width="100%" alt="Library"></td>
    <td width="50%"><img src="docs/assets/contactsheet-light.jpg" width="100%" alt="Contact sheet"></td>
  </tr>
  <tr>
    <td align="center"><sub>Roll management, filtering and project recovery</sub></td>
    <td align="center"><sub>Full-roll contact sheet with sprocket layout and metadata</sub></td>
  </tr>
</table>

### Reversal result

<table>
  <tr>
    <td width="33%"><img src="docs/assets/reversal-raw.jpg" width="100%" alt="Camera-copied raw input"></td>
    <td width="33%"><img src="docs/assets/reversal-nlp.jpg" width="100%" alt="NLP automatic reversal result"></td>
    <td width="33%"><img src="docs/assets/reversal-after.jpg" width="100%" alt="OpenRevelare reversal result"></td>
  </tr>
  <tr>
    <td align="center"><sub>Raw input</sub></td>
    <td align="center"><sub>NLP automatic reversal</sub></td>
    <td align="center"><sub>OpenRevelare result</sub></td>
  </tr>
</table>

## Features

| Category | Capabilities |
|---|---|
| Reversal | Cineon density-domain inversion, auto-calibration, narrow-band light decoupling (Path A), black-and-white negatives |
| Colour | ACEScg workspace; sRGB, Display P3 and Adobe RGB output; exact ICC management; Stage 2 controls |
| Pre-processing | LCC flat-field, lens distortion, vignetting, sprocket masking and geometry crop in linear light |
| Workflow | Roll management, roll sync, independent RGB alignment, virtual copies, format presets and 80-step undo/redo |
| Scopes | Histogram, YRGB parade, 10-bit Cineon coordinates, vectorscope, skin-tone indicator and enlarged windows |
| Output | 16-bit TIFF, JPEG, linear DNG, 32-bit float scene-linear ACEScg TIFF and HDR gain-map JPEG; templates, scaling and sharpening |
| Look / HDR | Cineon-input `.cube` print looks, SDR/HDR roll rendering, HDR limit in stops, HDR contact sheets |
| Reports | Per-frame report of input domain, base source and inversion parameters |

Inputs include DNG, NEF, CR2/CR3, ARW, RAF, RW2, ORF, PEF, IIQ, Hasselblad/Flextight `.fff` and TIFF. JPEG/PNG are export or cache formats, not negative-input formats. RAW file-dialog filters accept upper- and lower-case extensions.

## How it works

Transmittance `T` is converted to log density:

$$D = -\log_{10}(T)$$

The core operations are:

$$T_{norm} = T / T_{base}$$

$$D_{corr}[c] = D[c] \times w_{high}[c] + w_{offset}[c]$$

$$T_{pos} = 10^{\left(\frac{R_{out}}{D_{max}[c]}D[c]-R_{out}\right)}$$

The per-frame pipeline is: linearisation → optical correction → optional light-source decoupling → base removal → density-domain white balance → inversion → output. The application includes the complete derivation under **Help → Operation guide / Technical theory**.

## Installation

Modified-edition packages are published on [Releases](https://github.com/arthurzhaizhao-code/Open-Revelare-modified/releases/latest) with the .NET runtime included.

| Platform | Package | Status and requirements |
|---|---|---|
| macOS Apple Silicon | `-arm64.dmg` | macOS 12+; primary test platform |
| macOS Intel | `-x86_64.dmg` | macOS 12+; automated build and bundle verification |

Windows and Linux users should install the [official upstream release](https://github.com/Toshihiko-Lin/Open-Revelare/releases/latest).

<details>
<summary>Platform notes</summary>

- **macOS**: approve the first launch under **System Settings → Privacy & Security → Open Anyway**, or run:

  ```bash
  xattr -dr com.apple.quarantine /Applications/OpenRevelare.app
  ```

</details>

## Data locations

| Content | Windows | Linux / macOS |
|---|---|---|
| Settings and roll index | `%APPDATA%\OpenRevelare` | `$XDG_CONFIG_HOME/OpenRevelare/` (default `~/.config`) |
| Contact-sheet cache | `%LOCALAPPDATA%\OpenRevelare\sheets` | `$XDG_CACHE_HOME/OpenRevelare/sheets/` (default `~/.cache`) |
| DNG decode cache | `.revelare-cache/` beside the source | Same |
| Print LUT drop-in folder | `%LOCALAPPDATA%\OpenRevelare\luts` | `$XDG_DATA_HOME/OpenRevelare/luts/` (default `~/.local/share`) |
| Project file | `.ncproj` beside the source when writable; otherwise `%APPDATA%\OpenRevelare\rolls` | Beside the source when writable; otherwise `$XDG_CONFIG_HOME/OpenRevelare/rolls/` |

Cache locations and limits are configurable. Uninstalling does not remove these data.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/arthurzhaizhao-code/Open-Revelare-modified.git
cd Open-Revelare-modified
git switch codex/negbase-parity
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/OpenRevelare.Gui
```

CLI example:

```bash
dotnet run --project src/OpenRevelare.Cli -- -i neg.tiff -o pos.tiff --input-linear --color-space sRGB
```

`--input-linear` and `--input-srgb` are mutually exclusive input declarations. Legacy `--d-max`,
`--grade`, `--pivot` and `--scan-exposure-ev` are accepted for old parity scripts but are no-ops;
the current inversion is controlled by the two calibrated density endpoints and output range.

Read [CONTRIBUTING.md](CONTRIBUTING.md) before development. Calibration experiments are documented in [`docs/calibration/`](docs/calibration/); colour-management decisions are recorded in [`docs/color-management-architecture.md`](docs/color-management-architecture.md).

## Licence and third-party components

The code is licensed under **GPL-3.0-only**; see [LICENSE](LICENSE).

`models/net_awb.onnx` is the Deep White-Balance Editing model, distributed under **CC BY-NC-SA 4.0** and outside this project's GPL scope. See [models/README.md](models/README.md) and [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).

References include [LightSourceDecouple](https://github.com/karasuyasabou/LightSourceDecouple), [DiVERE](https://github.com/flipswitchingmonkey/DiVERE) and darktable `negadoctor`.

## Project origin and acknowledgements

This repository is derived from [Toshihiko-Lin/Open-Revelare](https://github.com/Toshihiko-Lin/Open-Revelare), created by Toshihiko-Lin and released under GPL-3.0-only. The modified-edition maintainer thanks the original author and upstream contributors for the complete application foundation.

The following names are retained from upstream in recognition of their support for its early development:

- 豆腐
- Caramello_焦糖玛奇朵
- REPEATER000
- jamais
- hhd

## Limitations and feedback

- No per-roll colour-chart calibration.
- 8-bit TIFF may show banding in shadows; 16-bit input is recommended.
- This modified edition does not publish Windows or Linux installers.
- Intel packages pass automated build and bundle verification; primary interaction testing is on Apple Silicon.

Report modified-edition issues in [this repository](https://github.com/arthurzhaizhao-code/Open-Revelare-modified/issues). Use the [upstream issue tracker](https://github.com/Toshihiko-Lin/Open-Revelare/issues) only for issues confirmed to apply upstream. Include the macOS version, Mac model, camera or scanner, input format and error details. Do not upload private originals.
