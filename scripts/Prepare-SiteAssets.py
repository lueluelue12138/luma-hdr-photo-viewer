"""Derive web-sized homepage images from the project logo and real Luma screenshots. Every picture of the app is a crop of a screenshot;
the one exception, the "Photo information" card, is drawn in HTML (site/index.html, docs/WEBSITE.md).

Inputs (untracked, see docs/WEBSITE.md):
  branding/Luma.png                       tracked logo master
  --previews DIR (default artifacts/promotional-previews):
    hero-sdr.png       SDR screenshot of the hero frame (Prepare-SiteHdr.py reads hero-capture.jxr, the HDR half of the same frame)
    live-sdr.png       viewer window showing a Live Photo, for the Live demo: its toolbar carries the Live icon
    ui-favorites.png   gallery, Favorites page, with HDR and Live badges
    ui-albums.png      gallery, Albums page
Outputs go to site/assets (override with --output); favicon.ico goes next to it. The hero's AVIFs come from Prepare-SiteHdr.py.
"""
from pathlib import Path
import argparse

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, default=root / 'site/assets')
parser.add_argument('--previews', type=Path, default=root / 'artifacts/promotional-previews')
args = parser.parse_args()
out = args.output
out.mkdir(parents=True, exist_ok=True)

LOGO = root / 'branding/Luma.png'
WEBP = {'method': 6}

# Crops are (left, top, right, bottom) in source pixels.
HERO_CROP = 2                                   # same cut as Prepare-SiteHdr.py: the window outline and rounded-corner black
HERO_FALLBACK_WIDTH = 1100                      # WebP for browsers without AVIF
BADGES = ('ui-favorites.png', (1000, 215, 1990, 575), None)         # three gallery cards with HDR / Live badges
ALBUMS = ('ui-albums.png', (0, 0, 2025, 1313), 1300)                # the whole Albums page, reduced
LIVE = ('live-sdr.png', (400, 67, 1440, 847), None)                 # toolbar with the Live icon over the photo; the photo starts at y=67, the icon centre is (1024.5, 127.5)


def save_webp(image, name, quality):
    path = out / name
    image.save(path, 'WEBP', quality=quality, **WEBP)
    return path


def crop(spec):
    name, box, width = spec
    with Image.open(args.previews / name) as source:
        image = source.convert('RGB').crop(box)
    if width:
        image = image.resize((width, round(image.height * width / image.width)), Image.Resampling.LANCZOS)
    return image


with Image.open(args.previews / 'hero-sdr.png') as source:
    hero = source.convert('RGB')
hero = hero.crop((HERO_CROP, HERO_CROP, hero.width - HERO_CROP, hero.height - HERO_CROP))
save_webp(hero.resize((HERO_FALLBACK_WIDTH, round(hero.height * HERO_FALLBACK_WIDTH / hero.width)), Image.Resampling.LANCZOS), 'hero-1100.webp', 72)
for name, spec, quality in (('card-badges.webp', BADGES, 92), ('card-albums.webp', ALBUMS, 90), ('live-demo.webp', LIVE, 90)):
    save_webp(crop(spec), name, quality)

# Logo: crop the transparent master to the visible mark; the icon is 967 x 943 px.
with Image.open(LOGO) as master:
    mark = master.convert('RGBA')
alpha = np.asarray(mark)[..., 3]
ys, xs = np.where(alpha > 8)
mark = mark.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
logo = mark.resize((256, round(mark.height * 256 / mark.width)), Image.Resampling.LANCZOS)
logo.save(out / 'logo.webp', 'WEBP', quality=96, alpha_quality=100, **WEBP)

square = Image.new('RGBA', (max(mark.size),) * 2, (0, 0, 0, 0))
square.paste(mark, ((square.width - mark.width) // 2, (square.height - mark.height) // 2))
flat = Image.new('RGB', square.size, (255, 255, 255))
flat.paste(square, mask=square.getchannel('A'))
flat.resize((180, 180), Image.Resampling.LANCZOS).save(out / 'apple-touch-icon.png', optimize=True)
square.resize((256, 256), Image.Resampling.LANCZOS).save(
    out.parent / 'favicon.ico', sizes=[(16, 16), (32, 32), (48, 48)])


def rounded_mask(size, radius):
    mask = Image.new('L', size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size[0] - 1, size[1] - 1), radius=radius, fill=255)
    return mask


# Social preview, 1200 x 628 (1.91:1): the window beside the app icon on the page's pale gradient.
# Deterministic and font-free: the title and description come from the page's own meta tags.
# Full-colour PNG on purpose: a 256-colour palette bands the gradient.
W, H, SHOT_WIDTH, ICON = 1200, 628, 600, 210
yy, xx = np.mgrid[0:H, 0:W]
t = np.clip((xx / W) * 0.55 + (yy / H) * 0.45, 0, 1)[..., None]
top_left, middle, bottom_right = np.array([203, 232, 255.]), np.array([238, 243, 255.]), np.array([224, 226, 255.])
gradient = np.where(t < 0.5, top_left + (middle - top_left) * (t / 0.5), middle + (bottom_right - middle) * ((t - 0.5) / 0.5))
card = Image.fromarray(np.clip(np.rint(gradient), 0, 255).astype(np.uint8)).convert('RGBA')

shot = hero.resize((SHOT_WIDTH, round(hero.height * SHOT_WIDTH / hero.width)), Image.Resampling.LANCZOS).convert('RGBA')
shot.putalpha(rounded_mask(shot.size, 10))
left, top = W - shot.width - 80, (H - shot.height) // 2
shadow = Image.new('RGBA', card.size, (0, 0, 0, 0))
ImageDraw.Draw(shadow).rounded_rectangle((left, top + 16, left + shot.width, top + shot.height + 16), radius=10, fill=(24, 48, 110, 80))
card = Image.alpha_composite(card, shadow.filter(ImageFilter.GaussianBlur(24)))
card.alpha_composite(shot, (left, top))

icon = logo.resize((ICON, round(logo.height * ICON / logo.width)), Image.Resampling.LANCZOS)
icon_x, icon_y = 110, (H - icon.height) // 2
glow = Image.new('RGBA', card.size, (0, 0, 0, 0))
glow.paste((1, 103, 252, 70), (icon_x + 6, icon_y + 18, icon_x + icon.width - 6, icon_y + icon.height + 6))
card = Image.alpha_composite(card, glow.filter(ImageFilter.GaussianBlur(26)))
card.alpha_composite(icon, (icon_x, icon_y))
card.convert('RGB').save(out / 'og.png', optimize=True)

for path in sorted(out.iterdir()):
    print(f'{path.name:24s}{path.stat().st_size:>9,d} bytes')
print(f'{"favicon.ico":24s}{(out.parent / "favicon.ico").stat().st_size:>9,d} bytes')
