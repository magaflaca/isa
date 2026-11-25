import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
src, out = sys.argv[1], sys.argv[2]
img = Image.open(src).convert('RGB')
a = np.asarray(img).astype(np.float32)
H, W, _ = a.shape
x0, x1, y0, y1 = 270, 885, 215, 690
left = a[:, 230:275].mean(axis=1)
right = a[:, 885:930].mean(axis=1)
t = (np.arange(W) - 252.0) / (907.0 - 252.0)
t = np.clip(t, 0, 1)[None, :, None]
bg = left[:, None, :] * (1 - t) + right[:, None, :] * t
diff = np.sqrt(((a - bg) ** 2).sum(axis=2))
hit = (diff > 70).astype(np.uint8) * 255
region = np.zeros_like(hit)
region[y0:y1, x0:x1] = hit[y0:y1, x0:x1]
m = Image.fromarray(region).filter(ImageFilter.MaxFilter(3))
pad = Image.new('L', (W + 2, H + 2), 0)
pad.paste(m, (1, 1))
ImageDraw.floodfill(pad, (0, 0), 128)
p = np.asarray(pad)[1:-1, 1:-1]
solid = np.where(p == 128, 0, 255).astype(np.uint8)
mask = Image.fromarray(solid).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(1.2))
logo = img.copy()
logo.putalpha(mask)
logo = logo.crop((x0 - 10, y0 - 10, x1 + 10, y1 + 10))
logo = logo.resize((logo.width * 2 // 3, logo.height * 2 // 3), Image.LANCZOS)
logo.save(out + '/GtaLogo.png', optimize=True)
print(logo.size)
