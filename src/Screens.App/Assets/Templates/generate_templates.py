#!/usr/bin/env python3
"""Generates the bundled template background images (purple palette,
original geometric artwork, no stock imagery). Run from this directory:
    python3 generate_templates.py
Outputs land next to this script; only the PNGs ship in the app.
"""
import math
from PIL import Image, ImageDraw

PRIMARY = (124, 58, 237)       # #7C3AED
PRIMARY_DARK = (109, 40, 217)  # #6D28D9
PRIMARY_LIGHT = (167, 139, 250)  # #A78BFA
SURFACE_DARK = (26, 16, 40)    # #1A1028
SURFACE_LIGHT = (248, 246, 252)  # #F8F6FC
WHITE = (255, 255, 255)


def lerp_color(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def vertical_gradient(size, top, bottom):
    w, h = size
    img = Image.new("RGB", size, top)
    draw = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(h - 1, 1)
        draw.line([(0, y), (w, y)], fill=lerp_color(top, bottom, t))
    return img


def diagonal_gradient(size, top_left, bottom_right):
    w, h = size
    img = Image.new("RGB", size)
    px = img.load()
    diag = w + h
    for y in range(h):
        for x in range(0, w, 2):
            t = (x + y) / diag
            c = lerp_color(top_left, bottom_right, t)
            px[x, y] = c
            if x + 1 < w:
                px[x + 1, y] = c
    return img


def rounded_rect(draw, box, radius, **kwargs):
    draw.rounded_rectangle(box, radius=radius, **kwargs)


def save(img, name):
    img.save(name, "PNG")
    print(f"wrote {name} ({img.width}x{img.height})")


# 1. Certificate — cert-classic
def cert_classic():
    w, h = 2000, 1414
    img = vertical_gradient((w, h), SURFACE_LIGHT, (238, 231, 250))
    draw = ImageDraw.Draw(img)
    margin = 60
    draw.rectangle([margin, margin, w - margin, h - margin], outline=PRIMARY, width=6)
    draw.rectangle([margin + 18, margin + 18, w - margin - 18, h - margin - 18], outline=PRIMARY_LIGHT, width=2)
    # laurel-ish corner flourishes: simple concentric arcs
    for cx, cy in [(margin + 90, margin + 90), (w - margin - 90, margin + 90),
                   (margin + 90, h - margin - 90), (w - margin - 90, h - margin - 90)]:
        for r in (50, 35, 20):
            draw.ellipse([cx - r, cy - r, cx + r, cy + r], outline=PRIMARY_LIGHT, width=3)
    draw.ellipse([w / 2 - 40, margin + 40, w / 2 + 40, margin + 120], fill=PRIMARY)
    save(img, "cert-classic.png")


# 2. ID badge / name tag — badge-lanyard
def badge_lanyard():
    w, h = 1200, 1800
    img = Image.new("RGB", (w, h), WHITE)
    draw = ImageDraw.Draw(img)
    header_h = 520
    header = diagonal_gradient((w, header_h), PRIMARY_DARK, PRIMARY_LIGHT)
    img.paste(header, (0, 0))
    draw.ellipse([w / 2 - 130, header_h - 130, w / 2 + 130, header_h + 130], fill=WHITE)
    draw.ellipse([w / 2 - 100, header_h - 100, w / 2 + 100, header_h + 100], fill=PRIMARY_LIGHT)
    draw.rounded_rectangle([w / 2 - 250, h - 260, w / 2 + 250, h - 200], radius=18, fill=PRIMARY)
    # lanyard hole
    draw.ellipse([w / 2 - 40, 30, w / 2 + 40, 110], outline=SURFACE_DARK, width=8)
    save(img, "badge-lanyard.png")


# 3. Event flyer — flyer-event
def flyer_event():
    w, h = 1500, 2100
    img = diagonal_gradient((w, h), SURFACE_DARK, PRIMARY_DARK)
    draw = ImageDraw.Draw(img)
    # angled band
    draw.polygon([(0, h * 0.62), (w, h * 0.50), (w, h * 0.62), (0, h * 0.74)], fill=PRIMARY)
    draw.polygon([(0, h * 0.66), (w, h * 0.54), (w, h * 0.58), (0, h * 0.70)], fill=PRIMARY_LIGHT)
    for i in range(24):
        x = (i * 97) % w
        y = int(h * 0.08 + (i * 53) % int(h * 0.35))
        r = 3 + (i % 4)
        draw.ellipse([x - r, y - r, x + r, y + r], fill=PRIMARY_LIGHT)
    save(img, "flyer-event.png")


# 4. Product price poster — price-poster
def price_poster():
    w, h = 1600, 1600
    img = Image.new("RGB", (w, h), SURFACE_LIGHT)
    draw = ImageDraw.Draw(img)
    draw.rectangle([0, 0, w, 260], fill=PRIMARY)
    # price burst (starburst polygon)
    cx, cy, r1, r2, points = w / 2, h * 0.62, 430, 500, 14
    star = []
    for i in range(points * 2):
        r = r2 if i % 2 == 0 else r1
        ang = math.pi * i / points
        star.append((cx + r * math.sin(ang), cy - r * math.cos(ang)))
    draw.polygon(star, fill=PRIMARY_DARK)
    draw.ellipse([cx - 380, cy - 380, cx + 380, cy + 380], fill=PRIMARY)
    save(img, "price-poster.png")


# 5. Sale / discount tag — sale-tag
def sale_tag():
    w, h = 1400, 900
    img = Image.new("RGB", (w, h), SURFACE_LIGHT)
    draw = ImageDraw.Draw(img)
    tag = [(140, 0), (w, 0), (w, h), (140, h), (0, h / 2)]
    draw.polygon(tag, fill=PRIMARY)
    draw.polygon([(x + 24 if x > 140 else x + 40, y) for x, y in tag], fill=PRIMARY_DARK)
    draw.ellipse([70, h / 2 - 34, 138, h / 2 + 34], fill=SURFACE_LIGHT)
    save(img, "sale-tag.png")


# 6. Social quote post — quote-social
def quote_social():
    w = h = 1600
    img = vertical_gradient((w, h), PRIMARY_DARK, SURFACE_DARK)
    draw = ImageDraw.Draw(img)
    draw.polygon([(140, 420), (140, 240), (300, 240), (240, 420)], fill=PRIMARY_LIGHT)
    draw.polygon([(340, 420), (340, 240), (500, 240), (440, 420)], fill=PRIMARY_LIGHT)
    for i in range(6):
        y = h - 200 + i * 4
        draw.line([(0, y), (w, y)], fill=lerp_color(PRIMARY_LIGHT, SURFACE_DARK, i / 6), width=2)
    save(img, "quote-social.png")


# 7. Real estate listing card — realestate-card
def realestate_card():
    w, h = 1600, 1200
    img = Image.new("RGB", (w, h), WHITE)
    draw = ImageDraw.Draw(img)
    sky = vertical_gradient((w, 640), PRIMARY_LIGHT, SURFACE_LIGHT)
    img.paste(sky, (0, 0))
    # simple house silhouette skyline, original geometric, not a photo
    ground_y = 640
    draw.rectangle([0, ground_y, w, h], fill=SURFACE_LIGHT)
    for i, (x, bw, bh) in enumerate([(80, 220, 260), (340, 260, 340), (640, 200, 220), (880, 300, 300), (1220, 240, 260)]):
        color = PRIMARY if i % 2 == 0 else PRIMARY_DARK
        draw.rectangle([x, ground_y - bh, x + bw, ground_y], fill=color)
    draw.rectangle([0, h - 160, w, h], fill=PRIMARY_DARK)
    save(img, "realestate-card.png")


# 8. Employee of the month award — award-employee
def award_employee():
    w, h = 1600, 1200
    img = diagonal_gradient((w, h), SURFACE_LIGHT, (232, 222, 250))
    draw = ImageDraw.Draw(img)
    margin = 50
    draw.rectangle([margin, margin, w - margin, h - margin], outline=PRIMARY_DARK, width=8)
    cx, cy = w / 2, 300
    for r, c in [(150, PRIMARY), (120, PRIMARY_LIGHT), (90, WHITE)]:
        draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=c)
    draw.polygon([(cx - 40, cy + 70), (cx, cy + 220), (cx - 90, cy + 160)], fill=PRIMARY_DARK)
    draw.polygon([(cx + 40, cy + 70), (cx, cy + 220), (cx + 90, cy + 160)], fill=PRIMARY_DARK)
    save(img, "award-employee.png")


