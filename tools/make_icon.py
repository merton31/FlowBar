# Значок FlowBar: tools\make_icon.py <картинка> -> assets\logo.png и assets\FlowBar.ico
# Вырезает скруглённый квадрат, фон вокруг делает прозрачным.
import sys, os
from PIL import Image, ImageDraw, ImageFilter
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
im = Image.open(sys.argv[1]).convert('RGBA')
w, h = im.size
bg = im.getpixel((3, 3))[:3]
def far(p): return sum(abs(a - b) for a, b in zip(p[:3], bg)) > 30
# края квадрата: первая/последняя строка и столбец, заметно отличающиеся от фона
cols = [x for x in range(w) if sum(far(im.getpixel((x, y))) for y in range(0, h, 4)) > h // 16]
rows = [y for y in range(h) if sum(far(im.getpixel((x, y))) for x in range(0, w, 4)) > w // 16]
l, r, t, b = cols[0], cols[-1], rows[0], rows[-1]
print('box', l, t, r, b)
s = max(r - l, b - t)
cx, cy = (l + r) // 2, (t + b) // 2
im = im.crop((cx - s // 2, cy - s // 2, cx - s // 2 + s, cy - s // 2 + s))
# маска со скруглением, сглаженная в 4 раза большем размере
k = 4; m = Image.new('L', (s * k, s * k), 0)
ImageDraw.Draw(m).rounded_rectangle((0, 0, s * k - 1, s * k - 1), radius=int(s * k * 0.235), fill=255)
im.putalpha(m.resize((s, s), Image.LANCZOS))
big = im.resize((256, 256), Image.LANCZOS)
os.makedirs(os.path.join(root, 'assets'), exist_ok=True)
big.save(os.path.join(root, 'assets', 'logo.png'))
big.save(os.path.join(root, 'assets', 'FlowBar.ico'), sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
print('ok')
