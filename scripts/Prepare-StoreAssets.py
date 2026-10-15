"""Resize the existing transparent project logo for the Store app tile."""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1]
source = root / 'branding/Luma.png'
output = root / 'artifacts/store-assets'
output.mkdir(parents=True, exist_ok=True)
with Image.open(source) as master:
    assert master.mode == 'RGBA' and master.width == master.height
    tile = master.resize((300, 300), Image.Resampling.LANCZOS)
    path = output / 'Luma-StoreTile-300.png'
    tile.save(path, optimize=True, compress_level=9)
with Image.open(path) as check:
    assert check.size == (300, 300) and check.mode == 'RGBA'
    assert check.tobytes() == tile.tobytes()
print(f'Prepared transparent 300x300 Store tile: {path}; no screenshots generated or uploaded.')
