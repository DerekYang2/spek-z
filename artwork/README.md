# Spek-Z artwork

`spek-z.svg` is the master for all exports. It's based on the Spek icon by Olga Vasylevska, from
[Spek](https://github.com/alexkay/spek), and is licensed under GPL-3.0 like the rest of Spek-Z. The pig is
unchanged; Spek-Z enlarges it and adds a magnifying glass with a spectrogram.

## Regenerate

From the repository root:

```powershell
python -m pip install -r artwork/requirements.txt
python artwork/export.py
```

Open `artwork/preview.html` to check the icon sizes on light and dark backgrounds. Python is only needed to
regenerate artwork, not to build or run Spek-Z.

## Exports

- `src/SpekZ/Assets/WindowIcon.ico` is the executable, window and taskbar icon.
- `src/SpekZ/Assets/Square44x44Logo.targetsize-48_altform-unplated.png` is the title bar icon.
- The other images in `src/SpekZ/Assets` are the MSIX package logos for a Store build: scale-qualified PNGs for
  100% to 400%, and target-size PNGs from 16 to 256 pixels.
- `logo.png` is a transparent 1024-pixel square for general use.
- `store-icon-300.png` and `store-icon-150.png` are Store listing icons. Use the size Partner Center asks for.
