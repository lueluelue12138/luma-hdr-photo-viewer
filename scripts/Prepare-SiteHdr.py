"""Turn a Windows HDR capture of the Luma window, plus the SDR screenshot of the same frame, into the homepage hero image:
one AVIF whose base image is the SDR screenshot and whose gain map (ISO 21496-1) lifts it to the HDR capture.

Inputs (the whole window, interface included - nothing is redrawn in HTML):
  --capture    HDR capture of the window: a Game Bar .jxr (Win+Alt+PrtScn on an HDR display), half-float scRGB where
               1.0 = 80 nits.
  --reference  The PNG Game Bar writes next to it (or any SDR screenshot of the same frame). It becomes the SDR base image.
Outputs (default site/assets): hero-<width>.avif at --width (1834) and at 1100 px, the same picture twice; hero-hdr-badge.webp with --badge.

Both files must show the same pixels. Game Bar's pair does. Two screenshots taken separately seldom do, and then the gain map would
show halos along edges: --register finds the whole-pixel offset between them (a Snipaste and a Game Bar capture of one window differ
by a few pixels at the top and left) and cuts the capture to the reference's frame without resampling it; --register affine also
corrects a small difference of scale (affine ECC on log luminance), which does resample. The edge correlation is printed either way.

The two frames can differ in one place: the status badge in the window's corner reads "SDR · 原图" in an SDR screenshot taken with the
original shown, and "RTX HDR 已生效" in the HDR capture. A gain map cannot turn one label into the other, so --badge X0,Y0,X1,Y1 (reference
pixels, before the crop) names the box of the larger badge: the gain map leaves that box as the SDR picture, and hero-hdr-badge.webp, a
transparent full-frame overlay that holds the capture's own pixels there, is written for site.js to lay over the HDR half only.

Why one file: a browser shows the base image of a gain-map AVIF on an SDR display (or when it cannot apply gain maps) and
applies the gain only where the display has HDR headroom. The page uses this single file for both halves of its
SDR / HDR comparison - the left half is limited to SDR with CSS dynamic-range-limit - so every visitor downloads one image.
A PQ image would instead have to be tone mapped by the browser, and came out about 25 % darker than the page in headless Chromium.

Colour handling: SDR white in the capture is a Windows display setting. It is read from the data - the window's own flat, light
interface sits at SDR white - or given with --sdr-white, and scaled to the 203 nits HDR reference white that libavif treats as
SDR white, so highlights keep their ratio to SDR white. Game Bar's SDR PNG is a tone-mapped copy that lifts mid-tones by up to
~18 %, so the HDR picture is "the capture, but never darker than the PNG": the interface, which sits at SDR white, and the
mid-tones come out the same in both halves, and only highlights brighter than SDR white are added.

Needs: pip install imagecodecs numpy pillow opencv-python-headless, and avifgainmaputil from libavif >= 1.2 (v1.4.2 was used;
build it from source with AVIF_BUILD_APPS=ON and pass the path with --avifgainmaputil or put it on PATH).
"""
from pathlib import Path
import argparse
import shutil
import subprocess
import tempfile

import cv2
import imagecodecs
import numpy as np
from PIL import Image

root = Path(__file__).resolve().parents[1]


def parse_box(text):
    try:
        x0, y0, x1, y1 = (int(v) for v in text.split(','))
    except ValueError:
        raise argparse.ArgumentTypeError('expected X0,Y0,X1,Y1')
    if x0 >= x1 or y0 >= y1:
        raise argparse.ArgumentTypeError('expected X0,Y0,X1,Y1 with X0 < X1 and Y0 < Y1')
    return x0, y0, x1, y1


parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
parser.add_argument('--capture', type=Path, default=root / 'artifacts/promotional-previews/hero-capture.jxr')
parser.add_argument('--reference', type=Path, default=root / 'artifacts/promotional-previews/hero-sdr.png')
parser.add_argument('--output', type=Path, default=root / 'site/assets')
parser.add_argument('--avifgainmaputil', default=shutil.which('avifgainmaputil'))
parser.add_argument('--crop', type=int, default=2, help='pixels to cut from every side: the window outline and rounded-corner black')
parser.add_argument('--sdr-white', type=float, help='SDR white as a scRGB multiple (1.0 = 80 nits); default: read from the data')
parser.add_argument('--register', nargs='?', const='shift', choices=('shift', 'affine'),
                    help='the two captures do not line up pixel for pixel: shift = find the whole-pixel offset, affine = also correct a small scale difference')
