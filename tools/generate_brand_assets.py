from __future__ import annotations

from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "BetterCapture.App" / "Assets"
SOURCE = ASSETS / "BetterCaptureLogo-Source.png"
MASTER = ASSETS / "BetterCaptureLogo-1024.png"

PALETTE = np.array(
    [
        (0, 188, 235),       # cyan
        (255, 177, 0),       # amber
        (29, 38, 51),        # graphite
        (255, 255, 255),     # aperture center
    ],
    dtype=np.int16,
)


def remove_small_components(mask: np.ndarray) -> np.ndarray:
    height, width = mask.shape
    minimum_area = max(256, int(width * height * 0.0015))
    visited = np.zeros_like(mask, dtype=bool)
    keep = np.zeros_like(mask, dtype=bool)

    for y in range(height):
        for x in range(width):
            if not mask[y, x] or visited[y, x]:
                continue

            queue: deque[tuple[int, int]] = deque([(x, y)])
            visited[y, x] = True
            component: list[tuple[int, int]] = []

            while queue:
                current_x, current_y = queue.popleft()
                component.append((current_x, current_y))
                for next_x, next_y in (
                    (current_x - 1, current_y),
                    (current_x + 1, current_y),
                    (current_x, current_y - 1),
                    (current_x, current_y + 1),
                ):
                    if (
                        0 <= next_x < width
                        and 0 <= next_y < height
                        and mask[next_y, next_x]
                        and not visited[next_y, next_x]
                    ):
                        visited[next_y, next_x] = True
                        queue.append((next_x, next_y))

            if len(component) >= minimum_area:
                xs, ys = zip(*component)
                keep[np.asarray(ys), np.asarray(xs)] = True

    return keep


def clean_master() -> Image.Image:
    source = Image.open(SOURCE).convert("RGBA")
    source = source.resize((1024, 1024), Image.Resampling.LANCZOS)
    pixels = np.asarray(source).copy()
    alpha = pixels[:, :, 3]
    keep = remove_small_components(alpha >= 40)
    pixels[:, :, 3] = np.where(keep, alpha, 0)

    opaque = pixels[:, :, 3] > 0
    rgb = pixels[:, :, :3].astype(np.int16)
    distances = ((rgb[:, :, None, :] - PALETTE[None, None, :, :]) ** 2).sum(axis=3)
    nearest = distances.argmin(axis=2)
    pixels[:, :, :3][opaque] = PALETTE[nearest[opaque]].astype(np.uint8)
    pixels[:, :, :3][~opaque] = 0

    master = Image.fromarray(pixels, mode="RGBA")
    master.save(MASTER, optimize=True)
    return master


def centered_canvas(master: Image.Image, size: tuple[int, int], fill_ratio: float) -> Image.Image:
    width, height = size
    logo_size = max(1, round(min(width, height) * fill_ratio))
    logo = master.resize((logo_size, logo_size), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", size, (0, 0, 0, 0))
    canvas.alpha_composite(logo, ((width - logo_size) // 2, (height - logo_size) // 2))
    return canvas


def save_png(master: Image.Image, name: str, size: tuple[int, int], fill_ratio: float) -> None:
    centered_canvas(master, size, fill_ratio).save(ASSETS / name, optimize=True)


def main() -> None:
    master = clean_master()

    save_png(master, "LockScreenLogo.scale-200.png", (48, 48), 0.86)
    save_png(master, "SplashScreen.scale-200.png", (1240, 600), 0.70)
    save_png(master, "Square150x150Logo.scale-200.png", (300, 300), 0.84)
    save_png(master, "Square44x44Logo.scale-200.png", (88, 88), 0.86)
    save_png(master, "Square44x44Logo.targetsize-24_altform-unplated.png", (24, 24), 0.92)
    save_png(master, "Square44x44Logo.targetsize-48_altform-lightunplated.png", (48, 48), 0.90)
    save_png(master, "StoreLogo.png", (50, 50), 0.86)
    save_png(master, "Wide310x150Logo.scale-200.png", (620, 300), 0.70)

    master.save(
        ASSETS / "AppIcon.ico",
        format="ICO",
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
    )


if __name__ == "__main__":
    main()
