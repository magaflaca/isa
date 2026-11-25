import sys
from PIL import Image, ImageDraw, ImageChops
src, out = sys.argv[1], sys.argv[2]
cover = Image.open(src).convert('RGB').resize((800, 600), Image.LANCZOS)
cover.save(out + '/GtaSix.png', optimize=True)

S = 4
W, H = 520 * S, 320 * S
def body():
    m = Image.new('L', (W, H), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle((96*S, 46*S, 424*S, 214*S), radius=78*S, fill=255)
    for cx, cy, ang in ((150, 196, -20), (370, 196, 20)):
        g = Image.new('L', (200*S, 300*S), 0)
        ImageDraw.Draw(g).rounded_rectangle((42*S, 40*S, 158*S, 250*S), radius=58*S, fill=255)
        g = g.rotate(ang, resample=Image.BICUBIC, expand=False)
        m.paste(255, ((cx-100)*S, (cy-150)*S), g)
    return m
m = body()
hole = Image.new('L', (W, H), 0)
h = ImageDraw.Draw(hole)
# d-pad
cx, cy = 156, 128
h.rounded_rectangle(((cx-34)*S, (cy-11)*S, (cx+34)*S, (cy+11)*S), radius=5*S, fill=255)
h.rounded_rectangle(((cx-11)*S, (cy-34)*S, (cx+11)*S, (cy+34)*S), radius=5*S, fill=255)
# face buttons
for bx, by in ((364, 96), (396, 128), (332, 128), (364, 160)):
    h.ellipse(((bx-14)*S, (by-14)*S, (bx+14)*S, (by+14)*S), fill=255)
# sticks: rings
for sx, sy in ((214, 178), (306, 178)):
    h.ellipse(((sx-30)*S, (sy-30)*S, (sx+30)*S, (sy+30)*S), fill=255)
# small center buttons
for sx in (236, 284):
    h.rounded_rectangle(((sx-12)*S, 92*S, (sx+12)*S, 103*S), radius=6*S, fill=255)
m = ImageChops.subtract(m, hole)
st = ImageDraw.Draw(m)
for sx, sy in ((214, 178), (306, 178)):
    st.ellipse(((sx-19)*S, (sy-19)*S, (sx+19)*S, (sy+19)*S), fill=255)
img = Image.new('RGBA', (W, H), (255, 255, 255, 0))
img.putalpha(m)
img = img.resize((W // S, H // S), Image.LANCZOS)
img.save(out + '/GtaPad.png', optimize=True)

# keyboard
W2, H2 = 360 * S, 160 * S
k = Image.new('L', (W2, H2), 0)
d = ImageDraw.Draw(k)
d.rounded_rectangle((4*S, 4*S, 356*S, 156*S), radius=22*S, fill=255)
cut = Image.new('L', (W2, H2), 0)
c = ImageDraw.Draw(cut)
rows = [(24, 12, 0), (58, 11, 12), (92, 10, 24)]
for y, n, off in rows:
    for i in range(n):
        x = 24 + off + i * 26
        c.rounded_rectangle((x*S, y*S, (x+20)*S, (y+24)*S), radius=4*S, fill=255)
c.rounded_rectangle((90*S, 126*S, 270*S, 140*S), radius=5*S, fill=255)
k = ImageChops.subtract(k, cut)
img = Image.new('RGBA', (W2, H2), (255, 255, 255, 0))
img.putalpha(k)
img.resize((W2 // S, H2 // S), Image.LANCZOS).save(out + '/GtaKeys.png', optimize=True)
print('ok')

full = Image.open(src).convert('RGB')
w, h = full.size
cw = int(h * 128 / 136)
x0 = (w - cw) // 2
full.crop((x0, 0, x0 + cw, h)).resize((256, 272), Image.LANCZOS).save(out + '/GtaCard.png', optimize=True)
print('card ok')

