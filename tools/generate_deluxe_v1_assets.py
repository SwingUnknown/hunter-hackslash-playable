from __future__ import annotations

import json
import math
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageFont


FRAME_W = 144
FRAME_H = 216
SRC_FRAMES = 12
OUT_FRAMES = 18
DIRS = 8
DIRS_PER_ROW = 4
UPSCALE = 2
STATES = [
    "idle",
    "run",
    "dash",
    "light1",
    "light2",
    "light3",
    "light4",
    "heavy",
    "skill",
    "ultimate",
    "hit",
]

ROOT = Path(__file__).resolve().parents[1]
CHAR_SRC = ROOT / "assets" / "characters" / "action8" / "pose_v6"
ENEMY_SRC = ROOT / "assets" / "enemies" / "action8"
CHAR_OUT = ROOT / "assets" / "characters" / "action8" / "deluxe_v1"
ENEMY_OUT = ROOT / "assets" / "enemies" / "action8" / "deluxe_v1"
ILLUST_OUT = ROOT / "assets" / "illustrations" / "deluxe_v1"


@dataclass(frozen=True)
class AssetSpec:
    source: Path
    output: Path
    name: str
    kind: str
    aura: tuple[int, int, int]
    rim: tuple[int, int, int]
    accent: tuple[int, int, int]
    motif: str
    boss: bool = False


CHARACTERS = [
    AssetSpec(CHAR_SRC / "gon_action8.png", CHAR_OUT / "gon_action8.png", "Gon", "hunter", (88, 204, 110), (252, 255, 188), (36, 78, 42), "impact"),
    AssetSpec(CHAR_SRC / "killua_action8.png", CHAR_OUT / "killua_action8.png", "Killua", "hunter", (120, 212, 248), (245, 252, 255), (86, 160, 230), "lightning"),
    AssetSpec(CHAR_SRC / "kurapika_action8.png", CHAR_OUT / "kurapika_action8.png", "Kurapika", "hunter", (242, 207, 107), (255, 250, 216), (58, 82, 170), "chain"),
    AssetSpec(CHAR_SRC / "adult_gon_action8.png", CHAR_OUT / "adult_gon_action8.png", "Awakened Gon", "hunter", (214, 255, 104), (255, 246, 190), (18, 28, 14), "vow", True),
]

ENEMY_COLORS = {
    "card_thief": ((95, 224, 205), (230, 255, 246), (38, 92, 96), "cards", False),
    "bomb_trapper": ((255, 123, 66), (255, 222, 165), (120, 48, 36), "bomb", False),
    "mafia_gunner": ((238, 202, 116), (255, 239, 190), (56, 66, 92), "bullet", False),
    "shadow_beast_tank": ((154, 230, 109), (226, 255, 185), (36, 78, 60), "claw", False),
    "ant_soldier": ((204, 240, 76), (248, 255, 170), (50, 78, 42), "ant", False),
    "ant_commander": ((204, 240, 76), (248, 255, 170), (74, 108, 42), "ant", False),
    "bomber_leader_boss": ((255, 123, 66), (255, 226, 180), (128, 40, 32), "bomb", True),
    "butler_captain_boss": ((120, 212, 248), (245, 252, 255), (38, 52, 74), "needle", True),
    "ant_king_boss": ((242, 207, 107), (255, 255, 196), (48, 74, 34), "king", True),
    "assassin_butler": ((120, 212, 248), (245, 252, 255), (38, 52, 74), "needle", False),
    "guard_beast": ((186, 124, 255), (242, 218, 255), (70, 36, 88), "claw", False),
    "arena_trickster_boss": ((242, 207, 107), (255, 248, 205), (86, 54, 104), "cards", True),
    "nen_boxer": ((242, 207, 107), (255, 240, 180), (82, 60, 42), "impact", False),
    "rookie_fighter": ((95, 224, 205), (230, 255, 246), (44, 82, 70), "impact", False),
}