parser.add_argument('--width', type=int, default=1834, help='width of the large hero image (the capture is scaled to it)')
parser.add_argument('--badge', type=parse_box, help='X0,Y0,X1,Y1: the larger of the two status badges, in reference pixels before the crop')
parser.add_argument('--quality', type=int, default=85, help='quality of the SDR base image, 0-100')
parser.add_argument('--gain-quality', type=int, default=70, help='quality of the gain map, 0-100')
parser.add_argument('--gain-downscaling', type=int, default=2, help='the gain map is stored at 1/N of the image size')
parser.add_argument('--speed', type=int, default=6, help='encoder speed 0 (slowest) - 10')
args = parser.parse_args()
if not args.avifgainmaputil:
    raise SystemExit('avifgainmaputil not found: pass --avifgainmaputil PATH (see the module docstring).')
args.output.mkdir(parents=True, exist_ok=True)

SMALL_WIDTH = 1100
BADGE_MARGIN, BADGE_RADIUS, BADGE_FEATHER = 3, 10, 2.0   # px: the overlay covers the badge box grown by the margin, with rounded corners and a soft rim
REFERENCE_WHITE_NITS = 203.0         # BT.2408 HDR reference white
LUMA = np.array([0.2126, 0.7152, 0.0722], np.float32)
BT709_TO_BT2020 = np.array([[0.6274039, 0.3292830, 0.0433131],
                            [0.0690973, 0.9195404, 0.0113623],
                            [0.0163914, 0.0880133, 0.8955953]], np.float32)
M1, M2 = 2610 / 16384, 2523 / 4096 * 128
C1, C2, C3 = 3424 / 4096, 2413 / 4096 * 32, 2392 / 4096 * 32


def srgb_to_linear(code):
    c = code / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4).astype(np.float32)


def linear_to_srgb8(linear):
    c = np.clip(linear, 0.0, 1.0)
    return np.rint(np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055) * 255).astype(np.uint8)


def pq_encode(nits):
    p = np.clip(nits / 10000.0, 0.0, 1.0) ** M1
    return ((C1 + C2 * p) / (1 + C3 * p)) ** M2


def smoothstep(low, high, x):
    t = np.clip((x - low) / (high - low), 0.0, 1.0)
    return t * t * (3 - 2 * t)


log_lum = lambda rgb: np.log(np.maximum(rgb @ LUMA, 2e-3)).astype(np.float32)
high_pass = lambda x: x - cv2.GaussianBlur(x, (0, 0), 3)


def edge_correlation(a, b):
    return float(np.corrcoef(high_pass(log_lum(a)).ravel(), high_pass(log_lum(b)).ravel())[0, 1])


def align(capture, reference_linear):
    """Warp the capture onto the reference grid (affine, ECC on log luminance, coarse to fine; the last 1/2-scale pass is good to ~0.1 px)."""
    template, moving = log_lum(reference_linear), log_lum(capture)
    norm = lambda x: ((x - x.mean()) / (x.std() + 1e-9)).astype(np.float32)
    warp = np.eye(2, 3, dtype=np.float32)
    for scale in (8, 4, 2):
        small = lambda x: norm(cv2.resize(x, None, fx=1 / scale, fy=1 / scale, interpolation=cv2.INTER_AREA))
        warp[:, 2] /= scale
        _, warp = cv2.findTransformECC(small(template), small(moving), warp, cv2.MOTION_AFFINE,
                                       (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 300, 1e-7), None, 5)
        warp[:, 2] *= scale
    height, width = template.shape
    aligned = cv2.warpAffine(capture, warp, (width, height), flags=cv2.INTER_CUBIC + cv2.WARP_INVERSE_MAP, borderMode=cv2.BORDER_REPLICATE)
    print(f'aligned: scale {warp[0, 0]:.4f} x {warp[1, 1]:.4f}, shift ({warp[0, 2]:+.1f}, {warp[1, 2]:+.1f}) px')
    return aligned