# 9. Generic phone mockup — phone-mockup (device frame for a user photo; no brand/logo)
def phone_mockup():
    w, h = 1200, 2640
    img = Image.new("RGB", (w, h), SURFACE_LIGHT)
    draw = ImageDraw.Draw(img)
    top = vertical_gradient((w, 2400), (238, 231, 250), SURFACE_LIGHT)
    img.paste(top, (0, 0))
    draw = ImageDraw.Draw(img)

    bezel_margin = 90
    bx0, by0, bx1, by1 = bezel_margin, bezel_margin, w - bezel_margin, 2400 - bezel_margin
    bezel_radius = 140
    draw.rounded_rectangle([bx0, by0, bx1, by1], radius=bezel_radius, fill=SURFACE_DARK)

    screen_inset = 28
    sx0, sy0, sx1, sy1 = bx0 + screen_inset, by0 + screen_inset, bx1 - screen_inset, by1 - screen_inset
    draw.rounded_rectangle([sx0, sy0, sx1, sy1], radius=bezel_radius - screen_inset, fill=(15, 8, 26))

    cutout_w, cutout_h = 200, 46
    cx = w / 2
    draw.rounded_rectangle([cx - cutout_w / 2, sy0 + 36, cx + cutout_w / 2, sy0 + 36 + cutout_h], radius=cutout_h / 2, fill=SURFACE_DARK)

    bar_w, bar_h = 260, 12
    draw.rounded_rectangle([cx - bar_w / 2, sy1 - 46, cx + bar_w / 2, sy1 - 46 + bar_h], radius=bar_h / 2, fill=(80, 70, 100))

    draw.rounded_rectangle([bx0 - 6, by0 + 260, bx0 + 2, by0 + 380], radius=6, fill=SURFACE_DARK)
    draw.rounded_rectangle([bx1 - 2, by0 + 300, bx1 + 6, by0 + 480], radius=6, fill=SURFACE_DARK)

    save(img, "phone-mockup.png")


if __name__ == "__main__":
    cert_classic()
    badge_lanyard()
    flyer_event()
    price_poster()
    sale_tag()
    quote_social()
    realestate_card()
    award_employee()
    phone_mockup()
