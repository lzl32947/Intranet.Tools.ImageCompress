"""Make fresh, repeatable image fixtures outside the source tree.
Usage: python tests/make_fixtures.py PATH (Pillow, numpy and pillow-heif).
"""
import os
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageCms, ImageDraw, ImageFont
from pillow_heif import register_heif_opener

register_heif_opener()
folder = Path(sys.argv[1])
folder.mkdir(parents=True, exist_ok=True)
rng = np.random.default_rng(451)
windows = Path(os.environ["WINDIR"])
wallpaper = windows / "Web/Wallpaper/Windows/img0.jpg"
photo = Image.open(wallpaper).convert("RGB") if wallpaper.is_file() else Image.fromarray(
    rng.integers(0, 255, (1800, 2400, 3), dtype=np.uint8))
photo.thumbnail((2100, 1500), Image.Resampling.LANCZOS)
orientation = Image.Exif()
orientation[274] = 6
srgb = ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB")).tobytes()
photo.save(folder / "手机 竖拍.jpg", quality=99, subsampling=0, exif=orientation, icc_profile=srgb)
photo.resize((55, 42)).save(folder / "已达标.jpeg", quality=86)
small_jpeg = (folder / "已达标.jpeg").read_bytes()
for size, name in [(500000, "正好500KB.jpg"), (500001, "多一字节.jpg")]:
    extra = size - len(small_jpeg)
    comments = bytearray()
    while extra:
        segment = min(65535, extra)
        comments.extend(b"\xff\xfe" + (segment - 2).to_bytes(2, "big") + bytes(segment - 4))
        extra -= segment
    (folder / name).write_bytes(small_jpeg[:2] + comments + small_jpeg[2:])
photo.resize((160, 112)).save(folder / "phone.heic", quality=80)
(folder / "phone.heif").write_bytes((folder / "phone.heic").read_bytes())
photo.resize((160, 112)).save(folder / "sample.avif", quality=75)
photo.resize((160, 112)).save(folder / "sample.tiff")
photo.resize((160, 112)).save(folder / "sample.gif")
photo.resize((160, 112)).save(folder / "sample.bmp")
photo.resize((160, 112)).save(folder / "small.png")
pts = Image.fromarray(rng.integers(0, 256, (800, 1200, 3), dtype=np.uint8))
pts.save(folder / "高细节.jpg", quality=99, subsampling=0)
pts.thumbnail((160, 120))
pts.save(folder / "pages.tif", save_all=True, append_images=[Image.new("RGB", pts.size, "blue")])
a = Image.new("RGBA", (100, 75), "red")
b = Image.new("RGBA", (100, 75), "blue")
for name in ("动图.png", "动图.webp", "动图.gif", "动图.avif"):
    a.save(folder / name, save_all=True, append_images=[b], duration=200, loop=0)

w, h = 920, 500
yy, xx = np.indices((h, w))
arr = np.stack([(xx * 173 // w + 20), (yy * 180 // h + 25), (xx + yy) % 170 + 50,
                np.clip((xx - 50) * 255 // (w - 130), 0, 255)], axis=2).astype(np.uint8)
rgba = Image.fromarray(arr)
draw = ImageDraw.Draw(rgba)
font = ImageFont.truetype(str(windows / "Fonts/msyh.ttc"), 15)
for row in range(11):
    draw.text((35 + row * 3, 15 + row * 42), "压缩图片  File 0123456789 透明渐变", font=font, fill=(25, 50, 80, 220))
rgba.save(folder / "透明 边缘.png", compress_level=0)
rgba.resize((160, 105)).save(folder / "alpha.webp", lossless=True)
screen = Image.new("RGB", (1040, 620), "#f3f7f2")
draw = ImageDraw.Draw(screen)
for row in range(24):
    draw.text((22, 12 + row * 25), f"{row + 1:02}  小字截图    Status 123456789    文件输出大小", font=font, fill="#17493e")
screen.save(folder / "小字截图.png", compress_level=0)
grad = ((xx * 47 + yy * 89) % 65536).astype(np.uint16)
Image.fromarray(grad).save(folder / "十六位.png", compress_level=0)
(folder / "损坏.png").write_bytes(b"\x89PNG\r\n\x1a\n" + b"\x00" * 15)
print(f"{len(list(folder.iterdir()))} fixtures in {folder}")
