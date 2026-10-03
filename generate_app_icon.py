#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Generate a simple music-note app icon for the WinUI project."""
import os

from PIL import Image, ImageDraw, ImageFont

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "GenshinMusicStudio.WinUI", "Assets")
SOURCE_LOGO = os.path.join(BASE, "app_icon_source.png")


def make_icon(size=256):
    if os.path.exists(SOURCE_LOGO):
        source = Image.open(SOURCE_LOGO).convert("RGBA")
        return source.resize((size, size), Image.Resampling.LANCZOS)
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    margin = max(2, size // 16)
    radius = max(8, size // 5)
    draw.rounded_rectangle(
        (margin, margin, size - margin, size - margin),
        radius=radius,
        fill=(37, 99, 235, 255),
        outline=(147, 197, 253, 255),
        width=max(1, size // 64),
    )
    font = ImageFont.truetype(r"C:\Windows\Fonts\seguisym.ttf", int(size * 0.62))
    text = "\u266a"
    box = draw.textbbox((0, 0), text, font=font)
    width = box[2] - box[0]
    height = box[3] - box[1]
    draw.text(
        ((size - width) / 2 - box[0], (size - height) / 2 - box[1] - size * 0.02),
        text,
        font=font,
        fill=(255, 255, 255, 255),
    )
    return image


def main():
    os.makedirs(BASE, exist_ok=True)
    icon = make_icon(256)
    icon.save(os.path.join(BASE, "AppIcon.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    icon.save(os.path.join(BASE, "app_icon.png"))
    icon.resize((88, 88), Image.Resampling.LANCZOS).save(os.path.join(BASE, "Square44x44Logo.scale-200.png"))
    icon.resize((300, 300), Image.Resampling.LANCZOS).save(os.path.join(BASE, "Square150x150Logo.scale-200.png"))
    icon.resize((50, 50), Image.Resampling.LANCZOS).save(os.path.join(BASE, "StoreLogo.png"))
    icon.resize((24, 24), Image.Resampling.LANCZOS).save(os.path.join(BASE, "Square44x44Logo.targetsize-24_altform-unplated.png"))
    icon.resize((48, 48), Image.Resampling.LANCZOS).save(os.path.join(BASE, "Square44x44Logo.targetsize-48_altform-lightunplated.png"))
    icon.resize((48, 48), Image.Resampling.LANCZOS).save(os.path.join(BASE, "LockScreenLogo.scale-200.png"))
    wide = Image.new("RGBA", (620, 300), (11, 22, 48, 255))
    logo_wide = icon.resize((260, 260), Image.Resampling.LANCZOS)
    wide.alpha_composite(logo_wide, ((620 - 260) // 2, (300 - 260) // 2))
    wide.save(os.path.join(BASE, "Wide310x150Logo.scale-200.png"))
    splash = Image.new("RGBA", (1240, 600), (11, 22, 48, 255))
    logo_splash = icon.resize((420, 420), Image.Resampling.LANCZOS)
    splash.alpha_composite(logo_splash, ((1240 - 420) // 2, (600 - 420) // 2))
    splash.save(os.path.join(BASE, "SplashScreen.scale-200.png"))
    print("icons written to", BASE)


if __name__ == "__main__":
    main()
