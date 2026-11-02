from __future__ import annotations

import io
import math
import struct
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "glove-sprites.png"
OUTPUT = ROOT / "cursor-pack"
SIZES = (32, 48, 64)
ANIMATION_FRAMES = 10


def visible_bbox(image: Image.Image, threshold: int = 5) -> tuple[int, int, int, int]:
    alpha = image.getchannel("A")
    mask = alpha.point(lambda value: 255 if value >= threshold else 0)
    box = mask.getbbox()
    if box is None:
        raise ValueError("The sprite contains no visible pixels")
    return box


def write_cursor(path: Path, frames: list[tuple[Image.Image, tuple[int, int]]]) -> None:
    """Write a multi-resolution Windows CUR file with PNG-compressed images."""
    payloads: list[tuple[bytes, tuple[int, int], tuple[int, int]]] = []
    for image, hotspot in frames:
        stream = io.BytesIO()
        image.save(stream, format="PNG", optimize=True)
        payloads.append((stream.getvalue(), image.size, hotspot))

    header_size = 6 + 16 * len(payloads)
    offset = header_size
    entries: list[bytes] = []
    for payload, (width, height), (hotspot_x, hotspot_y) in payloads:
        entries.append(
            struct.pack(
                "<BBBBHHII",
                0 if width == 256 else width,
                0 if height == 256 else height,
                0,
                0,
                hotspot_x,
                hotspot_y,
                len(payload),
                offset,
            )
        )
        offset += len(payload)

    path.write_bytes(
        struct.pack("<HHH", 0, 2, len(payloads))
        + b"".join(entries)
        + b"".join(payload for payload, _, _ in payloads)
    )


def cursor_bytes(frames: list[tuple[Image.Image, tuple[int, int]]]) -> bytes:
    """Return a complete multi-resolution CUR payload for an ANI frame."""
    payloads: list[tuple[bytes, tuple[int, int], tuple[int, int]]] = []
    for image, hotspot in frames:
        stream = io.BytesIO()
        image.save(stream, format="PNG", optimize=True)
        payloads.append((stream.getvalue(), image.size, hotspot))

    header_size = 6 + 16 * len(payloads)
    offset = header_size
    entries: list[bytes] = []
    for payload, (width, height), (hotspot_x, hotspot_y) in payloads:
        entries.append(
            struct.pack(
                "<BBBBHHII",
                0 if width == 256 else width,
                0 if height == 256 else height,
                0,
                0,
                hotspot_x,
                hotspot_y,
                len(payload),
                offset,
            )
        )
        offset += len(payload)

    return (
        struct.pack("<HHH", 0, 2, len(payloads))
        + b"".join(entries)
        + b"".join(payload for payload, _, _ in payloads)
    )


def riff_chunk(tag: bytes, payload: bytes) -> bytes:
    padding = b"\0" if len(payload) % 2 else b""
    return tag + struct.pack("<I", len(payload)) + payload + padding


def write_animated_cursor(
    path: Path,
    frames: list[list[tuple[Image.Image, tuple[int, int]]]],
    jiffies_per_frame: int = 4,
) -> None:
    frame_chunks = b"".join(
        riff_chunk(b"icon", cursor_bytes(frame)) for frame in frames
    )
    animation_header = struct.pack(
        "<9I",
        36,
        len(frames),
        len(frames),
        0,
        0,
        32,
        1,
        jiffies_per_frame,
        1,
    )
    body = (
        b"ACON"
        + riff_chunk(b"anih", animation_header)
        + riff_chunk(b"rate", struct.pack("<" + "I" * len(frames), *([jiffies_per_frame] * len(frames))))
        + riff_chunk(b"LIST", b"fram" + frame_chunks)
    )
    path.write_bytes(b"RIFF" + struct.pack("<I", len(body)) + body)


def lighten_glove(image: Image.Image) -> Image.Image:
    alpha = image.getchannel("A")
    rgb = image.convert("RGB")
    rgb = ImageEnhance.Color(rgb).enhance(0.72)
    rgb = ImageEnhance.Brightness(rgb).enhance(1.07)
    ivory = Image.new("RGB", rgb.size, (255, 246, 222))
    rgb = Image.blend(rgb, ivory, 0.12)
    result = rgb.convert("RGBA")
    result.putalpha(alpha)
    return result


