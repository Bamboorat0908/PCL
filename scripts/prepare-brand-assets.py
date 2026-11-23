"""Package the approved brand artwork into the launcher's embedded image assets.

The full-resolution originals remain in Images/Brand (and Images/BrsMachinist.png);
only the sizes the UI can actually present are embedded in BRCL.exe.

Size budget (largest on-screen use x 2.5 DPI, rounded up to a standard size):
  LauncherLogo.png   96 DIP (FormMain about card)            -> 320 px
  BrsServerLogo.png  46 DIP (PageBRS header)                 -> 160 px
  LauncherSplash.png SplashScreen, shown at native pixels    -> 128 px
  icon.ico           unchanged, see ICON_NOTE
  BrsMachinist.jpg   home hero, decoded at <= 1672 px wide    -> 1672 x 941

Each raster is cut from the master at exactly one size. Alpha images are then
palette-quantized with a deterministic, alpha-premultiplied k-means. A result
is accepted only when, composited on the light, neutral and dark launcher
backgrounds and scaled to every on-screen size up to 2.5x DPI, it stays at or
above MIN_PSNR against the previously shipped asset (the 512/256 px cut);
otherwise the build fails. The JPEG is accepted on SSIM against the master.

Requires Pillow and NumPy. Run from any directory:
    python scripts/prepare-brand-assets.py [--check]
--check verifies that the committed assets equal a fresh build (no writes).
"""
from __future__ import annotations

import argparse
import io
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
IMAGES = ROOT / 'Plain Craft Launcher 2' / 'Images'
BRAND = IMAGES / 'Brand'

MIN_PSNR = 40.0          # dB, composited, per background
MIN_SSIM = 0.99          # luma SSIM against the master photograph
JPEG_QUALITY = 90        # 4:4:4, progressive, optimized Huffman tables
BACKGROUNDS = ((255, 255, 255), (231, 232, 227), (17, 20, 22))
DPI_SCALES = (1.0, 1.25, 1.5, 1.75, 2.0, 2.5)
ICON_NOTE = '''icon.ico is not regenerated. Indexed-PNG frames are silently dropped by
WPF's IconBitmapDecoder (window icon), and 32-bit frames rebuilt from quantized
pixels save only ~17% at 44 dB, so the pixel-exact shell icon is kept.'''


# ----------------------------------------------------------------- metrics
def _composite(rgba: np.ndarray, background) -> np.ndarray:
    alpha = rgba[..., 3:4] / 255.0
    return rgba[..., :3] * alpha + np.asarray(background, dtype=np.float64) * (1 - alpha)


def composited_psnr(reference: Image.Image, candidate: Image.Image) -> float:
    a = np.asarray(reference.convert('RGBA'), dtype=np.float64)
    b = np.asarray(candidate.convert('RGBA'), dtype=np.float64)
    worst = 99.0
    for background in BACKGROUNDS:
        mse = float(((_composite(a, background) - _composite(b, background)) ** 2).mean())
        worst = min(worst, 99.0 if mse == 0 else 10 * np.log10(255 ** 2 / mse))
    return worst


def _box(x: np.ndarray, k: int = 8) -> np.ndarray:
    c = np.cumsum(np.cumsum(np.pad(x, ((1, 0), (1, 0))), 0), 1)
    return (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k]) / (k * k)


def luma_ssim(reference: Image.Image, candidate: Image.Image) -> float:
    def luma(img):
        a = np.asarray(img.convert('RGB'), dtype=np.float64)
        return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114
    x, y = luma(reference), luma(candidate)
    c1, c2 = (0.01 * 255) ** 2, (0.03 * 255) ** 2
    mx, my = _box(x), _box(y)
    sxx, syy, sxy = _box(x * x) - mx * mx, _box(y * y) - my * my, _box(x * y) - mx * my
    return float((((2 * mx * my + c1) * (2 * sxy + c2)) / ((mx * mx + my * my + c1) * (sxx + syy + c2))).mean())


