import sys
from PIL import Image
keyart, out = sys.argv[1], sys.argv[2]
k = Image.open(keyart).convert('RGB')
print('keyart', k.size)
if k.width > 1600:
    k = k.resize((1600, k.height * 1600 // k.width), Image.LANCZOS)
k.save(out + '/GtaKeyArt.jpg', quality=92, optimize=True)
