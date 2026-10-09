"""Generate small, deterministic linear-negative fixtures for NegBase black-box tests."""

from pathlib import Path

import numpy as np
from PIL import Image


WIDTH, HEIGHT = 1024, 768
BASE = np.array([230.0, 150.0, 80.0], dtype=np.float64)
PROFILE = Path("/System/Library/ColorSync/Profiles/ACESCG Linear.icc")


def transmission(density: np.ndarray | float) -> np.ndarray:
    return BASE * np.power(10.0, -np.asarray(density)[..., None])


def fixture(diffuse: bool, specular: bool) -> np.ndarray:
    image = np.broadcast_to(BASE, (HEIGHT, WIDTH, 3)).copy()
    border = 80
    yy, xx = np.mgrid[0 : HEIGHT - border * 2, 0 : WIDTH - border * 2]
    density = 0.15 + 0.50 * (xx / max(1, xx.shape[1] - 1))
    picture = transmission(density)
    # Deliberately coloured scene content. Endpoint detection must not neutralise these regions.
    picture[..., 0] *= 0.85 + 0.15 * np.sin(yy / 37.0)
    picture[..., 1] *= 0.90 + 0.10 * np.cos(xx / 53.0)
    picture[..., 2] *= 0.80 + 0.20 * np.sin((xx + yy) / 71.0)
    image[border:-border, border:-border] = picture

    if diffuse:
        image[260:420, 390:550] = transmission(0.80)
    if specular:
        image[325:357, 594:626] = transmission(1.20)
    return np.clip(np.rint(image), 1, 255).astype(np.uint8)


def main() -> None:
    out = Path(__file__).resolve().parent / "negbase-fixtures"
    out.mkdir(parents=True, exist_ok=True)
    icc = PROFILE.read_bytes()
    cases = {
        "01_diffuse_and_specular.tif": (True, True),
        "02_diffuse_only.tif": (True, False),
        "03_specular_only.tif": (False, True),
        "04_no_reference.tif": (False, False),
    }
    for name, flags in cases.items():
        Image.fromarray(fixture(*flags), "RGB").save(
            out / name, format="TIFF", compression="raw", icc_profile=icc
        )


if __name__ == "__main__":
    main()