def rendered_psnr(reference: Image.Image, candidate: Image.Image, display_dips) -> float:
    """Worst composited PSNR when both images are scaled to each on-screen pixel size."""
    reference, candidate = reference.convert('RGBA'), candidate.convert('RGBA')
    worst = composited_psnr(reference, candidate) if reference.size == candidate.size else 99.0
    for dip in display_dips:
        for scale in DPI_SCALES:
            px = max(1, round(dip * scale))
            worst = min(worst, composited_psnr(reference.resize((px, px), Image.Resampling.LANCZOS),
                                               candidate.resize((px, px), Image.Resampling.LANCZOS)))
    return worst


# ----------------------------------------------------------- quantization
def _nearest(points: np.ndarray, centres: np.ndarray) -> np.ndarray:
    labels = np.empty(len(points), dtype=np.int64)
    for start in range(0, len(points), 8192):
        chunk = points[start:start + 8192]
        labels[start:start + 8192] = ((chunk[:, None, :] - centres[None]) ** 2).sum(2).argmin(1)
    return labels


def quantize(image: Image.Image, colours: int = 256, iterations: int = 16) -> Image.Image:
    """Deterministic k-means palette in alpha-premultiplied RGBA space."""
    rgba = np.asarray(image.convert('RGBA'), dtype=np.float64).reshape(-1, 4)
    points = np.concatenate([rgba[:, :3] * rgba[:, 3:] / 255.0, rgba[:, 3:]], 1)
    unique, inverse, counts = np.unique(points, axis=0, return_inverse=True, return_counts=True)
    inverse = inverse.reshape(-1)
    if len(unique) <= colours:
        centres = unique.copy()
    else:
        rng = np.random.default_rng(20261123)
        centres = [unique[rng.choice(len(unique), p=counts / counts.sum())]]
        distance = ((unique - centres[0]) ** 2).sum(1)
        for _ in range(colours - 1):
            weight = distance * counts
            centres.append(unique[rng.choice(len(unique), p=weight / weight.sum())])
            distance = np.minimum(distance, ((unique - centres[-1]) ** 2).sum(1))
        centres = np.asarray(centres)
        for _ in range(iterations):
            labels = _nearest(unique, centres)
            total = np.bincount(labels, weights=counts, minlength=len(centres))
            for channel in range(4):
                sums = np.bincount(labels, weights=unique[:, channel] * counts, minlength=len(centres))
                centres[:, channel] = np.where(total > 0, sums / np.maximum(total, 1e-9), centres[:, channel])
    labels = _nearest(unique, centres)
    alpha = np.clip(np.round(centres[:, 3]), 0, 255)
    colour = np.where(alpha[:, None] > 0, centres[:, :3] * 255.0 / np.maximum(alpha[:, None], 1), 0)
    palette = np.clip(np.round(np.concatenate([colour, alpha[:, None]], 1)), 0, 255).astype(np.uint8)
    pixels = palette[labels[inverse]].reshape(image.height, image.width, 4)
    return Image.fromarray(pixels, 'RGBA')


# ----------------------------------------------------------------- encoders
def png_bytes(image: Image.Image, palette: bool) -> bytes:
    """Encode losslessly (smallest of the zlib strategies Pillow offers).
    With palette=True the (already quantized) image is stored indexed."""
    if palette:
        rgba = np.asarray(image.convert('RGBA'))
        table, index = np.unique(rgba.reshape(-1, 4), axis=0, return_inverse=True)
        if len(table) > 256:
            raise ValueError('palette encoding needs a quantized image')
        target = Image.fromarray(index.reshape(image.height, image.width).astype(np.uint8), 'P')
        target.putpalette(table[:, :3].reshape(-1).tolist())
        extra = {'transparency': bytes(table[:, 3].tolist())}
    else:
        target, extra = image.convert('RGBA'), {}
    best = None
    for options in ({}, {'optimize': True}, {'compress_level': 9}, {'compress_level': 9, 'compress_type': 1}):
        buffer = io.BytesIO()
        target.save(buffer, 'PNG', **options, **extra)
        if best is None or len(buffer.getvalue()) < len(best):
            best = buffer.getvalue()
    decoded = np.asarray(Image.open(io.BytesIO(best)).convert('RGBA'))
    if not np.array_equal(decoded, np.asarray(image.convert('RGBA'))):
        raise AssertionError('PNG encoding is not lossless')
    return best