def enemy_specs() -> list[AssetSpec]:
    specs: list[AssetSpec] = []
    for src in sorted(ENEMY_SRC.glob("*_action8.png")):
        if src.parent.name == "deluxe_v1":
            continue
        stem = src.stem.replace("_action8", "")
        aura, rim, accent, motif, boss = ENEMY_COLORS.get(stem, ((160, 210, 180), (245, 255, 230), (60, 80, 70), "impact", "boss" in stem))
        title = " ".join(part.capitalize() for part in stem.split("_"))
        specs.append(AssetSpec(src, ENEMY_OUT / src.name, title, "enemy", aura, rim, accent, motif, boss))
    return specs


def ensure_dirs() -> None:
    for path in (CHAR_OUT, ENEMY_OUT, ILLUST_OUT):
        path.mkdir(parents=True, exist_ok=True)


def source_box(state_index: int, direction: int, frame: int, frames: int) -> tuple[int, int, int, int]:
    x = ((direction % DIRS_PER_ROW) * frames + frame) * FRAME_W
    y = (state_index * 2 + direction // DIRS_PER_ROW) * FRAME_H
    return x, y, x + FRAME_W, y + FRAME_H


def premul_blend(a: Image.Image, b: Image.Image, t: float) -> Image.Image:
    if t <= 0:
        return a.copy()
    if t >= 1:
        return b.copy()
    return Image.blend(a, b, t)


def resample_frame(sheet: Image.Image, state_index: int, direction: int, out_frame: int) -> Image.Image:
    src_pos = out_frame * (SRC_FRAMES - 1) / (OUT_FRAMES - 1)
    f0 = int(math.floor(src_pos))
    f1 = min(SRC_FRAMES - 1, f0 + 1)
    t = src_pos - f0
    a = sheet.crop(source_box(state_index, direction, f0, SRC_FRAMES))
    b = sheet.crop(source_box(state_index, direction, f1, SRC_FRAMES))
    return premul_blend(a, b, t)


def alpha_mask(frame: Image.Image) -> Image.Image:
    return frame.getchannel("A")


def solid_from_alpha(mask: Image.Image, color: tuple[int, int, int], alpha_mul: float = 1.0) -> Image.Image:
    img = Image.new("RGBA", mask.size, (*color, 255))
    img.putalpha(mask.point(lambda v: max(0, min(255, int(v * alpha_mul)))))
    return img


def compose_outline(frame: Image.Image, spec: AssetSpec, state: str, direction: int, out_frame: int) -> Image.Image:
    scale = UPSCALE
    hi = frame.resize((FRAME_W * scale, FRAME_H * scale), Image.Resampling.LANCZOS)
    mask = alpha_mask(hi)

    outer = mask.filter(ImageFilter.MaxFilter(7 if spec.boss else 5))
    middle = mask.filter(ImageFilter.MaxFilter(5))
    inner = mask.filter(ImageFilter.MinFilter(3))
    edge = ImageChops.subtract(middle, inner)
    aura_edge = ImageChops.subtract(outer, middle).filter(ImageFilter.GaussianBlur(1.4))

    out = Image.new("RGBA", hi.size, (0, 0, 0, 0))
    shadow = solid_from_alpha(mask.filter(ImageFilter.GaussianBlur(4)), (0, 0, 0), 0.34 if spec.boss else 0.26)
    out.alpha_composite(shadow, (0, 6 if spec.boss else 5))
    out.alpha_composite(solid_from_alpha(outer, (5, 8, 8), 0.95))
    out.alpha_composite(solid_from_alpha(aura_edge, spec.aura, 0.72 if spec.boss else 0.54))
    out.alpha_composite(hi)
    out.alpha_composite(solid_from_alpha(edge, spec.rim, 0.28 if state in {"skill", "ultimate", "dash"} else 0.16))

    draw = ImageDraw.Draw(out, "RGBA")
    draw_state_effects(draw, spec, state, direction, out_frame, scale)
    out = ImageEnhance.Color(out).enhance(1.10)
    out = ImageEnhance.Contrast(out).enhance(1.08)
    out = out.filter(ImageFilter.UnsharpMask(radius=1.1, percent=120, threshold=4))
    return out.resize((FRAME_W, FRAME_H), Image.Resampling.LANCZOS)


def direction_angle(direction: int) -> float:
    return [math.pi / 2, math.pi / 4, 0, -math.pi / 4, -math.pi / 2, -3 * math.pi / 4, math.pi, 3 * math.pi / 4][direction]


def draw_state_effects(draw: ImageDraw.ImageDraw, spec: AssetSpec, state: str, direction: int, frame: int, scale: int) -> None:
    angle = direction_angle(direction)
    dx, dy = math.cos(angle), math.sin(angle)
    sx, sy = -dy, dx * 0.62
    t = frame / max(1, OUT_FRAMES - 1)
    pulse = math.sin(t * math.pi)
    cx = 72 * scale + dx * pulse * 20 * scale
    cy = 126 * scale + dy * pulse * 8 * scale
    aura = (*spec.aura, int(54 + pulse * 90))
    rim = (*spec.rim, int(65 + pulse * 110))

    if state in {"run", "dash"}:
        trail_count = 4 if state == "dash" else 2
        for lane in range(trail_count):
            offset = (lane - (trail_count - 1) / 2) * 6 * scale
            x0 = cx - dx * (42 + lane * 14) * scale + sx * offset
            y0 = cy - dy * (18 + lane * 5) * scale + sy * offset
            x1 = cx - dx * (10 + lane * 5) * scale + sx * offset * 0.2
            y1 = cy - dy * (5 + lane * 2) * scale + sy * offset * 0.2
            draw.line([(x0, y0), (x1, y1)], fill=(*spec.aura, 50 if state == "run" else 104), width=max(1, 2 * scale))

    if state.startswith("light") or state in {"heavy", "skill", "ultimate"}:
        radius = {
            "light1": 38,
            "light2": 44,
            "light3": 50,
            "light4": 58,
            "heavy": 66,
            "skill": 78,
            "ultimate": 98,
        }.get(state, 42) * scale
        arc = 1.25 if state.startswith("light") else 1.9
        points = []
        for i in range(28):
            u = i / 27
            a = angle - arc * 0.5 + arc * u
            r = radius * (0.58 + math.sin(u * math.pi) * 0.42)
            points.append((cx + math.cos(a) * r, cy + math.sin(a) * r * 0.66))
        draw.line(points, fill=(*spec.aura, 72 if state != "ultimate" else 115), width=max(1, 5 * scale), joint="curve")
        draw.line(points, fill=rim, width=max(1, 2 * scale), joint="curve")

        if spec.motif == "lightning":
            for lane in (-1, 0, 1):
                pts = []
                for i in range(6):
                    u = i / 5
                    jitter = ((-1) ** i) * (7 + pulse * 8) * scale
                    pts.append((cx - dx * u * 82 * scale + sx * (lane * 10 * scale + jitter), cy - dy * u * 40 * scale + sy * (lane * 10 * scale - jitter * 0.2)))
                draw.line(pts, fill=(*spec.rim, 190), width=max(1, 2 * scale), joint="curve")
                draw.line(pts, fill=(*spec.aura, 92), width=max(1, 5 * scale), joint="curve")
        elif spec.motif == "chain":
            for i in range(9):
                u = i / 8
                px = cx + math.cos(angle - 0.86 + u * 1.72) * radius * 0.55
                py = cy + math.sin(angle - 0.86 + u * 1.72) * radius * 0.34
                rr = (4 + pulse * 2) * scale
                draw.ellipse((px - rr, py - rr * 0.55, px + rr, py + rr * 0.55), outline=(*spec.rim, 170), width=max(1, 2 * scale))
        elif spec.motif in {"bomb", "cards"}:
            for i in range(6):
                u = i / 5 - 0.5
                px = cx + sx * u * 70 * scale + dx * (20 + abs(u) * 25) * scale
                py = cy + sy * u * 24 * scale + dy * 18 * scale
                if spec.motif == "cards":
                    draw.rounded_rectangle((px - 7 * scale, py - 11 * scale, px + 7 * scale, py + 11 * scale), radius=2 * scale, outline=(*spec.rim, 138), width=max(1, scale))
                else:
                    draw.ellipse((px - 5 * scale, py - 5 * scale, px + 5 * scale, py + 5 * scale), fill=(*spec.aura, 118))
        elif spec.motif in {"ant", "king", "claw"}:
            for i in range(4):
                u = i / 3 - 0.5
                start = (cx + sx * u * 54 * scale, cy + sy * u * 20 * scale)
                end = (start[0] + dx * (36 + pulse * 20) * scale + sx * u * 14 * scale, start[1] + dy * (22 + pulse * 8) * scale)
                draw.line([start, end], fill=(*spec.aura, 88), width=max(1, 3 * scale))
        elif spec.motif == "vow":
            for i in range(13):
                u = i / 12 - 0.5
                root = (72 * scale + sx * u * 96 * scale, 176 * scale - abs(u) * 18 * scale)
                tip = (root[0] - dx * (36 + pulse * 42) * scale + sx * u * 18 * scale, 28 * scale + abs(u) * 20 * scale)
                draw.line([root, tip], fill=(*spec.accent, 138), width=max(1, 3 * scale))
                draw.line([root, tip], fill=(*spec.aura, 68), width=max(1, 6 * scale))


def make_sheet(spec: AssetSpec) -> None:
    src = Image.open(spec.source).convert("RGBA")
    sheet = Image.new("RGBA", (FRAME_W * OUT_FRAMES * DIRS_PER_ROW, FRAME_H * len(STATES) * 2), (0, 0, 0, 0))
    for s, state in enumerate(STATES):
        for d in range(DIRS):
            for f in range(OUT_FRAMES):
                frame = resample_frame(src, s, d, f)
                frame = compose_outline(frame, spec, state, d, f)
                x = ((d % DIRS_PER_ROW) * OUT_FRAMES + f) * FRAME_W
                y = (s * 2 + d // DIRS_PER_ROW) * FRAME_H
                sheet.alpha_composite(frame, (x, y))
    spec.output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(spec.output)


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        "C:/Windows/Fonts/seguisb.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf",
        "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf",
    ]
    for candidate in candidates:
        try:
            return ImageFont.truetype(candidate, size)
        except OSError:
            continue
    return ImageFont.load_default()


def best_frame(sheet: Image.Image, state: str, direction: int, frame: int) -> Image.Image:
    s = STATES.index(state)
    return sheet.crop(source_box(s, direction, frame, OUT_FRAMES))


def make_illustration(spec: AssetSpec) -> None:
    sheet = Image.open(spec.output).convert("RGBA")
    canvas = Image.new("RGBA", (960, 1280), (12, 16, 18, 255))
    draw = ImageDraw.Draw(canvas, "RGBA")
    for y in range(1280):
        t = y / 1279
        r = int(12 + spec.accent[0] * 0.10 * (1 - t) + spec.aura[0] * 0.04 * t)
        g = int(16 + spec.accent[1] * 0.09 * (1 - t) + spec.aura[1] * 0.04 * t)
        b = int(18 + spec.accent[2] * 0.10 * (1 - t) + spec.aura[2] * 0.04 * t)
        draw.line([(0, y), (960, y)], fill=(r, g, b, 255))

    for i in range(18):
        angle = i * math.tau / 18
        x = 480 + math.cos(angle) * (260 + (i % 3) * 46)
        y = 570 + math.sin(angle) * (170 + (i % 4) * 28)
        draw.ellipse((x - 110, y - 42, x + 110, y + 42), outline=(*spec.aura, 34), width=3)
    for i in range(7):
        draw.line([(0, 180 + i * 112), (960, 78 + i * 142)], fill=(*spec.rim, 22), width=2)

    pose_state = "ultimate" if spec.boss or spec.motif in {"vow", "king"} else "skill"
    center = best_frame(sheet, pose_state, 2, OUT_FRAMES // 2)
    back = best_frame(sheet, "idle", 4, 3)
    side = best_frame(sheet, "run", 1, 7)
    for img, box, alpha in [
        (back.resize((300, 450), Image.Resampling.LANCZOS), (48, 424), 72),
        (side.resize((280, 420), Image.Resampling.LANCZOS), (650, 462), 78),
    ]:
        ghost = solid_from_alpha(img.getchannel("A"), spec.aura, alpha / 255).filter(ImageFilter.GaussianBlur(0.6))
        canvas.alpha_composite(ghost, box)
        faint = img.copy()
        faint.putalpha(faint.getchannel("A").point(lambda v: int(v * 0.34)))
        canvas.alpha_composite(faint, box)

    hero = center.resize((560 if spec.boss else 510, 840 if spec.boss else 765), Image.Resampling.LANCZOS)
    glow = solid_from_alpha(hero.getchannel("A").filter(ImageFilter.GaussianBlur(10)), spec.aura, 0.42)
    hero_x = 480 - hero.width // 2
    hero_y = 314 if spec.boss else 372
    canvas.alpha_composite(glow, (hero_x, hero_y))
    canvas.alpha_composite(hero, (hero_x, hero_y))

    title_font = font(64 if len(spec.name) < 14 else 52, True)
    sub_font = font(26, True)
    small_font = font(22, False)
    draw.rounded_rectangle((54, 58, 906, 184), radius=18, fill=(0, 0, 0, 126), outline=(*spec.aura, 156), width=3)
    draw.text((82, 78), spec.name, fill=(245, 248, 238, 255), font=title_font)
    tag = f"{spec.kind.upper()} / {spec.motif.upper()} / {'BOSS' if spec.boss else 'DELUXE'}"
    draw.text((86, 148), tag, fill=(*spec.rim, 230), font=sub_font)
    draw.rounded_rectangle((80, 1114, 880, 1208), radius=16, fill=(0, 0, 0, 150), outline=(*spec.rim, 120), width=2)
    draw.text((110, 1137), "8-direction action sheet | 18 animation frames | enhanced aura silhouette", fill=(224, 232, 226, 230), font=small_font)

    out = ILLUST_OUT / f"{spec.output.stem.replace('_action8', '')}_illustration.png"
    canvas.convert("RGB").save(out, quality=96)


def write_atlas(path: Path) -> None:
    data = {
        "frameWidth": FRAME_W,
        "frameHeight": FRAME_H,
        "frames": OUT_FRAMES,
        "directions": ["front", "frontRight", "right", "backRight", "back", "backLeft", "left", "frontLeft"],
        "dirsPerRow": DIRS_PER_ROW,
        "cols": OUT_FRAMES * DIRS_PER_ROW,
        "states": STATES,
        "rows": len(STATES) * 2,
        "version": "deluxe_v1",
    }
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")


def make_lineup(specs: list[AssetSpec]) -> None:
    cards = []
    for spec in specs:
        path = ILLUST_OUT / f"{spec.output.stem.replace('_action8', '')}_illustration.png"
        if path.exists():
            img = Image.open(path).convert("RGB").resize((240, 320), Image.Resampling.LANCZOS)
            cards.append((spec.name, img, spec.aura))
    cols = 4
    rows = math.ceil(len(cards) / cols)
    canvas = Image.new("RGB", (cols * 240, rows * 320), (10, 14, 16))
    draw = ImageDraw.Draw(canvas, "RGBA")
    for i, (_name, img, aura) in enumerate(cards):
        x = (i % cols) * 240
        y = (i // cols) * 320
        canvas.paste(img, (x, y))
        draw.rectangle((x, y, x + 239, y + 319), outline=(*aura, 180), width=2)
    canvas.save(ILLUST_OUT / "all_characters_lineup.png", quality=96)


def main() -> None:
    ensure_dirs()
    specs = CHARACTERS + enemy_specs()
    for spec in specs:
        if not spec.source.exists():
            print(f"missing {spec.source}")
            continue
        illustration = ILLUST_OUT / f"{spec.output.stem.replace('_action8', '')}_illustration.png"
        if spec.output.exists() and illustration.exists():
            print(f"skip existing {spec.output.relative_to(ROOT)}")
            continue
        print(f"deluxe {spec.name} -> {spec.output.relative_to(ROOT)}")
        make_sheet(spec)
        make_illustration(spec)
    write_atlas(CHAR_OUT / "action8_atlas.json")
    write_atlas(ENEMY_OUT / "action8_atlas.json")
    make_lineup(specs)


if __name__ == "__main__":
    main()