def shift_align(capture, reference_linear, search=8):
    """Cut the capture to the reference's frame by the whole-pixel offset at which their edges agree best. Both are screen captures at the
    same scale, so only the position of the window can differ; nothing is resampled. Pixels the capture lacks repeat its last row or column."""
    template, moving = high_pass(log_lum(reference_linear)), high_pass(log_lum(capture))
    height, width = template.shape
    margin = 2 * search
    core = template[margin:height - margin, margin:width - margin]
    core = core - core.mean()
    norm = float(np.sqrt((core * core).sum()))
    best = (-2.0, 0, 0)
    for dy in range(-search, search + 1):
        for dx in range(-search, search + 1):
            top, left = margin + dy, margin + dx
            if top < 0 or left < 0 or top + core.shape[0] > moving.shape[0] or left + core.shape[1] > moving.shape[1]:
                continue
            part = moving[top:top + core.shape[0], left:left + core.shape[1]]
            part = part - part.mean()
            score = float((core * part).sum() / (norm * np.sqrt((part * part).sum()) + 1e-12))
            if score > best[0]:
                best = (score, dx, dy)
    _, dx, dy = best
    padded = np.pad(capture, ((search, search), (search, search), (0, 0)), mode='edge')
    aligned = padded[search + dy:search + dy + height, search + dx:search + dx + width]
    if aligned.shape[:2] != (height, width):
        raise SystemExit('The capture is too small for this reference: it lacks more than the search range.')
    print(f'aligned: window offset ({dx:+d}, {dy:+d}) px, nothing resampled')
    return aligned


def rounded_box_alpha(shape, box, radius, feather):
    """1 inside the rounded rectangle `box` (x0, y0, x1, y1), falling to 0 over `feather` pixels outside it."""
    height, width = shape
    x0, y0, x1, y1 = box
    yy, xx = np.mgrid[0:height, 0:width].astype(np.float32)
    qx = np.abs(xx + 0.5 - (x0 + x1) / 2) - ((x1 - x0) / 2 - radius)
    qy = np.abs(yy + 0.5 - (y0 + y1) / 2) - ((y1 - y0) / 2 - radius)
    distance = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - radius
    return np.clip(1 - distance / feather, 0, 1)


def read_sdr_white(capture, reference_linear):
    """Where the HDR layer leaves the SDR picture alone, the ratio of the two is SDR white. The window's own light, flat interface
    (title bar, panels: unclipped and not tone mapped) shows it best; without one, use the dark-to-mid photo pixels. Either way take
    the peak of the ratio histogram."""
    sdr = reference_linear @ LUMA
    mean = cv2.blur(sdr, (5, 5))
    spread = cv2.blur(sdr * sdr, (5, 5)) - mean * mean
    mask = (sdr > 0.5) & (sdr < 0.97) & (spread < 1e-5)
    if mask.sum() < 5000:
        mask = (sdr > 0.06) & (sdr < 0.25)
    ratio = (capture @ LUMA)[mask] / sdr[mask]
    low, high = np.percentile(ratio, [3, 97])
    counts, edges = np.histogram(ratio, bins=300, range=(low, high))
    peak = int(np.argmax(counts))
    return float((edges[peak] + edges[peak + 1]) / 2)


def hdr_target(reference_linear, relative):
    """The HDR picture in SDR-white units: the SDR screenshot, plus whatever the capture adds above it. Differences under 5 % of SDR
    white (noise, 8-bit rounding) are dropped, those over 12 % are kept whole, with a smooth step between."""
    excess = relative - reference_linear
    return reference_linear + excess * smoothstep(0.05, 0.12, excess)


capture = imagecodecs.jpegxr_decode(args.capture.read_bytes())
if capture.dtype != np.float16 or capture.shape[2] < 3:
    raise SystemExit(f'Expected a half-float RGB(A) HDR capture, got {capture.dtype} {capture.shape}.')
capture = capture[..., :3].astype(np.float32)
with Image.open(args.reference) as opened:
    reference = opened.convert('RGB')
if reference.size != (capture.shape[1], capture.shape[0]) and not args.register:
    raise SystemExit(f'Capture is {capture.shape[1]}x{capture.shape[0]} but the reference is {reference.width}x{reference.height}: pass --register.')
reference_linear = srgb_to_linear(np.asarray(reference, np.float32))

if args.register:
    capture = (align if args.register == 'affine' else shift_align)(capture, reference_linear)
    print(f'edge correlation after alignment {edge_correlation(reference_linear, capture):.3f}')
else:
    before = edge_correlation(reference_linear, capture)
    print(f'edge correlation at zero shift {before:.3f}' + ('' if before > 0.95 else '  WARNING: the captures do not line up, try --register'))