def jpeg_bytes(image: Image.Image) -> bytes:
    buffer = io.BytesIO()
    image.convert('RGB').save(buffer, 'JPEG', quality=JPEG_QUALITY, subsampling=0, optimize=True, progressive=True)
    return buffer.getvalue()


# -------------------------------------------------------------------- build
def alpha_png(master: Image.Image, size: int, shipped: int, display_dips, report: dict, name: str) -> bytes:
    """Cut `size` px from the master, quantize, and gate it against the former `shipped` px asset."""
    cut = master.resize((size, size), Image.Resampling.LANCZOS)
    previous = master.resize((shipped, shipped), Image.Resampling.LANCZOS)
    candidate = quantize(cut)
    score = rendered_psnr(previous, candidate, display_dips)
    if score < MIN_PSNR:
        raise ValueError(f'{name}: {score:.2f} dB is below {MIN_PSNR} dB')
    data = png_bytes(candidate, palette=True)
    report[name] = {'size': [size, size], 'bytes': len(data), 'previous_size': [shipped, shipped],
                    'worst_rendered_psnr_db': round(score, 2),
                    'quantization_psnr_db': round(composited_psnr(cut, candidate), 2)}
    return data


def build() -> tuple[dict[str, bytes], dict]:
    report: dict = {}
    outputs: dict[str, bytes] = {}
    launcher = Image.open(BRAND / 'LauncherSelected.png').convert('RGBA')
    server = Image.open(BRAND / 'ServerRounded.png').convert('RGBA')

    # Uses: FormMain 42/96 DIP, about card 72 DIP, message boxes 32 DIP, error window icon.
    outputs['LauncherLogo.png'] = alpha_png(launcher, 320, 512, (32, 42, 48, 72, 96), report, 'LauncherLogo.png')
    # SplashScreen paints the bitmap 1:1, so it keeps its pixel size.
    outputs['LauncherSplash.png'] = alpha_png(launcher, 128, 128, (), report, 'LauncherSplash.png')
    outputs['BrsServerLogo.png'] = alpha_png(server, 160, 256, (46,), report, 'BrsServerLogo.png')

    # icon.ico is intentionally left as committed (see ICON_NOTE).

    master = Image.open(IMAGES / 'BrsMachinist.png').convert('RGB')
    if master.size != (1672, 941):
        raise ValueError('PageLaunchRight.HeroImageWidth/Height must match the hero master')
    hero = jpeg_bytes(master)
    decoded = Image.open(io.BytesIO(hero))
    ssim = luma_ssim(master, decoded)
    if ssim < MIN_SSIM:
        raise ValueError(f'BrsMachinist.jpg SSIM {ssim:.4f} is below {MIN_SSIM}')
    outputs['BrsMachinist.jpg'] = hero
    report['BrsMachinist.jpg'] = {'size': master.size, 'bytes': len(hero), 'quality': JPEG_QUALITY,
                                  'ssim': round(ssim, 4), 'psnr_db': round(composited_psnr(master, decoded), 2)}
    return outputs, report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--report')
    args = parser.parse_args()
    outputs, report = build()
    stale = [name for name, data in outputs.items() if (IMAGES / name).read_bytes() != data]
    if args.report:
        Path(args.report).write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    if args.check:
        if stale:
            print('Stale brand assets: ' + ', '.join(stale))
            return 1
        print('Brand assets are up to date.')
        return 0
    for name, data in outputs.items():
        (IMAGES / name).write_bytes(data)
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0


if __name__ == '__main__':
    sys.exit(main())
