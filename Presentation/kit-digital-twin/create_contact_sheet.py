from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent
IMAGE_DIR = ROOT / "images"
OUTPUT = IMAGE_DIR / "all-kits-assembled-overview.png"

KITS = [
    ("kit-1-assembled-colored.png", "KIT 1", "SORTING", "SIMULATION + ASSEMBLY"),
    ("kit-2-assembled-colored.png", "KIT 2", "STAMPING", "SIMULATION + ASSEMBLY"),
    ("kit-3-assembled-colored.png", "KIT 3", "STAMPING + COLOR SORTING", "SIMULATION + ASSEMBLY"),
    ("kit-4-assembled-colored.png", "KIT 4", "INSPECTION", "ASSEMBLY ONLY"),
    ("kit-5-assembled-colored.png", "KIT 5", "MULTI-PART ASSEMBLY", "ASSEMBLY ONLY"),
]

WIDTH, HEIGHT = 1920, 1080
BG = "#F5F7FA"
CARD_BG = "#FFFFFF"
INK = "#152235"
MUTED = "#64748B"
ACCENTS = ["#2563EB", "#7C3AED", "#DB2777", "#EA580C", "#16A34A"]


def crop_white(image: Image.Image) -> Image.Image:
    rgb = image.convert("RGB")
    background = Image.new("RGB", rgb.size, "white")
    difference = ImageChops.difference(rgb, background).convert("L")
    difference = difference.point(lambda p: 255 if p > 12 else 0)
    bounds = difference.getbbox()
    return rgb.crop(bounds) if bounds else rgb


def fit(image: Image.Image, box: tuple[int, int]) -> Image.Image:
    copy = image.copy()
    copy.thumbnail(box, Image.Resampling.LANCZOS)
    return copy


def main() -> None:
    canvas = Image.new("RGB", (WIDTH, HEIGHT), BG)
    draw = ImageDraw.Draw(canvas)
    title_font = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 48)
    kit_font = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 28)
    name_font = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 21)
    note_font = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 16)

    draw.text((70, 35), "MODULAR AUTOMATION KITS — ASSEMBLED CAD OVERVIEW", fill=INK, font=title_font)
    draw.text(
        (72, 96),
        "Colored segmentation distinguishes CAD components; it does not represent manufacturing colors.",
        fill=MUTED,
        font=name_font,
    )

    card_w, card_h = 560, 390
    positions = [(70, 150), (680, 150), (1290, 150), (375, 590), (985, 590)]

    for index, ((filename, kit, name, scope), (x, y)) in enumerate(zip(KITS, positions)):
        draw.rounded_rectangle((x, y, x + card_w, y + card_h), radius=22, fill=CARD_BG, outline="#D9E1EA", width=2)
        draw.rounded_rectangle((x, y, x + 12, y + card_h), radius=6, fill=ACCENTS[index])

        source = crop_white(Image.open(IMAGE_DIR / filename))
        rendered = fit(source, (card_w - 58, 255))
        image_x = x + (card_w - rendered.width) // 2 + 5
        image_y = y + 18 + (255 - rendered.height) // 2
        canvas.paste(rendered, (image_x, image_y))

        text_y = y + 282
        draw.text((x + 34, text_y), kit, fill=ACCENTS[index], font=kit_font)
        draw.text((x + 34, text_y + 38), name, fill=INK, font=name_font)
        scope_box = draw.textbbox((0, 0), scope, font=note_font)
        scope_w = scope_box[2] - scope_box[0]
        draw.rounded_rectangle(
            (x + 34, text_y + 72, x + 55 + scope_w, text_y + 101),
            radius=11,
            fill="#E9EEF5",
        )
        draw.text((x + 44, text_y + 76), scope, fill=MUTED, font=note_font)

    canvas.save(OUTPUT, optimize=True)
    print(OUTPUT)


if __name__ == "__main__":
    main()
