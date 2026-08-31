from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 1024
SCALE = SIZE / 256
BACKGROUND = "#5d7188"
FOREGROUND = "#f8fafc"


def scaled(value: int) -> int:
    return round(value * SCALE)


image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle(
    (0, 0, SIZE - 1, SIZE - 1),
    radius=scaled(56),
    fill=BACKGROUND,
)
draw.ellipse(
    (scaled(58), scaled(67), scaled(198), scaled(207)),
    fill=FOREGROUND,
)
draw.line(
    (scaled(105), scaled(43), scaled(151), scaled(43)),
    fill=FOREGROUND,
    width=scaled(16),
)
draw.line(
    (scaled(128), scaled(67), scaled(128), scaled(48)),
    fill=FOREGROUND,
    width=scaled(14),
)
draw.line(
    (scaled(183), scaled(87), scaled(196), scaled(74)),
    fill=FOREGROUND,
    width=scaled(13),
)
draw.line(
    (scaled(128), scaled(137), scaled(128), scaled(96)),
    fill=BACKGROUND,
    width=scaled(13),
)
draw.line(
    (scaled(128), scaled(137), scaled(163), scaled(159)),
    fill=BACKGROUND,
    width=scaled(13),
)
draw.ellipse(
    (scaled(120), scaled(129), scaled(136), scaled(145)),
    fill=BACKGROUND,
)

output = Path(__file__).resolve().parents[1] / "src" / "GameTimeRecord.App" / "Assets" / "app.ico"
icon = image.resize((256, 256), Image.Resampling.LANCZOS)
icon.save(output, format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(output)