white = args.sdr_white or read_sdr_white(capture, reference_linear)
relative = capture / white           # 1.0 = SDR white
target = hdr_target(reference_linear, relative)
print(f'SDR white {white:.3f} x 80 = {white * 80:.0f} nits; the capture peaks at {relative.max():.2f}x SDR white;'
      f' {100 * (relative.max(axis=2) > 1).mean():.2f}% of pixels have a channel above SDR white;'
      f' the HDR picture differs from the SDR one in {100 * (target.max(axis=2) > reference_linear.max(axis=2) * 1.05).mean():.2f}% of them')

overlay = None
if args.badge:
    x0, y0, x1, y1 = args.badge
    core = (x0 - BADGE_MARGIN, y0 - BADGE_MARGIN, x1 + BADGE_MARGIN, y1 + BADGE_MARGIN)
    target[core[1]:core[3], core[0]:core[2]] = reference_linear[core[1]:core[3], core[0]:core[2]]     # the gain map stays out of the box; the overlay covers it
    identity = linear_to_srgb8(relative)                        # the capture as an SDR picture: SDR white = 255, anything brighter clips
    alpha = rounded_box_alpha(identity.shape[:2], core, BADGE_RADIUS + BADGE_MARGIN, BADGE_FEATHER)
    rgba = np.dstack([identity, np.rint(alpha * 255).astype(np.uint8)])
    rgba[alpha == 0, :3] = 0
    rim = (alpha > 0) & (alpha < 1)
    seam = np.abs(identity[rim].astype(int) - linear_to_srgb8(target)[rim].astype(int))
    print(f'badge overlay: box {core}, rim {int(rim.sum())} px; against the HDR layer the rim differs by {seam.mean():.1f} codes on average, {seam.max()} at most')
    overlay = Image.fromarray(rgba)

# Crop last, so that the alignment and SDR white see the whole frame.
delta = target - reference_linear            # what the HDR picture adds to the SDR one, in SDR-white units
crop = args.crop
if crop:
    box = (crop, crop, reference.width - crop, reference.height - crop)
    reference = reference.crop(box)
    target = target[crop:-crop, crop:-crop]
    delta = delta[crop:-crop, crop:-crop]
    if overlay is not None:
        overlay = overlay.crop(box)

with tempfile.TemporaryDirectory() as tmp:
    tmp = Path(tmp)
    for width in (args.width, SMALL_WIDTH):
        size = (width, round(reference.height * width / reference.width))
        base = reference.resize(size, Image.Resampling.LANCZOS)
        base.save(tmp / f'base-{width}.png')
        # The HDR layer is the resized base plus the resized difference, so it equals the base exactly wherever the picture adds nothing;
        # resizing the SDR and HDR pictures separately would leave faint gains along sharp edges such as text.
        layer = srgb_to_linear(np.asarray(base, np.float32)) + cv2.resize(delta, size, interpolation=cv2.INTER_AREA)     # averaged in linear light
        nits = np.maximum(layer @ BT709_TO_BT2020.T, 0.0) * REFERENCE_WHITE_NITS
        alternate = np.rint(pq_encode(nits) * 65535).astype(np.uint16)
        (tmp / f'alt-{width}.png').write_bytes(bytes(imagecodecs.png_encode(
            alternate, primaries='BT2020', transfer='PQ', matrix='IDENTITY')))
        out = args.output / f'hero-{width}.avif'
        subprocess.run([
            args.avifgainmaputil, 'combine', str(tmp / f'base-{width}.png'), str(tmp / f'alt-{width}.png'), str(out),
            '--cicp-base', '1/13/6', '--cicp-alternate', '9/16/9', '-q', str(args.quality),
            '--qgain-map', str(args.gain_quality), '--downscaling', str(args.gain_downscaling), '-s', str(args.speed)],
            check=True, capture_output=True)
        print(f'{out.name}: {size[0]}x{size[1]}, {out.stat().st_size / 1024:.0f} KiB')
        if width == args.width:
            print(subprocess.run([args.avifgainmaputil, 'printmetadata', str(out)], check=True, capture_output=True, text=True).stdout.strip())

if overlay is not None:
    size = (args.width, round(overlay.height * args.width / overlay.width))
    path = args.output / 'hero-hdr-badge.webp'
    overlay.resize(size, Image.Resampling.LANCZOS).save(path, 'WEBP', lossless=True, method=6)
    print(f'{path.name}: {size[0]}x{size[1]}, {path.stat().st_size / 1024:.1f} KiB')