def spinner_frame(size: int, phase: int, center: tuple[float, float], radius: float, dot_radius: float) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    for step in range(ANIMATION_FRAMES):
        trail = (step - phase) % ANIMATION_FRAMES
        opacity = max(48, 255 - trail * 23)
        angle = -math.pi / 2 + (2 * math.pi * step / ANIMATION_FRAMES)
        x = center[0] + math.cos(angle) * radius
        y = center[1] + math.sin(angle) * radius
        glow = dot_radius * (1.45 if trail == 0 else 1.0)
        draw.ellipse(
            (x - glow, y - glow, x + glow, y + glow),
            fill=(255, 244, 191, opacity),
            outline=(115, 77, 26, min(255, opacity + 35)),
            width=max(1, round(size / 64)),
        )
    return image


def hearth_ibeam(size: int) -> Image.Image:
    """Draw a readable ivory-and-gold text cursor with Hearthstone-style trim."""
    scale = 4
    high_size = size * scale
    image = Image.new("RGBA", (high_size, high_size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    center = high_size // 2
    top = round(high_size * 0.12)
    bottom = round(high_size * 0.88)
    stem_half = max(scale, round(high_size * 0.045))
    serif_half = round(high_size * 0.18)
    serif_height = max(scale * 2, round(high_size * 0.075))
    outline = max(scale, round(high_size * 0.025))

    dark = (79, 49, 19, 255)
    gold = (197, 143, 49, 255)
    ivory = (255, 247, 220, 255)

    # Dark silhouette keeps the I-beam readable on both white and dark text fields.
    draw.rounded_rectangle(
        (center - stem_half - outline, top, center + stem_half + outline, bottom),
        radius=outline,
        fill=dark,
    )
    draw.rounded_rectangle(
        (center - serif_half - outline, top, center + serif_half + outline, top + serif_height + outline),
        radius=outline,
        fill=dark,
    )
    draw.rounded_rectangle(
        (center - serif_half - outline, bottom - serif_height - outline, center + serif_half + outline, bottom),
        radius=outline,
        fill=dark,
    )

    draw.rounded_rectangle(
        (center - stem_half, top + outline, center + stem_half, bottom - outline),
        radius=max(1, outline // 2),
        fill=ivory,
        outline=gold,
        width=outline,
    )
    draw.rounded_rectangle(
        (center - serif_half, top + outline, center + serif_half, top + serif_height),
        radius=max(1, outline // 2),
        fill=ivory,
        outline=gold,
        width=outline,
    )
    draw.rounded_rectangle(
        (center - serif_half, bottom - serif_height, center + serif_half, bottom - outline),
        radius=max(1, outline // 2),
        fill=ivory,
        outline=gold,
        width=outline,
    )

    gem = max(scale * 2, round(high_size * 0.055))
    draw.polygon(
        ((center, center - gem), (center + gem, center), (center, center + gem), (center - gem, center)),
        fill=(241, 186, 68, 255),
        outline=dark,
    )
    return image.resize((size, size), Image.Resampling.LANCZOS)


def render_pose(
    pose: Image.Image,
    source_scale: float,
    canvas_size: int,
    align_bottom: bool,
) -> Image.Image:
    box = visible_bbox(pose)
    cropped = pose.crop(box)
    rendered_size = (
        max(1, round(cropped.width * source_scale)),
        max(1, round(cropped.height * source_scale)),
    )
    rendered = cropped.resize(rendered_size, Image.Resampling.LANCZOS)

    canvas = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    margin = max(1, round(canvas_size / 32))
    y = canvas_size - margin - rendered.height if align_bottom else margin
    canvas.alpha_composite(rendered, (margin, y))
    return canvas


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    sheet = Image.open(SOURCE).convert("RGBA")
    midpoint = sheet.width // 2
    normal_source = sheet.crop((0, 0, midpoint, sheet.height))
    pressed_source = sheet.crop((midpoint, 0, sheet.width, sheet.height))
    normal_source = lighten_glove(normal_source)
    pressed_source = lighten_glove(pressed_source)
    pressed_source = pressed_source.rotate(10, resample=Image.Resampling.BICUBIC, expand=True)

    normal_height = visible_bbox(normal_source)[3] - visible_bbox(normal_source)[1]
    normal_frames: list[tuple[Image.Image, tuple[int, int]]] = []
    pressed_frames: list[tuple[Image.Image, tuple[int, int]]] = []

    preview = Image.new("RGBA", (640, 160), (25, 20, 16, 255))
    for size in SIZES:
        scale = (size - max(2, round(size / 16))) / normal_height
        normal = render_pose(normal_source, scale, size, align_bottom=False)
        pressed = render_pose(pressed_source, scale, size, align_bottom=True)
        normal_frames.append((normal, (0, 0)))
        pressed_frames.append((pressed, (0, 0)))

        if size == 64:
            preview.alpha_composite(normal.resize((128, 128)), (16, 16))
            preview.alpha_composite(pressed.resize((128, 128)), (176, 16))
            normal.save(OUTPUT / "glove-normal-64.png")
            pressed.save(OUTPUT / "glove-pressed-64.png")

    write_cursor(OUTPUT / "glove-normal.cur", normal_frames)
    write_cursor(OUTPUT / "glove-pressed.cur", pressed_frames)

    ibeam_frames = [
        (hearth_ibeam(size), (size // 2, size // 2))
        for size in SIZES
    ]
    write_cursor(OUTPUT / "glove-ibeam.cur", ibeam_frames)
    ibeam_frames[-1][0].save(OUTPUT / "glove-ibeam-64.png")

    busy_frames = [
        [
            (
                spinner_frame(
                    size,
                    phase,
                    (size / 2, size / 2),
                    size * 18 / 64,
                    size * 3.2 / 64,
                ),
                (size // 2, size // 2),
            )
            for size in SIZES
        ]
        for phase in range(ANIMATION_FRAMES)
    ]
    write_animated_cursor(OUTPUT / "glove-busy.ani", busy_frames)

    working_frames: list[list[tuple[Image.Image, tuple[int, int]]]] = []
    for phase in range(ANIMATION_FRAMES):
        resolutions: list[tuple[Image.Image, tuple[int, int]]] = []
        for size, (normal, _) in zip(SIZES, normal_frames):
            working = Image.new("RGBA", (size, size), (0, 0, 0, 0))
            working.alpha_composite(normal)
            ring = spinner_frame(
                size,
                phase,
                (size * 49 / 64, size * 48 / 64),
                size * 10 / 64,
                size * 2.2 / 64,
            )
            working.alpha_composite(ring)
            resolutions.append((working, (0, 0)))
        working_frames.append(resolutions)
    write_animated_cursor(OUTPUT / "glove-working.ani", working_frames)

    busy_preview = busy_frames[0][-1][0]
    working_preview = working_frames[0][-1][0]
    busy_preview.save(OUTPUT / "glove-busy-64.png")
    working_preview.save(OUTPUT / "glove-working-64.png")
    preview.alpha_composite(busy_preview.resize((128, 128)), (336, 16))
    preview.alpha_composite(working_preview.resize((128, 128)), (496, 16))
    preview.save(OUTPUT / "preview.png", optimize=True)

    gif_frames: list[Image.Image] = []
    for busy, working in zip(busy_frames, working_frames):
        frame = Image.new("RGBA", (320, 160), (25, 20, 16, 255))
        frame.alpha_composite(busy[-1][0].resize((128, 128)), (16, 16))
        frame.alpha_composite(working[-1][0].resize((128, 128)), (176, 16))
        gif_frames.append(frame.convert("P", palette=Image.Palette.ADAPTIVE))
    gif_frames[0].save(
        OUTPUT / "loading-preview.gif",
        save_all=True,
        append_images=gif_frames[1:],
        duration=67,
        loop=0,
        optimize=False,
    )


if __name__ == "__main__":
    main()
