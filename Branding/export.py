"""Export the Spek-Z icon to Windows assets and repository artwork.

Run from any directory with Python 3.10 or newer:
    python -m pip install -r Branding/requirements.txt
    python Branding/export.py

The app uses the checked-in exports. Python is not a build dependency.
"""

from io import BytesIO
from pathlib import Path

from PIL import Image
import resvg_py


HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
ASSETS = ROOT / "src" / "SpekZ" / "Assets"
SCALES = (100, 125, 150, 200, 250, 300, 400)
TARGETS = (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
RESAMPLE = Image.Resampling.LANCZOS
MASTER_SIZE = 3072
# Transparent margin around the drawing, as a fraction of the square's side.
MASTER_PADDING = 0.02


def load_master():
    data = resvg_py.svg_to_bytes(
        svg_path=str(HERE / "spek-z.svg"), width=MASTER_SIZE, height=MASTER_SIZE
    )
    drawing = Image.open(BytesIO(data)).convert("RGBA")
    # Centre the drawing's visible pixels in a square, so every export has the same padding.
    drawing = drawing.crop(drawing.getchannel("A").getbbox())
    side = round(max(drawing.size) / (1 - 2 * MASTER_PADDING))
    master = Image.new("RGBA", (side, side))
    master.alpha_composite(drawing, ((side - drawing.width) // 2, (side - drawing.height) // 2))
    return master.resize((MASTER_SIZE, MASTER_SIZE), RESAMPLE)


def scaled(value, scale):
    return (value * scale + 50) // 100


def canvas(master, size, icon_size):
    image = Image.new("RGBA", size)
    icon = master.resize((icon_size, icon_size), RESAMPLE)
    image.alpha_composite(icon, ((size[0] - icon_size) // 2, (size[1] - icon_size) // 2))
    return image


def save(image, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, optimize=True)


def windows_assets(master):
    # The MSIX package logos. Tiles get more padding than the unplated taskbar icons.
    families = {
        "Square44x44Logo": ((44, 44), 0.9),
        "Square150x150Logo": ((150, 150), 0.66),
        "SmallTile": ((71, 71), 0.66),
        "LargeTile": ((310, 310), 0.66),
        "Wide310x150Logo": ((310, 150), 0.66),
        "SplashScreen": ((620, 300), 0.4),
        "StoreLogo": ((50, 50), 1.0),
    }
    outputs = []
    for name, (base, fraction) in families.items():
        for scale in SCALES:
            size = tuple(scaled(v, scale) for v in base)
            path = ASSETS / f"{name}.scale-{scale}.png"
            save(canvas(master, size, max(1, round(min(size) * fraction))), path)
            outputs.append(path)

    for target in TARGETS:
        for qualifier, fraction in (("", 0.9), ("_altform-unplated", 1.0), ("_altform-lightunplated", 1.0)):
            path = ASSETS / f"Square44x44Logo.targetsize-{target}{qualifier}.png"
            save(canvas(master, (target, target), max(1, round(target * fraction))), path)
            outputs.append(path)

    # Windows badge images are white alpha masks, not coloured icons.
    alpha = canvas(master, (48, 48), 44).getchannel("A")
    badge = Image.new("RGBA", (48, 48), "white")
    badge.putalpha(alpha)
    path = ASSETS / "LockScreenLogo.scale-200.png"
    save(badge, path)
    outputs.append(path)

    path = ASSETS / "WindowIcon.ico"
    master.save(path, format="ICO", sizes=[(s, s) for s in sorted(set(TARGETS) | {128})])
    outputs.append(path)
    return outputs


def repository_artwork(master):
    outputs = []
    for name, size in (("logo.png", 1024), ("store-icon-300.png", 300), ("store-icon-150.png", 150)):
        path = HERE / name
        save(master.resize((size, size), RESAMPLE), path)
        outputs.append(path)
    return outputs


def preview():
    cards = []
    for theme, color in (("light", "#F4F4F4"), ("dark", "#1A1A1A")):
        samples = "".join(
            f'<img src="../src/SpekZ/Assets/Square44x44Logo.targetsize-{s}_altform-unplated.png" '
            f'width="{s}" height="{s}" alt="{s} px icon">'
            for s in TARGETS
        )
        cards.append(
            f'<section style="background:{color};color:{"#eee" if theme == "dark" else "#222"}">'
            f"<h2>{theme.capitalize()} background</h2>"
            f'<img src="logo.png" width="256" alt="Spek-Z icon">'
            f'<div class="sizes">{samples}</div></section>'
        )
    (HERE / "preview.html").write_text(
        '<!doctype html><html lang="en"><meta charset="utf-8"><title>Spek-Z branding</title>'
        "<style>body{font-family:Segoe UI,sans-serif;background:#29292d;margin:24px}"
        "section{padding:24px;margin-bottom:16px;border-radius:12px}"
        "h2{font-size:18px}.sizes{display:flex;align-items:center;gap:20px;margin-top:20px;flex-wrap:wrap}</style>"
        + "".join(cards)
        + "</html>",
        encoding="utf-8",
    )


def main():
    master = load_master()
    outputs = windows_assets(master) + repository_artwork(master)
    preview()
    for path in outputs:
        print(path.relative_to(ROOT))
    print(f"Exported {len(outputs)} images. Preview: Branding/preview.html")


if __name__ == "__main__":
    main()
