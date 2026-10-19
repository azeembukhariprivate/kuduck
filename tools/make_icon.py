"""Generates the KUDUCK icon (.ico) and logo PNG from the duck shape.

Run:  python tools/make_icon.py
Needs: pip install pillow
"""
from pathlib import Path
from PIL import Image, ImageDraw

YELLOW = (247, 197, 49, 255)
INK = (23, 24, 28, 255)
BEAK = (238, 122, 43, 255)

# Duck outline on a 64x64 grid (same shape used in the app's XAML).
BODY = [("M", (8, 30)), ("L", (18, 37)), ("L", (28, 37)), ("Q", (32, 41), (40, 41)),
        ("L", (56, 41)), ("Q", (54, 54), (34, 54)), ("Q", (16, 54), (13, 44)),
        ("Q", (10, 38), (8, 30))]
BEAK_PTS = [(48, 26), (58, 29), (48, 32)]


def flatten(cmds, steps=24):
    pts, cur = [], None
    for c in cmds:
        if c[0] in ("M", "L"):
            cur = c[1]
            pts.append(cur)
        elif c[0] == "Q":
            (cx, cy), (ex, ey) = c[1], c[2]
            sx, sy = cur
            for i in range(1, steps + 1):
                t = i / steps
                x = (1 - t) ** 2 * sx + 2 * (1 - t) * t * cx + t * t * ex
                y = (1 - t) ** 2 * sy + 2 * (1 - t) * t * cy + t * t * ey
                pts.append((x, y))
            cur = (ex, ey)
    return pts


def render(size, square=True):
    s = 8  # supersample
    big = size * s
    k = big / 64.0
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    duck = INK if square else YELLOW
    eye = YELLOW if square else INK
    if square:
        d.rounded_rectangle([0, 0, big - 1, big - 1], radius=int(16 * k), fill=YELLOW)
    d.polygon([(x * k, y * k) for x, y in flatten(BODY)], fill=duck)
    d.ellipse([(38 - 11) * k, (29 - 11) * k, (38 + 11) * k, (29 + 11) * k], fill=duck)
    d.polygon([(x * k, y * k) for x, y in BEAK_PTS], fill=BEAK)
    if size >= 24:
        d.ellipse([(41 - 2.2) * k, (26 - 2.2) * k, (41 + 2.2) * k, (26 + 2.2) * k], fill=eye)
    return img.resize((size, size), Image.LANCZOS)


def main():
    root = Path(__file__).resolve().parent.parent
    assets = root / "src" / "Kuduck" / "Assets"
    assets.mkdir(parents=True, exist_ok=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    base = render(256)
    base.save(assets / "kuduck.ico", sizes=[(s, s) for s in sizes],
              append_images=[render(s) for s in sizes if s != 256])
    render(256).save(assets / "kuduck-256.png")
    (root / "docs").mkdir(exist_ok=True)
    render(512).save(root / "docs" / "logo.png")
    print("icons written")


if __name__ == "__main__":
    main()
